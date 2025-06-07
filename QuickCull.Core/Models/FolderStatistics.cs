using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Models
{
    public class FolderStatistics
    {
        public int TotalImages { get; set; }
        public int AnalyzedImages { get; set; }
        public int UnanalyzedImages { get; set; }
        public int ImagesWithXmp { get; set; }
        public int RawImages { get; set; }
        public Dictionary<int, int> RatingDistribution { get; set; } = new();
        public double AverageSharpness { get; set; }
        public int HighQualityImages { get; set; }
        public long TotalFileSize { get; set; }
        public string FolderPath { get; set; }

        public double AnalysisProgress => TotalImages > 0 ? (double)AnalyzedImages / TotalImages * 100 : 0;
        public string FormattedFileSize => FormatFileSize(TotalFileSize);

        private static string FormatFileSize(long bytes)
        {
            var units = new[] { "B", "KB", "MB", "GB", "TB" };
            var size = (double)bytes;
            var unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:F1} {units[unitIndex]}";
        }
    }
}
