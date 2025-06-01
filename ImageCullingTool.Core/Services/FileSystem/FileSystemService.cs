using ImageCullingTool.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ImageCullingTool.Core.Services.FileSystem
{
    public class FileSystemService : IFileSystemService
    {
        private static readonly string[] SupportedExtensions = {
        // RAW formats
        ".nef", ".cr2", ".cr3", ".arw", ".dng", ".orf", ".rw2", ".pef", ".raf", ".3fr",
        
        // Standard formats
        ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".tif", ".gif", ".webp",
        
        // Other formats
        ".heic", ".heif", ".avif"
    };

        private static readonly string[] RawFormats = {
        ".nef", ".cr2", ".cr3", ".arw", ".dng", ".orf", ".rw2", ".pef", ".raf", ".3fr"
    };

        public async Task<IEnumerable<ImageFileInfo>> ScanFolderAsync(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

            var results = new List<ImageFileInfo>();
            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(IsImageSupported)
                .OrderBy(Path.GetFileName);

            foreach (var filePath in files)
            {
                var fileInfo = new FileInfo(filePath);
                var extension = Path.GetExtension(filePath).ToLowerInvariant();

                results.Add(new ImageFileInfo
                {
                    FullPath = filePath,
                    Filename = fileInfo.Name,
                    FileSize = fileInfo.Length,
                    ModifiedDate = fileInfo.LastWriteTime,
                    Format = extension,
                    IsRaw = IsRawFormat(extension),
                    HasXmp = await FileExistsAsync(GetXmpPath(filePath)),
                    FileHash = await CalculateFileHashAsync(filePath)
                });
            }

            return results;
        }

        public async Task<string> CalculateFileHashAsync(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                using var sha256 = SHA256.Create();

                // For large files, only hash first 64KB for performance
                var buffer = new byte[Math.Min(65536, stream.Length)];
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

                var hash = sha256.ComputeHash(buffer, 0, bytesRead);
                return Convert.ToHexString(hash)[..16]; // First 16 chars for shorter hash
            }
            catch
            {
                // Fallback to file size + modified date if hashing fails
                var fileInfo = new FileInfo(filePath);
                return $"{fileInfo.Length}_{fileInfo.LastWriteTime.Ticks}";
            }
        }

        public bool IsImageSupported(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return SupportedExtensions.Contains(extension);
        }

        public string GetXmpPath(string imagePath)
        {
            string dir = Path.GetDirectoryName(imagePath);
            string filename = Path.GetFileNameWithoutExtension(imagePath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(filename))
                throw new ArgumentException("Invalid image path provided.");
            // Use the same directory and filename, but with .xmp extension
            return Path.Combine(dir, filename) + ".xmp";
        }

        public async Task<bool> FileExistsAsync(string path)
        {
            return await Task.FromResult(File.Exists(path));
        }

        public async Task<DateTime> GetLastWriteTimeAsync(string path)
        {
            return await Task.FromResult(File.GetLastWriteTime(path));
        }

        private static bool IsRawFormat(string extension)
        {
            return RawFormats.Contains(extension.ToLowerInvariant());
        }
    }
}
