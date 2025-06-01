using ImageCullingTool.Models;
using ImageCullingTool.Core.Services.FileSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageCullingTool.Core.Services.XMP
{
    public interface IXmpService
    {
        Task WriteAnalysisToXmpAsync(string imagePath, AnalysisResult analysisResult);
        Task<AnalysisResult> ReadAnalysisFromXmpAsync(string imagePath);
        Task<XmpMetadata> ReadAllXmpDataAsync(string imagePath);
        bool XmpFileExists(string imagePath);
        Task<DateTime?> GetXmpModifiedDateAsync(string imagePath);
    }
}
