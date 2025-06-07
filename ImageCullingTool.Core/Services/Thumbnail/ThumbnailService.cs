using ImageCullingTool.Core.Extensions;
using ImageCullingTool.Core.Services.Logging;
using ImageCullingTool.Core.Services.Settings;
using ImageMagick;
using ImageMagick.Formats;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ImageCullingTool.Core.Services.Thumbnail
{
    public class ThumbnailService : IThumbnailService
    {
        private readonly ILoggingService _loggingService;
        public ThumbnailService(ILoggingService loggingService)
        {
            _loggingService = loggingService;
        }
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
            string thumbnailPath = Path.Combine(dir, "thumbnails", $"{fileName}.jpg");
            return thumbnailPath;
        }

        public async Task GenerateThumbnailsAsync(string folderPath)
        {
            string thumbnailDir = Path.Combine(folderPath, "thumbnails");
            Directory.CreateDirectory(thumbnailDir);
            var imageFiles = await FileServiceProvider.FileService.ScanFolderAsync(folderPath);
            var rawFiles = imageFiles.Where(f => f.IsRaw).Select(f => f.FullPath).ToList();
            var nonRawFiles = imageFiles.Where(f => !f.IsRaw).Select(f => f.FullPath).ToList();

            var sw = new Stopwatch();
            sw.Start();
            //use cpu cores -2
            int maxDegreeOfParallelism = Environment.ProcessorCount - 2;

            // Generate thumbnails for non raw images
            nonRawFiles.AsParallel()
                .WithDegreeOfParallelism(maxDegreeOfParallelism)
                .ForAll(imageFile =>
            {
                GenerateThumbnailForNonRawImage(imageFile);
            });

            // Generate thumbnails for raw images
            rawFiles.AsParallel()
                .WithDegreeOfParallelism(maxDegreeOfParallelism)
                .ForAll(imageFile =>
            {
                GenerateThumbnailForRawImage(imageFile);
            });

            sw.Stop();
            _loggingService.LogInfoAsync($"Generating Thumbnails took: {sw.Elapsed}");
        }

        public void GenerateThumbnailForRawImage(string imagePath)
        {
            var thumbnailPath = GetThumbnailPath(imagePath);
            if (File.Exists(thumbnailPath))
            {
                return;
            }

            try
            {
                using (var image = new MagickImage())
                {
                    image.Settings.SetDefines(new DngReadDefines
                    {
                        ReadThumbnail = true,
                    });
                    image.Ping(imagePath);

                    // Get the orientation from the main image
                    var orientation = image.Orientation;

                    var profile = image.GetProfile("dng:thumbnail");
                    if (profile != null)
                    {
                        // Load the thumbnail data into a new MagickImage
                        using (var thumbnail = new MagickImage(profile.ToByteArray()))
                        {
                            // Apply the orientation from the main image
                            thumbnail.Orientation = orientation;
                            thumbnail.AutoOrient();


                            int targetWidth = SettingsService.Settings.ThumbnailWidth;
                            int targetHeight = (int)(targetWidth * image.Height / (double)image.Width);

                            // Optional: resize if needed
                            if (thumbnail.Width > targetWidth || thumbnail.Height > targetHeight)
                            {
                                thumbnail.FilterType = FilterType.Box;
                                int width = Math.Min(targetWidth, (int)thumbnail.Width);
                                int height = (int)(width * thumbnail.Height / (double)thumbnail.Width);
                                thumbnail.Resize((uint)width, (uint)height);
                            }

                            thumbnail.Format = MagickFormat.Jpeg;
                            thumbnail.Quality = 85;
                            thumbnail.Write(thumbnailPath);
                        }
                    }
                    else
                    {
                        // Fallback: no embedded thumbnail found
                        GenerateThumbnailFromFullRaw(imagePath, thumbnailPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync($"Failed to generate thumbnail for RAW {imagePath}: {ex.GetFullDetails()}");
                // Fallback to full processing
                GenerateThumbnailFromFullRaw(imagePath, thumbnailPath);
            }
        }

        private void GenerateThumbnailFromFullRaw(string imagePath, string thumbnailPath)
        {
            using (var image = new MagickImage(imagePath))
            {
                image.AutoOrient();

                int width = SettingsService.Settings.ThumbnailWidth;
                int height = (int)(width * image.Height / (double)image.Width);

                image.FilterType = FilterType.Box;
                image.Resize((uint)width, (uint)height);
                image.Format = MagickFormat.Jpeg;
                image.Quality = 85;

                image.Write(thumbnailPath);
            }
        }

        public void GenerateThumbnailForNonRawImage(string imagePath)
        {
            var thumbnailPath = GetThumbnailPath(imagePath);
            if (File.Exists(thumbnailPath))
            {
                return;
            }

            try
            {
                using (var image = new MagickImage())
                {
                    // Configure for speed before reading
                    image.Settings.SetDefine(MagickFormat.Jpeg, "size", "128x128"); // Read only what we need
                    //image.Settings.ColorSpace = ColorSpace.sRGB;
                    image.Settings.Interlace = Interlace.NoInterlace;

                    // Read the image
                    image.Read(imagePath);

                    // Use faster resize algorithm
                    image.FilterType = FilterType.Box; // Faster than default Lanczos

                    // Auto-orient the image based on EXIF data
                    image.AutoOrient();

                    // Calculate dimensions
                    int width = SettingsService.Settings.ThumbnailWidth;
                    int height = (int)(width * image.Height / (double)image.Width);

                    // Resize
                    image.Thumbnail((uint)width, (uint)height);

                    // Optimize JPEG settings for speed
                    image.Format = MagickFormat.Jpeg;
                    image.Quality = 85; // Lower quality = faster

                    image.Write(thumbnailPath);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync($"Failed to generate thumbnail for {imagePath}: {ex.GetFullDetails()}");
            }
        }
    }
}
