using ImageCullingTool.Core.Services.FileSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageCullingTool.Core.Services
{
    public static class FileServiceProvider
    {
        public static IFileSystemService FileService { get; set; } = new FileSystemService();
    }
}
