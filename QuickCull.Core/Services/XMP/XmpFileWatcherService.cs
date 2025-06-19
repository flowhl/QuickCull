using QuickCull.Core.Services.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Core.Services.XMP
{
    public class XmpFileWatcherService : IXmpFileWatcherService
    {
        private FileSystemWatcher _fileWatcher;
        private readonly ConcurrentDictionary<string, DateTime> _lastProcessedTimes;
        private readonly Timer _debounceTimer;
        private readonly ConcurrentQueue<XmpFileChangedEventArgs> _pendingChanges;
        private readonly SemaphoreSlim _processingLock;
        private readonly ILoggingService _loggingService;

        // Debounce settings (Lightroom can trigger multiple events for one change)
        private const int DebounceDelayMs = 1000; // Wait 1 second after last change
        private const int MinTimeBetweenProcessingMs = 500; // Don't process same file more than once per 500ms

        public event EventHandler<XmpFileChangedEventArgs> XmpFileChanged;

        public bool IsWatching => _fileWatcher?.EnableRaisingEvents == true;
        public string WatchedFolder { get; private set; }

        public XmpFileWatcherService(ILoggingService loggingService = null)
        {
            _loggingService = loggingService;
            _lastProcessedTimes = new ConcurrentDictionary<string, DateTime>();
            _pendingChanges = new ConcurrentQueue<XmpFileChangedEventArgs>();
            _processingLock = new SemaphoreSlim(1, 1);

            // Timer for debouncing file system events
            _debounceTimer = new Timer(ProcessPendingChanges, null, Timeout.Infinite, Timeout.Infinite);
        }

        // Add these members to XmpFileWatcherService class
        private volatile bool _suspended = false;
        private readonly object _suspendLock = new object();

        // Add these methods to XmpFileWatcherService
        public void SuspendWatching()
        {
            lock (_suspendLock)
            {
                _suspended = true;
            }
        }

        public void ResumeWatching()
        {
            lock (_suspendLock)
            {
                _suspended = false;
            }
        }

        public async Task StartWatchingAsync(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("Folder path cannot be null or empty", nameof(folderPath));

            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

            await StopWatchingAsync();

            try
            {
                WatchedFolder = folderPath;

                _fileWatcher = new FileSystemWatcher(folderPath)
                {
                    Filter = "*.xmp",
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
                    IncludeSubdirectories = false, // Only watch the main folder, not subfolders
                    EnableRaisingEvents = false // Will enable after setting up events
                };

                // Subscribe to events
                _fileWatcher.Created += OnXmpFileEvent;
                _fileWatcher.Changed += OnXmpFileEvent;
                _fileWatcher.Deleted += OnXmpFileEvent;
                _fileWatcher.Renamed += OnXmpFileRenamed;
                _fileWatcher.Error += OnWatcherError;

                // Start watching
                _fileWatcher.EnableRaisingEvents = true;

                await _loggingService?.LogInfoAsync($"Started watching XMP files in: {folderPath}");
            }
            catch (Exception ex)
            {
                await _loggingService?.LogErrorAsync($"Failed to start watching folder: {folderPath}", ex);
                throw;
            }
        }

        public async Task StopWatchingAsync()
        {
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Created -= OnXmpFileEvent;
                _fileWatcher.Changed -= OnXmpFileEvent;
                _fileWatcher.Deleted -= OnXmpFileEvent;
                _fileWatcher.Renamed -= OnXmpFileRenamed;
                _fileWatcher.Error -= OnWatcherError;

                _fileWatcher.Dispose();
                _fileWatcher = null;

                await _loggingService?.LogInfoAsync($"Stopped watching XMP files in: {WatchedFolder}");
                WatchedFolder = null;
            }

            // Stop debounce timer
            _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);

            // Process any remaining pending changes
            await ProcessPendingChangesAsync();

            // Clear state
            _lastProcessedTimes.Clear();
            while (_pendingChanges.TryDequeue(out _)) { }
        }

        private void OnXmpFileEvent(object sender, FileSystemEventArgs e)
        {
            try
            {
                var changeType = e.ChangeType switch
                {
                    WatcherChangeTypes.Created => FileChangeType.Created,
                    WatcherChangeTypes.Changed => FileChangeType.Modified,
                    WatcherChangeTypes.Deleted => FileChangeType.Deleted,
                    _ => FileChangeType.Modified
                };

                QueueFileChange(e.FullPath, changeType);
            }
            catch (Exception ex)
            {
                _loggingService?.LogErrorAsync($"Error processing XMP file event: {e.FullPath}", ex);
            }
        }

        private void OnXmpFileRenamed(object sender, RenamedEventArgs e)
        {
            try
            {
                // Treat rename as delete old + create new
                QueueFileChange(e.OldFullPath, FileChangeType.Deleted);
                QueueFileChange(e.FullPath, FileChangeType.Created);
            }
            catch (Exception ex)
            {
                _loggingService?.LogErrorAsync($"Error processing XMP file rename: {e.OldFullPath} -> {e.FullPath}", ex);
            }
        }

        private async void OnWatcherError(object sender, ErrorEventArgs e)
        {
            await _loggingService?.LogErrorAsync("FileSystemWatcher error", e.GetException());

            // Try to restart the watcher if it failed
            if (!string.IsNullOrEmpty(WatchedFolder))
            {
                try
                {
                    await Task.Delay(2000); // Wait a bit before restarting
                    await StartWatchingAsync(WatchedFolder);
                    await _loggingService?.LogInfoAsync("FileSystemWatcher restarted successfully");
                }
                catch (Exception restartEx)
                {
                    await _loggingService?.LogErrorAsync("Failed to restart FileSystemWatcher", restartEx);
                }
            }
        }

        private void QueueFileChange(string xmpFilePath, FileChangeType changeType)
        {
            if (string.IsNullOrEmpty(xmpFilePath) || !xmpFilePath.EndsWith(".xmp", StringComparison.OrdinalIgnoreCase))
                return;

            // Check if watching is suspended
            lock (_suspendLock)
            {
                if (_suspended)
                {
                    return; // Skip processing while suspended
                }
            }

            // ... rest of existing QueueFileChange method
            var now = DateTime.Now;
            var key = $"{xmpFilePath}_{changeType}";

            if (_lastProcessedTimes.TryGetValue(key, out var lastProcessed) &&
                (now - lastProcessed).TotalMilliseconds < MinTimeBetweenProcessingMs)
            {
                return;
            }

            var imageFilePath = xmpFilePath.Substring(0, xmpFilePath.Length - 4);
            var imageFilename = Path.GetFileName(imageFilePath);
            imageFilePath = Directory
                                .GetFiles(Path.GetDirectoryName(imageFilePath))
                                .Where(x => Path.GetFileNameWithoutExtension(x) == imageFilename)
                                .FirstOrDefault(x => !Path.GetExtension(x).ToLower().Contains("xmp"));
            imageFilename = Path.GetFileName(imageFilePath);

            var changeEvent = new XmpFileChangedEventArgs
            {
                XmpFilePath = xmpFilePath,
                ImageFilePath = imageFilePath,
                ImageFilename = imageFilename,
                ChangeType = changeType,
                Timestamp = now
            };

            _pendingChanges.Enqueue(changeEvent);
            _debounceTimer.Change(DebounceDelayMs, Timeout.Infinite);
        }

        private void ProcessPendingChanges(object state)
        {
            // Use async method but don't await (timer callback can't be async)
            _ = Task.Run(ProcessPendingChangesAsync);
        }

        private async Task ProcessPendingChangesAsync()
        {
            await _processingLock.WaitAsync();
            try
            {
                var processedFiles = new HashSet<string>();

                while (_pendingChanges.TryDequeue(out var changeEvent))
                {
                    try
                    {
                        // Only process each file once per batch (take the latest change)
                        var fileKey = changeEvent.ImageFilename.ToLowerInvariant();
                        if (processedFiles.Contains(fileKey))
                            continue;

                        processedFiles.Add(fileKey);

                        // Update last processed time
                        var key = $"{changeEvent.XmpFilePath}_{changeEvent.ChangeType}";
                        _lastProcessedTimes.AddOrUpdate(key, changeEvent.Timestamp, (_, _) => changeEvent.Timestamp);

                        // Fire the event
                        XmpFileChanged?.Invoke(this, changeEvent);

                        await _loggingService?.LogInfoAsync(
                            $"XMP file {changeEvent.ChangeType.ToString().ToLower()}: {changeEvent.ImageFilename}");
                    }
                    catch (Exception ex)
                    {
                        await _loggingService?.LogErrorAsync(
                            $"Error processing XMP change for {changeEvent.ImageFilename}", ex);
                    }
                }
            }
            finally
            {
                _processingLock.Release();
            }
        }

        public void Dispose()
        {
            StopWatchingAsync().GetAwaiter().GetResult();
            _debounceTimer?.Dispose();
            _processingLock?.Dispose();
        }
    }
}
