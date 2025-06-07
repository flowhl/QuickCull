using ImageCullingTool.Models;
using ImageCullingTool.Core.Services.FileSystem;
using ImageCullingTool.Core.Services.XMP;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ImageCullingTool.Core.Services.Logging;

namespace ImageCullingTool.Core.Services.Cache
{
    public class CacheService : ICacheService
    {
        private readonly IFileSystemService _fileSystemService;
        private readonly ILoggingService _loggingService;
        private readonly IXmpService _xmpService;
        private string _currentFolderPath;
        private CullingDbContext _context;

        public CacheService(IFileSystemService fileSystemService, IXmpService xmpService, ILoggingService loggingService)
        {
            _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
            _xmpService = xmpService ?? throw new ArgumentNullException(nameof(xmpService));
            _loggingService = loggingService;
        }

        public async Task InitializeAsync(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("Folder path cannot be null or empty", nameof(folderPath));

            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

            _currentFolderPath = folderPath;

            // Dispose existing context if any
            _context?.Dispose();

            // Create new context for this folder
            _context = new CullingDbContext(folderPath, _loggingService);

            // Ensure database exists
            await _context.EnsureDatabaseCreatedAsync();

            // Validate cache on initialization
            var validation = await ValidateCacheAsync(folderPath);
            if (!validation.IsValid)
            {
                // Auto-rebuild if cache is invalid
                await RebuildCacheFromXmpAsync(folderPath);
            }
        }

        public async Task RebuildCacheFromXmpAsync(string folderPath, IProgress<AnalysisProgress> progress = null)
        {
            if (_context == null)
                throw new InvalidOperationException("Cache service not initialized. Call InitializeAsync first.");

            var imageFiles = await _fileSystemService.ScanFolderAsync(folderPath);
            var imageFilesList = imageFiles.ToList();

            progress?.Report(new AnalysisProgress
            {
                TotalImages = imageFilesList.Count,
                ProcessedImages = 0,
                CurrentImage = "Clearing cache..."
            });

            // Clear existing cache and reset context to avoid tracking issues
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM Images");

            // Clear EF change tracker to avoid conflicts
            _context.ChangeTracker.Clear();

            var processedCount = 0;
            var startTime = DateTime.Now;
            var batch = new List<ImageAnalysis>();

            foreach (var imageFile in imageFilesList)
            {
                var imageAnalysis = await CreateImageAnalysisFromFileAsync(imageFile);
                batch.Add(imageAnalysis);

                processedCount++;

                // Report progress
                if (progress != null)
                {
                    var elapsed = DateTime.Now - startTime;
                    var estimatedTotal = processedCount > 0 ?
                        TimeSpan.FromTicks(elapsed.Ticks * imageFilesList.Count / processedCount) :
                        TimeSpan.Zero;
                    var remaining = estimatedTotal - elapsed;

                    progress.Report(new AnalysisProgress
                    {
                        TotalImages = imageFilesList.Count,
                        ProcessedImages = processedCount,
                        CurrentImage = imageFile.Filename,
                        ElapsedTime = elapsed,
                        EstimatedTimeRemaining = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero
                    });
                }

                // Batch save every 100 items for performance
                if (batch.Count >= 100)
                {
                    _context.Images.AddRange(batch);
                    await _context.SaveChangesAsync();
                    _context.ChangeTracker.Clear(); // Clear tracking after save
                    batch.Clear();
                }
            }

            // Save remaining items
            if (batch.Any())
            {
                _context.Images.AddRange(batch);
                await _context.SaveChangesAsync();
                _context.ChangeTracker.Clear();
            }

            progress?.Report(new AnalysisProgress
            {
                TotalImages = imageFilesList.Count,
                ProcessedImages = imageFilesList.Count,
                CurrentImage = "Cache rebuild complete",
                ElapsedTime = DateTime.Now - startTime
            });
        }

        public async Task UpdateSingleImageCacheAsync(string imagePath)
        {
            if (_context == null)
                throw new InvalidOperationException("Cache service not initialized. Call InitializeAsync first.");

            var filename = Path.GetFileName(imagePath);

            // Use AsNoTracking to avoid tracking conflicts, then handle separately
            var existingEntry = await _context.Images
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Filename == filename);

            if (File.Exists(imagePath))
            {
                // Image exists - update or create cache entry
                var fileInfo = new FileInfo(imagePath);
                var imageFileInfo = new ImageFileInfo
                {
                    FullPath = imagePath,
                    Filename = filename,
                    FileSize = fileInfo.Length,
                    ModifiedDate = fileInfo.LastWriteTime,
                    Format = Path.GetExtension(imagePath).ToLowerInvariant(),
                    IsRaw = _fileSystemService.IsImageSupported(imagePath) && IsRawFormat(imagePath),
                    HasXmp = await _fileSystemService.FileExistsAsync(_fileSystemService.GetXmpPath(imagePath)),
                    FileHash = await _fileSystemService.CalculateFileHashAsync(imagePath)
                };

                var updatedAnalysis = await CreateImageAnalysisFromFileAsync(imageFileInfo);

                if (existingEntry != null)
                {
                    // Remove existing and add new to avoid tracking conflicts
                    var trackedEntity = await _context.Images.FirstOrDefaultAsync(i => i.Filename == filename);
                    if (trackedEntity != null)
                    {
                        _context.Images.Remove(trackedEntity);
                    }
                    _context.Images.Add(updatedAnalysis);
                }
                else
                {
                    // Add new entry
                    _context.Images.Add(updatedAnalysis);
                }
            }
            else if (existingEntry != null)
            {
                // Image deleted - remove from cache
                var trackedEntity = await _context.Images.FirstOrDefaultAsync(i => i.Filename == filename);
                if (trackedEntity != null)
                {
                    _context.Images.Remove(trackedEntity);
                }
            }

            await _context.SaveChangesAsync();

            // Clear tracker to prevent future conflicts
            _context.ChangeTracker.Clear();
        }

        public async Task<ImageAnalysis> GetImageAsync(string filename)
        {
            if (_context == null)
                throw new InvalidOperationException("Cache service not initialized. Call InitializeAsync first.");

            return await _context.Images.FirstOrDefaultAsync(i => i.Filename == filename);
        }

        public async Task<IEnumerable<ImageAnalysis>> GetAllImagesAsync()
        {
            if (_context == null)
                throw new InvalidOperationException("Cache service not initialized. Call InitializeAsync first.");

            return await _context.Images.OrderBy(i => i.Filename).ToListAsync();
        }

        public async Task<IEnumerable<ImageAnalysis>> QueryImagesAsync(Expression<Func<ImageAnalysis, bool>> predicate)
        {
            if (_context == null)
                throw new InvalidOperationException("Cache service not initialized. Call InitializeAsync first.");

            return await _context.Images.Where(predicate).ToListAsync();
        }

        public async Task<IEnumerable<ImageAnalysis>> GetFilteredImagesAsync(ImageFilter filter)
        {
            if (_context == null)
                throw new InvalidOperationException("Cache service not initialized. Call InitializeAsync first.");

            var query = _context.Images.AsQueryable();

            // Apply filters
            if (filter.MinRating.HasValue)
                query = query.Where(i => i.LightroomRating >= filter.MinRating || i.PredictedRating >= filter.MinRating);

            if (filter.MaxRating.HasValue)
                query = query.Where(i => (i.LightroomRating ?? i.PredictedRating ?? 0) <= filter.MaxRating);

            if (filter.MinSharpness.HasValue)
                query = query.Where(i => i.SharpnessOverall >= filter.MinSharpness);

            if (filter.EyesOpenOnly == true)
                query = query.Where(i => i.EyesOpen == true);

            if (filter.AnalyzedOnly == true)
                query = query.Where(i => i.AnalysisDate.HasValue);

            if (filter.RawOnly == true)
                query = query.Where(i => i.IsRaw);

            if (filter.HasXmpOnly == true)
                query = query.Where(i => i.HasXmp);

            if (filter.IncludeFormats?.Any() == true)
                query = query.Where(i => filter.IncludeFormats.Contains(i.ImageFormat));

            if (!string.IsNullOrWhiteSpace(filter.SearchText))
            {
                var searchTerm = filter.SearchText.ToLower();
                query = query.Where(i => i.Filename.ToLower().Contains(searchTerm));
            }

            return await query.OrderBy(i => i.Filename).ToListAsync();
        }

        public async Task<CacheValidationResult> ValidateCacheAsync(string folderPath)
        {
            if (_context == null)
            {
                return new CacheValidationResult
                {
                    IsValid = false,
                    Issues = new List<string> { "Cache service not initialized" }
                };
            }

            var result = new CacheValidationResult
            {
                Issues = new List<string>()
            };

            try
            {
                // Get current files in folder
                var currentFiles = await _fileSystemService.ScanFolderAsync(folderPath);
                var currentFilesList = currentFiles.ToList();

                // Get cached entries
                var cachedEntries = await _context.Images.ToListAsync();

                // Check for missing images (in cache but not on disk)
                var missingImages = cachedEntries
                    .Where(cached => !currentFilesList.Any(current => current.Filename == cached.Filename))
                    .ToList();

                result.OrphanedCacheEntries = missingImages.Count;
                if (missingImages.Any())
                {
                    result.Issues.Add($"{missingImages.Count} images in cache but not found on disk");
                }

                // Check for new images (on disk but not in cache)
                var newImages = currentFilesList
                    .Where(current => !cachedEntries.Any(cached => cached.Filename == current.Filename))
                    .ToList();

                result.MissingImages = newImages.Count;
                if (newImages.Any())
                {
                    result.Issues.Add($"{newImages.Count} new images found that are not in cache");
                }

                // Check for modified images (file hash or XMP date changed)
                var modifiedImages = 0;
                foreach (var currentFile in currentFilesList)
                {
                    var cachedEntry = cachedEntries.FirstOrDefault(c => c.Filename == currentFile.Filename);
                    if (cachedEntry != null)
                    {
                        // Check if file was modified
                        if (cachedEntry.FileHash != currentFile.FileHash ||
                            cachedEntry.FileModifiedDate != currentFile.ModifiedDate)
                        {
                            modifiedImages++;
                        }

                        // Check if XMP was modified
                        if (currentFile.HasXmp && cachedEntry.HasXmp)
                        {
                            var xmpModified = await _xmpService.GetXmpModifiedDateAsync(currentFile.FullPath);
                            if (xmpModified.HasValue &&
                                (!cachedEntry.XmpModifiedDate.HasValue || xmpModified > cachedEntry.XmpModifiedDate))
                            {
                                modifiedImages++;
                            }
                        }
                    }
                }

                result.ModifiedImages = modifiedImages;
                if (modifiedImages > 0)
                {
                    result.Issues.Add($"{modifiedImages} images have been modified since last cache update");
                }

                result.IsValid = result.Issues.Count == 0;

                return result;
            }
            catch (Exception ex)
            {
                return new CacheValidationResult
                {
                    IsValid = false,
                    Issues = new List<string> { $"Cache validation failed: {ex.Message}" }
                };
            }
        }

        public async Task<IEnumerable<string>> GetUnanalyzedImagesAsync()
        {
            if (_context == null)
                throw new InvalidOperationException("Cache service not initialized. Call InitializeAsync first.");

            var unanalyzedImages = await _context.Images
                .Where(i => !i.AnalysisDate.HasValue)
                .Select(i => i.Filename)
                .ToListAsync();

            return unanalyzedImages;
        }

        private async Task<ImageAnalysis> CreateImageAnalysisFromFileAsync(ImageFileInfo fileInfo)
        {
            var imageAnalysis = new ImageAnalysis
            {
                Filename = fileInfo.Filename,
                FilePath = fileInfo.FullPath,
                ImageFormat = fileInfo.Format,
                IsRaw = fileInfo.IsRaw,
                FileSize = fileInfo.FileSize,
                FileModifiedDate = fileInfo.ModifiedDate,
                FileHash = fileInfo.FileHash,
                HasXmp = fileInfo.HasXmp
            };

            // Load data from XMP if it exists
            if (fileInfo.HasXmp)
            {
                try
                {
                    var xmpMetadata = await _xmpService.ReadAllXmpDataAsync(fileInfo.FullPath);
                    imageAnalysis.XmpModifiedDate = xmpMetadata.LastModified;

                    // Map Lightroom data
                    imageAnalysis.LightroomRating = xmpMetadata.LightroomRating;
                    imageAnalysis.LightroomPick = xmpMetadata.LightroomPick;
                    imageAnalysis.LightroomLabel = xmpMetadata.LightroomLabel;

                    // Map analysis data if available
                    if (xmpMetadata.AnalysisData != null)
                    {
                        var analysis = xmpMetadata.AnalysisData;
                        imageAnalysis.AnalysisDate = analysis.AnalyzedAt;
                        imageAnalysis.AnalysisVersion = analysis.AnalysisVersion;
                        imageAnalysis.ModelVersion = analysis.ModelVersion;
                        imageAnalysis.SharpnessOverall = analysis.SharpnessOverall;
                        imageAnalysis.SharpnessSubject = analysis.SharpnessSubject;
                        imageAnalysis.SubjectCount = analysis.SubjectCount;
                        imageAnalysis.SubjectSharpnessPercentage = analysis.SubjectSharpnessPercentage;
                        imageAnalysis.EyesOpen = analysis.EyesOpen;
                        imageAnalysis.EyeConfidence = analysis.EyeConfidence;
                        imageAnalysis.PredictedRating = analysis.PredictedRating;
                        imageAnalysis.PredictionConfidence = analysis.PredictionConfidence;

                        // Handle subject types
                        if (analysis.SubjectTypes?.Any() == true)
                        {
                            imageAnalysis.SubjectTypes = JsonSerializer.Serialize(analysis.SubjectTypes);
                        }

                        // Handle extended data
                        if (analysis.ExtendedData?.Any() == true)
                        {
                            imageAnalysis.ExtendedAnalysisJson = JsonSerializer.Serialize(analysis.ExtendedData);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log error but continue - XMP might be corrupted
                    Console.WriteLine($"Warning: Failed to read XMP for {fileInfo.Filename}: {ex.Message}");
                }
            }

            return imageAnalysis;
        }

        private static bool IsRawFormat(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var rawFormats = new[] { ".nef", ".cr2", ".cr3", ".arw", ".dng", ".orf", ".rw2", ".pef", ".raf", ".3fr" };
            return rawFormats.Contains(extension);
        }

        public void Dispose()
        {
            _context?.Dispose();
        }
    }
}