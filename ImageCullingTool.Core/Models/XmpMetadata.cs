using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageCullingTool.Models
{
    public class XmpMetadata
    {
        // Lightroom standard fields (read-only for your app)
        public int? LightroomRating { get; set; }
        public bool? LightroomPick { get; set; }
        public string LightroomLabel { get; set; }
        public List<string> Keywords { get; set; }

        // Your analysis data (read/write)
        public AnalysisResult AnalysisData { get; set; }

        // File tracking
        public DateTime LastModified { get; set; }
    }
}
