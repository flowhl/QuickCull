using QuickCull.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Core.Services.Analysis.ImageAnalysis
{
    public interface IImageAnalysisService
    {
        Task<AnalysisResult> AnalyzeImageAsync(AnalysisResult result);
        void Init();
    }
}
