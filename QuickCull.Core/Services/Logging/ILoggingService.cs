using QuickCull.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Core.Services.Logging
{
    public interface ILoggingService
    {
        Task LogInfoAsync(string message);
        Task LogWarningAsync(string message);
        Task LogErrorAsync(string message, Exception exception = null);
        Task LogAnalysisResultAsync(string imagePath, AnalysisResult result);
    }
}
