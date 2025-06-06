using ImageCullingTool.Core.Services.Logging;
using ImageCullingTool.Models;
using ImageCullingTool.Core.Services.Analysis;
using ImageCullingTool.Core.Services.Cache;
using ImageCullingTool.Core.Services.FileSystem;
using ImageCullingTool.Core.Services.XMP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ImageCullingTool.Core.Services.Thumbnail;

namespace ImageCullingTool.Core.Services.ImageCulling
{
    public class ImageCullingService : IImageCullingService, IDisposable
    {
        private readonly IAnalysisService _analysisService;
        private readonly ICacheService _cacheService;
        private readonly IXmpService _xmpService;
        private readonly IFileSystemService _fileSystemService;
        private readonly ILoggingService _loggingService;
        private readonly IXmpFileWatcherService _fileWatcherService;
        private readonly IThumbnailService _thumbnailService;

        private string _currentFolderPath;
        private bool _isInitialized;
        private readonly SemaphoreSlim _operationSemaphore;

        // Events for UI updates
        public event EventHandler<XmpFileChangedEventArgs> XmpFileChanged;

        public string CurrentFolderPath => _currentFolderPath;

        public ImageCullingService(
            IAnalysisService analysisService,
            ICacheService cacheService,
            IXmpService xmpService,
            IFileSystemService fileSystemService,
            IThumbnailService thumbnailService,
            IXmpFileWatcherService fileWatcherService = null,
            ILoggingService loggingService = null
            )
        {
            _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _xmpService = xmpService ?? throw new ArgumentNullException(nameof(xmpService));
            _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
            _fileWatcherService = fileWatcherService; // Optional
            _loggingService = loggingService; // Optional
            _thumbnailService = thumbnailService; // Optional

            _operationSemaphore = new SemaphoreSlim(1, 1); // Prevent concurrent operations

            // Subscribe to file watcher events if available
            if (_fileWatcherService != null)
            {
                _fileWatcherService.XmpFileChanged += OnFileWatcherXmpChanged;
            }
        }

        public async Task LoadFolderAsync(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("Folder path cannot be null or empty", nameof(folderPath));

            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

            await _operationSemaphore.WaitAsync();
            try
            {
                await _loggingService?.LogInfoAsync($"Loading folder: {folderPath}");

                // Initialize analysis service if not already done
                if (!_isInitialized)
                {
                    await _analysisService.InitializeAsync(new AnalysisConfiguration
                    {
                        EnableGpuAcceleration = true,
                        BatchSize = 4,
                        WriteXmpFiles = true
                    });
                    _isInitialized = true;
                }

                // Initialize cache service for this folder
                await _cacheService.InitializeAsync(folderPath);

                _currentFolderPath = folderPath;

                // Start watching XMP files for changes
                if (_fileWatcherService != null)
                {
                    await _fileWatcherService.StartWatchingAsync(folderPath);
                }

                await _loggingService?.LogInfoAsync($"Successfully loaded folder with cache validation");
            }
            catch (Exception ex)
            {
                await _loggingService?.LogErrorAsync($"Failed to load folder {folderPath}", ex);
                throw;
            }
            finally
            {
                _operationSemaphore.Release();
            }
            try
            {
                // Generate thumbnails for all images in the folder
                await _thumbnailService.GenerateThumbnailsAsync(folderPath);
                await _loggingService?.LogInfoAsync("Thumbnails generated for all images");
            }
            catch (Exception ex)
            {
                await _loggingService?.LogErrorAsync("Failed to generate thumbnails", ex);
                throw;
            }
        }

        public async Task AnalyzeAllImagesAsync(IProgress<AnalysisProgress> progress = null, CancellationToken cancellationToken = default)
        {
            EnsureFolderLoaded();

            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                await _loggingService?.LogInfoAsync("Starting analysis of all images");

                // Get unanalyzed images
                var unanalyzedImages = await _cacheService.GetUnanalyzedImagesAsync();
                var unanalyzedList = unanalyzedImages.ToList();

                if (!unanalyzedList.Any())
                {
                    await _loggingService?.LogInfoAsync("No images need analysis");
                    progress?.Report(new AnalysisProgress
                    {
                        TotalImages = 0,
                        ProcessedImages = 0,
                        CurrentImage = "No images to analyze"
                    });
                    return;
                }

                await _loggingService?.LogInfoAsync($"Found {unanalyzedList.Count} images to analyze");

                // Convert filenames to full paths
                var imagePaths = unanalyzedList.Select(filename =>
                    Path.Combine(_currentFolderPath, filename)).ToList();

                // Create progress wrapper that also updates cache
                var internalProgress = new Progress<AnalysisProgress>(async analysisProgress =>
                {
                    // Report to caller
                    progress?.Report(analysisProgress);

                    // Update cache for completed images
                    if (!string.IsNullOrEmpty(analysisProgress.CurrentImage) &&
                        analysisProgress.ProcessedImages > 0)
                    {
                        try
                        {
                            var lastCompletedPath = imagePaths[analysisProgress.ProcessedImages - 1];
                            await _cacheService.UpdateSingleImageCacheAsync(lastCompletedPath);
                        }
                        catch (Exception ex)
                        {
                            await _loggingService?.LogWarningAsync($"Failed to update cache for {analysisProgress.CurrentImage}: {ex.Message}");
                        }
                    }
                });

                // Run batch analysis
                var results = await _analysisService.AnalyzeBatchAsync(imagePaths, internalProgress, cancellationToken);
                var resultsList = results.ToList();

                // Save all results to XMP and update cache
                var successCount = 0;
                var errorCount = 0;

                foreach (var result in resultsList)
                {
                    try
                    {
                        if (result.ExtendedData?.ContainsKey("failed") != true)
                        {
                            var imagePath = Path.Combine(_currentFolderPath, result.Filename);

                            // Write to XMP (single source of truth)
                            await _xmpService.WriteAnalysisToXmpAsync(imagePath, result);

                            // Update cache from XMP
                            await _cacheService.UpdateSingleImageCacheAsync(imagePath);

                            successCount++;
                            await _loggingService?.LogAnalysisResultAsync(imagePath, result);
                        }
                        else
                        {
                            errorCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        await _loggingService?.LogErrorAsync($"Failed to save analysis for {result.Filename}", ex);
                    }
                }

                await _loggingService?.LogInfoAsync($"Analysis complete: {successCount} successful, {errorCount} failed");

                // Final progress report
                progress?.Report(new AnalysisProgress
                {
                    TotalImages = unanalyzedList.Count,
                    ProcessedImages = unanalyzedList.Count,
                    CurrentImage = $"Complete: {successCount} analyzed, {errorCount} failed"
                });
            }
            catch (OperationCanceledException)
            {
                await _loggingService?.LogInfoAsync("Analysis operation cancelled by user");
                throw;
            }
            catch (Exception ex)
            {
                await _loggingService?.LogErrorAsync("Failed to analyze all images", ex);
                throw;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        public async Task AnalyzeImageAsync(string filename)
        {
            EnsureFolderLoaded();

            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("Filename cannot be null or empty", nameof(filename));

            var imagePath = Path.Combine(_currentFolderPath, filename);
            if (!File.Exists(imagePath))
                throw new FileNotFoundException($"Image not found: {imagePath}");

            await _operationSemaphore.WaitAsync();
            try
            {
                await _loggingService?.LogInfoAsync($"Analyzing single image: {filename}");

                // Run analysis
                var result = await _analysisService.AnalyzeImageAsync(imagePath);

                // Save to XMP (single source of truth)
                await _xmpService.WriteAnalysisToXmpAsync(imagePath, result);

                // Update cache from XMP
                await _cacheService.UpdateSingleImageCacheAsync(imagePath);

                await _loggingService?.LogAnalysisResultAsync(imagePath, result);
                await _loggingService?.LogInfoAsync($"Successfully analyzed {filename}");
            }
            catch (Exception ex)
            {
                await _loggingService?.LogErrorAsync($"Failed to analyze {filename}", ex);
                throw;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        public async Task RefreshCacheAsync()
        {
            EnsureFolderLoaded();

            await _operationSemaphore.WaitAsync();
            try
            {
                await _loggingService?.LogInfoAsync("Refreshing cache from XMP files");

                var progress = new Progress<AnalysisProgress>(p =>
                    Console.WriteLine($"Cache refresh: {p.ProcessedImages}/{p.TotalImages} - {p.CurrentImage}"));

                await _cacheService.RebuildCacheFromXmpAsync(_currentFolderPath, progress);

                await _loggingService?.LogInfoAsync("Cache refresh complete");
            }
            catch (Exception ex)
            {
                await _loggingService?.LogErrorAsync("Failed to refresh cache", ex);
                throw;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        public async Task<ImageAnalysis> GetImageAnalysisAsync(string filename)
        {
            EnsureFolderLoaded();

            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("Filename cannot be null or empty", nameof(filename));

            try
            {
                return await _cacheService.GetImageAsync(filename);
            }
            catch (Exception ex)
            {
                await _loggingService?.LogErrorAsync($"Failed to get analysis for {filename}", ex);
                throw;
            }
        }

        public async Task<IEnumerable<ImageAnalysis>> GetFilteredImagesAsync(ImageFilter filter)
        {
            EnsureFolderLoaded();

            try
            {
                var results = await _cacheService.GetFilteredImagesAsync(filter ?? new ImageFilter());

                await _loggingService?.LogInfoAsync($"Filtered query returned {results.Count()} images");

                return results;
            }
            catch (Exception ex)
            {
                await _loggingService?.LogErrorAsync("Failed to get filtered images", ex);
                throw;
            }
        }

        public async Task<CacheValidationResult> ValidateFolderAsync()
        {
            EnsureFolderLoaded();

            try
            {
                var result = await _cacheService.ValidateCacheAsync(_currentFolderPath);

                await _loggingService?.LogInfoAsync($"Cache validation: {(result.IsValid ? "Valid" : "Invalid")} - {result.Issues.Count} issues");

                return result;
            }
            catch (Exception ex)
            {
                await _loggingService?.LogErrorAsync("Failed to validate folder", ex);
                throw;
            }
        }

        public async Task<FolderStatistics> GetFolderStatisticsAsync()
        {
            EnsureFolderLoaded();

            try
            {
                var allImages = await _cacheService.GetAllImagesAsync();
                var imagesList = allImages.ToList();
                var imagesWithSharpness = imagesList.Where(i => i.SharpnessOverall.HasValue);

                var stats = new FolderStatistics
                {
                    TotalImages = imagesList.Count,
                    AnalyzedImages = imagesList.Count(i => i.AnalysisDate.HasValue),
                    UnanalyzedImages = imagesList.Count(i => !i.AnalysisDate.HasValue),
                    ImagesWithXmp = imagesList.Count(i => i.HasXmp),
                    RawImages = imagesList.Count(i => i.IsRaw),

                    // Rating distribution
                    RatingDistribution = new Dictionary<int, int>
                    {
                        [1] = imagesList.Count(i => (i.LightroomRating ?? i.PredictedRating) == 1),
                        [2] = imagesList.Count(i => (i.LightroomRating ?? i.PredictedRating) == 2),
                        [3] = imagesList.Count(i => (i.LightroomRating ?? i.PredictedRating) == 3),
                        [4] = imagesList.Count(i => (i.LightroomRating ?? i.PredictedRating) == 4),
                        [5] = imagesList.Count(i => (i.LightroomRating ?? i.PredictedRating) == 5)
                    },

                    // Quality metrics
                    AverageSharpness = !imagesWithSharpness.Any() ? 0 : imagesWithSharpness
                        .Average(i => i.SharpnessOverall.Value),
                    HighQualityImages = imagesList.Count(i =>
                        (i.LightroomRating ?? i.PredictedRating ?? 0) >= 4 &&
                        (i.SharpnessOverall ?? 0) >= 0.7),

                    TotalFileSize = imagesList.Sum(i => i.FileSize),
                    FolderPath = _currentFolderPath
                };

                return stats;
            }
            catch (Exception ex)
            {
                await _loggingService?.LogErrorAsync("Failed to get folder statistics", ex);
                throw;
            }
        }

        public async Task<IEnumerable<string>> GetRecommendedKeepersAsync(int maxCount = 50)
        {
            EnsureFolderLoaded();

            try
            {
                // Get high-quality images based on multiple criteria
                var candidates = await _cacheService.GetFilteredImagesAsync(new ImageFilter
                {
                    MinRating = 3,
                    MinSharpness = 0.6,
                    AnalyzedOnly = true
                });

                var keepers = candidates
                    .OrderByDescending(i => i.LightroomRating ?? i.PredictedRating ?? 0)
                    .ThenByDescending(i => i.SharpnessOverall ?? 0)
                    .ThenByDescending(i => i.EyesOpen == true ? 1 : 0) // Prefer open eyes
                    .ThenByDescending(i => i.SubjectCount ?? 0) // Prefer photos with subjects
                    .Take(maxCount)
                    .Select(i => i.Filename)
                    .ToList();

                await _loggingService?.LogInfoAsync($"Recommended {keepers.Count} keepers out of {candidates.Count()} candidates");

                return keepers;
            }
            catch (Exception ex)
            {
                await _loggingService?.LogErrorAsync("Failed to get recommended keepers", ex);
                throw;
            }
        }

        private async void OnFileWatcherXmpChanged(object sender, XmpFileChangedEventArgs e)
        {
            try
            {
                await _loggingService?.LogInfoAsync($"XMP file changed: {e.ImageFilename} ({e.ChangeType})");

                // Update cache for the affected image
                if (e.ChangeType == FileChangeType.Deleted)
                {
                    // XMP was deleted - update cache to reflect this
                    await _cacheService.UpdateSingleImageCacheAsync(e.ImageFilePath);
                }
                else if (e.ChangeType == FileChangeType.Created || e.ChangeType == FileChangeType.Modified)
                {
                    // XMP was created or modified - read new data and update cache
                    await _cacheService.UpdateSingleImageCacheAsync(e.ImageFilePath);
                }

                // Forward the event to UI
                XmpFileChanged?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                await _loggingService?.LogErrorAsync($"Error handling XMP file change for {e.ImageFilename}", ex);
            }
        }


        private void EnsureFolderLoaded()
        {
            if (string.IsNullOrEmpty(_currentFolderPath))
                throw new InvalidOperationException("No folder loaded. Call LoadFolderAsync first.");
        }

        public void Dispose()
        {
            _operationSemaphore?.Dispose();

            if (_cacheService is IDisposable disposableCache)
                disposableCache.Dispose();
        }
    }
}