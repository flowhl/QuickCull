using QuickCull.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Core.Services.Analysis
{
    public interface IAnalysisService
    {
        Task<AnalysisResult> AnalyzeImageAsync(string imagePath);
        Task<IEnumerable<AnalysisResult>> AnalyzeBatchAsync(
            IEnumerable<string> imagePaths,
            IProgress<AnalysisProgress> progress = null,
            CancellationToken cancellationToken = default);
        string CurrentModelVersion { get; }
        string CurrentAnalysisVersion { get; }
        Task InitializeAsync(AnalysisConfiguration config);
    }
}
