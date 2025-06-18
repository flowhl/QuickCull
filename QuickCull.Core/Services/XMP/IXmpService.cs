using QuickCull.Models;
using QuickCull.Core.Services.FileSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Core.Services.XMP
{
    public interface IXmpService
    {
        Task WriteAnalysisToXmpAsync(string imagePath, AnalysisResult analysisResult);
        Task WritePickStatusToXmpAsync(string imagePath, bool? pickStatus);
        Task<AnalysisResult> ReadAnalysisFromXmpAsync(string imagePath);
        Task<XmpMetadata> ReadAllXmpDataAsync(string imagePath);
        bool XmpFileExists(string imagePath);
        Task<DateTime?> GetXmpModifiedDateAsync(string imagePath);
    }
}
