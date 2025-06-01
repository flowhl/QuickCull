using ImageCullingTool.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageCullingTool.Services.ImageCulling
{
    public interface IImageCullingService : IDisposable
    {
        Task LoadFolderAsync(string folderPath);
        Task AnalyzeAllImagesAsync(IProgress<AnalysisProgress> progress = null, CancellationToken cancellationToken = default);
        Task AnalyzeImageAsync(string filename);
        Task RefreshCacheAsync();
        Task<ImageAnalysis> GetImageAnalysisAsync(string filename);
        Task<IEnumerable<ImageAnalysis>> GetFilteredImagesAsync(ImageFilter filter);
        Task<CacheValidationResult> ValidateFolderAsync();
        public Task<FolderStatistics> GetFolderStatisticsAsync();
        public Task<IEnumerable<string>> GetRecommendedKeepersAsync(int maxCount);
        string CurrentFolderPath { get; }
    }
}
