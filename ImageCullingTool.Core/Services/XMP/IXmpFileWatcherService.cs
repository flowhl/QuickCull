using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageCullingTool.Core.Services.XMP
{
    public interface IXmpFileWatcherService : IDisposable
    {
        event EventHandler<XmpFileChangedEventArgs> XmpFileChanged;
        Task StartWatchingAsync(string folderPath);
        Task StopWatchingAsync();
        bool IsWatching { get; }
        string WatchedFolder { get; }
    }

    public class XmpFileChangedEventArgs : EventArgs
    {
        public string XmpFilePath { get; set; }
        public string ImageFilePath { get; set; }
        public string ImageFilename { get; set; }
        public FileChangeType ChangeType { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum FileChangeType
    {
        Created,
        Modified,
        Deleted
    }
}
