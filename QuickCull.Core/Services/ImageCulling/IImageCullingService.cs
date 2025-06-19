using QuickCull.Core.Models;
using QuickCull.Core.Services.XMP;
using QuickCull.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Core.Services.ImageCulling
{
    public interface IImageCullingService : IDisposable
    {
        Task LoadFolderAsync(string folderPath);
        Task AnalyzeAllImagesAsync(IProgress<AnalysisProgress> progress = null, CancellationToken cancellationToken = default, bool reanalyseAll = false);
        Task AnalyzeImageAsync(string filename);
        Task RefreshCacheAsync();
        Task<ImageAnalysis> GetImageAnalysisAsync(string filename);
        Task<IEnumerable<ImageAnalysis>> GetFilteredImagesAsync(ImageFilter filter);
        Task<CacheValidationResult> ValidateFolderAsync();
        Task<FolderStatistics> GetFolderStatisticsAsync();
        Task<IEnumerable<string>> GetRecommendedKeepersAsync(int maxCount = 50);
        Task RegenerateThumbnails();
        Task<ImageAnalysis> SetPickStatusAsync(string filename, bool? pickStatus);
        Task<bool> ValidateImageCacheConsistencyAsync(string filename);
        Task<CacheValidationSummary> ValidateAllCacheConsistencyAsync(IProgress<AnalysisProgress> progress = null);
        Task<int> FixCacheInconsistenciesAsync(IEnumerable<string> filenames = null, IProgress<AnalysisProgress> progress = null);

        string CurrentFolderPath { get; }
        event EventHandler<XmpFileChangedEventArgs> XmpFileChanged;
    }
}
