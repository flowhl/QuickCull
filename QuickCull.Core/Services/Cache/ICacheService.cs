using QuickCull.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Core.Services.Cache
{
    public interface ICacheService
    {
        Task InitializeAsync(string folderPath);
        Task RebuildCacheFromXmpAsync(string folderPath, IProgress<AnalysisProgress> progress = null);
        Task UpdateSingleImageCacheAsync(string imagePath);
        Task<ImageAnalysis> GetImageAsync(string filename);
        Task<IEnumerable<ImageAnalysis>> GetAllImagesAsync();
        Task<IEnumerable<ImageAnalysis>> QueryImagesAsync(Expression<Func<ImageAnalysis, bool>> predicate);
        Task<IEnumerable<ImageAnalysis>> GetFilteredImagesAsync(ImageFilter filter);
        Task<CacheValidationResult> ValidateCacheAsync(string folderPath);
        Task<IEnumerable<string>> GetUnanalyzedImagesAsync();
    }
}
