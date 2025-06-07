using QuickCull.Core.Extensions;
using QuickCull.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Core.Services.Logging
{
    public class TraceLoggingService : ILoggingService
    {
        public async Task LogInfoAsync(string message)
        {
            Trace.WriteLine($"[INFO] {DateTime.Now:HH:mm:ss} {message}");
            await Task.CompletedTask;
        }

        public async Task LogWarningAsync(string message)
        {
            Trace.WriteLine($"[WARN] {DateTime.Now:HH:mm:ss} {message}");
            await Task.CompletedTask;
        }

        public async Task LogErrorAsync(string message, Exception exception = null)
        {
            Trace.WriteLine($"[ERROR] {DateTime.Now:HH:mm:ss} {message}");
            if (exception != null)
                Trace.WriteLine($"  Exception: {exception.GetFullDetails()}");
            await Task.CompletedTask;
        }

        public async Task LogAnalysisResultAsync(string imagePath, AnalysisResult result)
        {
            Trace.WriteLine($"[ANALYSIS] {Path.GetFileName(imagePath)}: " +
                $"Rating={result.PredictedRating}, Sharpness={result.SharpnessOverall:F2}");
            await Task.CompletedTask;
        }
    }
}
