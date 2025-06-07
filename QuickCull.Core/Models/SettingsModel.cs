using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Core.Models
{
    public class SettingsModel
    {
        public bool DisplayAsThumbnails { get; set; } = false;
        public int ThumbnailWidth { get; set; } = 150;
    }
}
