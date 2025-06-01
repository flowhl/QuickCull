using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageCullingTool.Models
{
    // Progress tracking
    public class AnalysisProgress
    {
        public int TotalImages { get; set; }
        public int ProcessedImages { get; set; }
        public string CurrentImage { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        public double PercentComplete => (double)ProcessedImages / TotalImages * 100;
    }

    // Cache validation
    public class CacheValidationResult
    {
        public bool IsValid { get; set; }
        public int MissingImages { get; set; }
        public int ModifiedImages { get; set; }
        public int OrphanedCacheEntries { get; set; }
        public List<string> Issues { get; set; } = new();
    }

    // UI filtering
    public class ImageFilter
    {
        public int? MinRating { get; set; }
        public int? MaxRating { get; set; }
        public double? MinSharpness { get; set; }
        public bool? EyesOpenOnly { get; set; }
        public bool? AnalyzedOnly { get; set; }
        public bool? RawOnly { get; set; }
        public bool? HasXmpOnly { get; set; }
        public List<string> IncludeFormats { get; set; }
        public string SearchText { get; set; }
    }

    // Configuration
    public class AnalysisConfiguration
    {
        public string ModelPath { get; set; }
        public float ConfidenceThreshold { get; set; } = 0.5f;
        public bool EnableGpuAcceleration { get; set; } = true;
        public int BatchSize { get; set; } = 4;
        public bool WriteXmpFiles { get; set; } = true;
    }
}
