using QuickCull.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Core.Services.Analysis.ImageAnalysis
{
    public interface IImageBatchAnalysisService
    {
        Task<List<AnalysisResult>> AnalyzeImageBatchAsync(List<AnalysisResult> results);
        void Init();
    }
}
