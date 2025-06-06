using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ImageCullingTool.Core.Services.Thumbnail
{
    public class ThumbnailService : IThumbnailService
    {
        public ThumbnailService() { }
        public BitmapSource GetThumbnailImage(string imagePath)
        {
            string path = GetThumbnailPath(imagePath);
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Thumbnail path cannot be null or empty.", nameof(imagePath));
            }
            return new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute));
        }

        public string GetThumbnailPath(string imagePath)
        {
            string dir = Path.GetDirectoryName(imagePath);
            if (string.IsNullOrEmpty(dir))
            {
                throw new ArgumentException("Image path is invalid.", nameof(imagePath));
            }
            string fileName = Path.GetFileNameWithoutExtension(imagePath);
            string thumbnailPath = Path.Combine(dir, "thumbnails", $"{fileName}.png");
            return thumbnailPath;
        }

        public async Task GenerateThumbnailsAsync(string folderPath)
        {
            string thumbnailDir = Path.Combine(folderPath, "thumbnails");
            Directory.CreateDirectory(thumbnailDir);
            var imageFiles = FileServiceProvider.FileService.ScanFolderForFiles(folderPath);

            imageFiles.AsParallel().ForAll(imageFile =>
            {
                GenerateThumbnailForImage(imageFile);
            });
        }

        public void GenerateThumbnailForImage(string imagePath)
        {
            var image = new Mat(imagePath);
            if (image.Empty())
            {
                return;
            }
            var thumbnailPath = GetThumbnailPath(imagePath);
            if (File.Exists(thumbnailPath))
            {
                return; // Thumbnail already exists
            }

            //resize to 100px * aspect ratio
            double aspectRatio = (double)image.Width / image.Height;
            int width = 100;
            int height = (int)(width / aspectRatio);
            var thumbnail = new Mat();
            Cv2.Resize(image, thumbnail, new OpenCvSharp.Size(width, height));
            // Save the thumbnail
            thumbnail.SaveImage(thumbnailPath);
        }
    }
}
