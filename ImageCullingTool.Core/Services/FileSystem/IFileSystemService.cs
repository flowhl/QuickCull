using ImageCullingTool.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageCullingTool.Services.FileSystem
{
    public interface IFileSystemService
    {
        Task<IEnumerable<ImageFileInfo>> ScanFolderAsync(string folderPath);
        Task<string> CalculateFileHashAsync(string filePath);
        bool IsImageSupported(string filePath);
        string GetXmpPath(string imagePath);
        Task<bool> FileExistsAsync(string path);
        Task<DateTime> GetLastWriteTimeAsync(string path);
    }
}
