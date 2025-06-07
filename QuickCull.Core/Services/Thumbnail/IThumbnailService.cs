using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace QuickCull.Core.Services.Thumbnail
{
    public interface IThumbnailService
    {
        public BitmapSource GetThumbnailImage(string imagePath);
        public string GetThumbnailPath(string imagePath);
        public Task GenerateThumbnailsAsync(string folderPath);
    }
}
