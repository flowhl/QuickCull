using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Models
{
    public class XmpMetadata
    {
        public DateTime LastModified { get; set; }
        public AnalysisResult AnalysisData { get; set; }

        // Traditional Lightroom/XMP rating (0-5 stars)
        public int? LightroomRating { get; set; }

        // Lightroom pick flag: true = picked, false = rejected, null = default/unpicked
        public bool? LightroomPick { get; set; }

        // Color label
        public string LightroomLabel { get; set; }

        // Camera Raw specific properties
        public bool? HasCameraRawAdjustments { get; set; }
        public bool? AdjustmentsApplied { get; set; }
        public string CameraProfile { get; set; }

        // Convenience properties
        public bool HasAnyLightroomData =>
            LightroomRating.HasValue ||
            LightroomPick.HasValue ||
            !string.IsNullOrEmpty(LightroomLabel) ||
            HasCameraRawAdjustments == true;

        // Helper properties for pick status
        public bool IsPicked => LightroomPick == true;
        public bool IsRejected => LightroomPick == false;
        public bool IsUnpicked => LightroomPick == null;

        public string PickStatusText => LightroomPick switch
        {
            true => "Picked",
            false => "Rejected",
            null => "Unpicked"
        };
    }
}
