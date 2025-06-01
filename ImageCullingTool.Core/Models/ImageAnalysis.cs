using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageCullingTool.Models
{
    public class ImageAnalysis
    {
        // Primary key & file info
        public string Filename { get; set; }
        public string ImageFormat { get; set; } // ".jpg", ".nef", etc.
        public bool IsRaw { get; set; }
        public long FileSize { get; set; }
        public DateTime FileModifiedDate { get; set; }
        public string FileHash { get; set; } // For detecting file changes

        // XMP tracking
        public DateTime? XmpModifiedDate { get; set; }
        public bool HasXmp { get; set; }

        // Lightroom data (read from XMP)
        public int? LightroomRating { get; set; }
        public bool? LightroomPick { get; set; }
        public string LightroomLabel { get; set; }

        // Analysis metadata
        public DateTime? AnalysisDate { get; set; }
        public string AnalysisVersion { get; set; }
        public string ModelVersion { get; set; }

        // Sharpness analysis
        public double? SharpnessOverall { get; set; }
        public double? SharpnessSubject { get; set; }

        // Subject detection
        public int? SubjectCount { get; set; }
        public string SubjectTypes { get; set; } // JSON: ["face", "person"]
        public double? SubjectSharpnessPercentage { get; set; }

        // Eye detection
        public bool? EyesOpen { get; set; }
        public double? EyeConfidence { get; set; }

        // AI predictions
        public int? PredictedRating { get; set; }
        public double? PredictionConfidence { get; set; }

        // Extensible analysis data
        public double? NoiseLevel { get; set; }
        public double? ExposureQuality { get; set; }
        public string ColorAnalysis { get; set; }
        public string ExtendedAnalysisJson { get; set; } // For future data
    }
}
