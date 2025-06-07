using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Models
{
    public class ImageFileInfo
    {
        public string FullPath { get; set; }
        public string Filename { get; set; }
        public long FileSize { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string Format { get; set; }
        public bool IsRaw { get; set; }
        public bool HasXmp { get; set; }
        public string FileHash { get; set; }
    }
}
