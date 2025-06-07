using QuickCull.Core.Services.Cache;
using QuickCull.Core.Services.FileSystem;
using QuickCull.Core.Services.ImageCulling;
using QuickCull.Core.Services.XMP;
using QuickCull.Core.Services.Analysis;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using QuickCull.Models;
using QuickCull.Core.Services.Logging;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using QuickCull.Core.Extensions;
using System.Globalization;
using QuickCull.Core.Services.Thumbnail;
using ImageMagick;
using QuickCull.Core.Services.Settings;

namespace QuickCull.WPF;
public partial class MainWindow : Window
{
    // Services (manually instantiated - no DI for now)
    private IImageCullingService _cullingService;
    private IFileSystemService _fileSystemService;
    private ICacheService _cacheService;
    private IXmpService _xmpService;
    private IAnalysisService _analysisService;
    private ILoggingService _loggingService;
    private IXmpFileWatcherService _fileWatcherService;
    private IThumbnailService _thumbnailService;

    // UI State
    private ObservableCollection<ImageAnalysis> _allImages;
    private ObservableCollection<ImageAnalysis> _filteredImages;
    private ImageAnalysis _selectedImage;
    private CancellationTokenSource _cancellationTokenSource;
    private bool _isOperationRunning;

    public MainWindow()
    {
        InitializeComponent();
        InitializeServices();
        InitializeUI();
    }

    private void InitializeServices()
    {
        // Manually create service instances (no DI container)
        _loggingService = new TraceLoggingService();
        _fileSystemService = new FileSystemService();
        _xmpService = new XmpService();
        _cacheService = new CacheService(_fileSystemService, _xmpService, _loggingService);
        _analysisService = new AnalysisService();
        _fileWatcherService = new XmpFileWatcherService(_loggingService);
        _thumbnailService = new ThumbnailService(_loggingService);

        _cullingService = new ImageCullingService(
            _analysisService, _cacheService, _xmpService,
            _fileSystemService, _thumbnailService, _fileWatcherService, _loggingService);

        // Subscribe to XMP file change events
        _cullingService.XmpFileChanged += OnXmpFileChanged;
    }

    private void InitializeUI()
    {
        _allImages = new ObservableCollection<ImageAnalysis>();
        _filteredImages = new ObservableCollection<ImageAnalysis>();
        ImageListControl.ItemsSource = _filteredImages;

        // Show the "no image" overlay initially
        NoImageOverlay.Visibility = Visibility.Visible;
        MainImage.Source = null;

        UpdateUI();
    }

    private async void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            await LoadFolderAsync(dialog.FolderName);
        }
    }

    private async void BtnRegenerateCache_Click(object sender, RoutedEventArgs e)
    {
        await RegenerateCache();
    }

    private async Task RegenerateCache()
    {
        try
        {
            SetOperationRunning(true, "Regenerating cache...");
            await _cullingService.RefreshCacheAsync();
            TxtStatus.Text = "Cache regenerated successfully";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error regenerating cache: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = "Cache regeneration failed";
        }
        finally
        {
            await RefreshImageListAsync();
            await UpdateFolderStatsAsync();
            SetOperationRunning(false);
        }
    }

    private async Task LoadFolderAsync(string folderPath)
    {
        try
        {
            SetOperationRunning(true, "Loading folder...");

            await _cullingService.LoadFolderAsync(folderPath);

            TxtCurrentFolder.Text = folderPath;

            await RefreshImageListAsync();
            await UpdateFolderStatsAsync();

            TxtStatus.Text = "Folder loaded successfully";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading folder: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = "Error loading folder";
        }
        finally
        {
            SetOperationRunning(false);
        }
    }

    private async void BtnAnalyzeAll_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_cullingService.CurrentFolderPath))
        {
            MessageBox.Show("Please select a folder first.", "No Folder Selected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            SetOperationRunning(true, "Analyzing all images...");

            var progress = new Progress<AnalysisProgress>(UpdateAnalysisProgress);

            await _cullingService.AnalyzeAllImagesAsync(progress, _cancellationTokenSource.Token);

            await RefreshImageListAsync();
            await UpdateFolderStatsAsync();

            // Update selected image if it was analyzed
            if (_selectedImage != null)
            {
                var updatedImage = _allImages.FirstOrDefault(i => i.Filename == _selectedImage.Filename);
                if (updatedImage != null)
                {
                    await DisplayImageDetails(updatedImage);
                }
            }

            TxtStatus.Text = "Analysis completed successfully";
        }
        catch (OperationCanceledException)
        {
            TxtStatus.Text = "Analysis cancelled by user";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error during analysis: {ex.Message}", "Analysis Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = "Analysis failed";
        }
        finally
        {
            SetOperationRunning(false);
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private async void BtnAnalyzeSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedImage == null)
        {
            MessageBox.Show("Please select an image first.", "No Image Selected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await AnalyzeSingleImageAsync(_selectedImage.Filename);
    }

    private async void BtnAnalyzeThis_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedImage == null) return;
        await AnalyzeSingleImageAsync(_selectedImage.Filename);
    }

    private async Task AnalyzeSingleImageAsync(string filename)
    {
        try
        {
            SetOperationRunning(true, $"Analyzing {filename}...");

            await _cullingService.AnalyzeImageAsync(filename);

            // Update the image in our collections
            var updatedImage = await _cullingService.GetImageAnalysisAsync(filename);
            if (updatedImage != null)
            {
                // Update in collections
                var allIndex = _allImages.ToList().FindIndex(i => i.Filename == filename);
                if (allIndex >= 0)
                {
                    _allImages[allIndex] = updatedImage;
                }

                var filteredIndex = _filteredImages.ToList().FindIndex(i => i.Filename == filename);
                if (filteredIndex >= 0)
                {
                    _filteredImages[filteredIndex] = updatedImage;
                }

                // Update display if this is the selected image
                if (_selectedImage.Filename == filename)
                {
                    await DisplayImageDetails(updatedImage);
                }
            }

            await UpdateFolderStatsAsync();
            TxtStatus.Text = $"Analysis complete: {filename}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error analyzing {filename}: {ex.Message}", "Analysis Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = "Analysis failed";
        }
        finally
        {
            SetOperationRunning(false);
        }
    }

    private async void BtnShowKeepers_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var keepers = await _cullingService.GetRecommendedKeepersAsync(100);
            var keepersList = keepers.ToList();

            // Filter to show only keepers
            _filteredImages.Clear();
            foreach (var image in _allImages.Where(i => keepersList.Contains(i.Filename)))
            {
                _filteredImages.Add(image);
            }

            TxtStatus.Text = $"Showing {_filteredImages.Count} recommended keepers";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error finding keepers: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnShowAll_Click(object sender, RoutedEventArgs e)
    {
        // Show all images
        _filteredImages.Clear();
        foreach (var image in _allImages)
        {
            _filteredImages.Add(image);
        }

        TxtStatus.Text = $"Showing all {_filteredImages.Count} images";
    }

    private async void ImageListControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems?.Count > 0 && e.AddedItems[0] is ImageAnalysis selectedImage)
        {
            _selectedImage = selectedImage;
            await DisplayImageAsync(selectedImage);
            await DisplayImageDetails(selectedImage);
        }
    }

    private async Task DisplayImageAsync(ImageAnalysis image)
    {
        try
        {
            string rawFilePath = Path.Combine(_cullingService.CurrentFolderPath, image.Filename);
            string thumbnailPath = _thumbnailService.GetThumbnailPath(rawFilePath);
            bool showThumbnail = SettingsService.Settings.DisplayAsThumbnails && _fileSystemService.FileExistsAsync(thumbnailPath).Result;

            string imagePath = showThumbnail ? thumbnailPath : rawFilePath;

            //if (File.Exists(imagePath))
            //{
            //    var sw = Stopwatch.StartNew();
            //    // Load image
            //    var bitmap = new BitmapImage();
            //    bitmap.BeginInit();
            //    bitmap.UriSource = new Uri(imagePath);
            //    bitmap.CacheOption = BitmapCacheOption.OnLoad;
            //    bitmap.EndInit();
            //    bitmap.Freeze();

            //    MainImage.Source = bitmap;
            //    NoImageOverlay.Visibility = Visibility.Collapsed;

            //    // Update image info
            //    TxtImageInfo.Text = $"{image.Filename} - {bitmap.PixelWidth}x{bitmap.PixelHeight}";
            //    sw.Stop();
            //    _loggingService.LogInfoAsync($"Loaded image {image.Filename} in {sw.ElapsedMilliseconds} ms");  
            //}
            if (File.Exists(imagePath))
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    // Quick orientation check with ImageMagick (fast metadata read)
                    OrientationType orientation;
                    using (var magickImage = new MagickImage())
                    {
                        magickImage.Ping(imagePath); // Fast metadata-only read
                        orientation = magickImage.Orientation;
                    }

                    // Fast WPF loading
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imagePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    // Apply orientation correction to the bitmap itself
                    BitmapSource orientedBitmap = ApplyOrientationToBitmap(bitmap, orientation);

                    MainImage.Source = orientedBitmap;
                    MainImage.RenderTransform = Transform.Identity; // Reset any previous transforms

                    NoImageOverlay.Visibility = Visibility.Collapsed;
                    string tHint = showThumbnail ? "(thumbnail)" : "";
                    TxtImageInfo.Text = $"{image.Filename} - {orientedBitmap.PixelWidth}x{orientedBitmap.PixelHeight} {tHint}";
                    sw.Stop();
                    _loggingService.LogInfoAsync($"Loaded image {image.Filename} in {sw.ElapsedMilliseconds} ms with orientation {orientation}");
                }
                catch (Exception ex)
                {
                    // Fallback without orientation correction
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imagePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    MainImage.Source = bitmap;
                    MainImage.RenderTransform = Transform.Identity;
                    NoImageOverlay.Visibility = Visibility.Collapsed;
                    TxtImageInfo.Text = $"{image.Filename} - {bitmap.PixelWidth}x{bitmap.PixelHeight}";
                }
            }
            else
            {
                MainImage.Source = null;
                NoImageOverlay.Visibility = Visibility.Visible;
                TxtImageInfo.Text = "Image file not found";
            }
        }
        catch (Exception ex)
        {
            MainImage.Source = null;
            NoImageOverlay.Visibility = Visibility.Visible;
            TxtImageInfo.Text = $"Error loading image: {ex.Message}";
        }
    }

    private BitmapSource ApplyOrientationToBitmap(BitmapSource source, OrientationType orientation)
    {
        if (orientation == OrientationType.TopLeft || orientation == OrientationType.Undefined)
        {
            return source; // No rotation needed
        }

        Transform transform = orientation switch
        {
            OrientationType.BottomRight => new RotateTransform(180),
            OrientationType.RightTop => new RotateTransform(90),
            OrientationType.LeftBottom => new RotateTransform(270),
            OrientationType.TopRight => new ScaleTransform(-1, 1), // Flip horizontal
            OrientationType.BottomLeft => new ScaleTransform(1, -1), // Flip vertical
            OrientationType.LeftTop => new TransformGroup
            {
                Children = { new RotateTransform(270), new ScaleTransform(-1, 1) }
            },
            OrientationType.RightBottom => new TransformGroup
            {
                Children = { new RotateTransform(90), new ScaleTransform(-1, 1) }
            },
            _ => Transform.Identity
        };

        var transformedBitmap = new TransformedBitmap(source, transform);
        transformedBitmap.Freeze();
        return transformedBitmap;
    }

    private async Task DisplayImageDetails(ImageAnalysis image)
    {
        // File Info
        TxtFileName.Text = image.Filename;
        TxtFileSize.Text = FormatFileSize(image.FileSize);
        TxtFileFormat.Text = $"{image.ImageFormat.ToUpper()} {(image.IsRaw ? "(RAW)" : "")}";

        // Try to get actual dimensions
        try
        {
            var imagePath = Path.Combine(_cullingService.CurrentFolderPath, image.Filename);
            if (File.Exists(imagePath) && MainImage.Source is BitmapSource bitmap)
            {
                TxtFileDimensions.Text = $"{bitmap.PixelWidth} x {bitmap.PixelHeight}";
            }
            else
            {
                TxtFileDimensions.Text = "Unknown";
            }
        }
        catch
        {
            TxtFileDimensions.Text = "Unknown";
        }

        // Lightroom Data
        TxtLrRating.Text = image.LightroomRating?.ToString() ?? "None";
        TxtLrPick.Text = image.LightroomPick?.ToString() ?? "None";
        TxtLrLabel.Text = string.IsNullOrEmpty(image.LightroomLabel) ? "None" : image.LightroomLabel;

        // AI Analysis
        if (image.AnalysisDate.HasValue)
        {
            TxtAiRating.Text = $"{image.PredictedRating} stars";
            TxtSharpness.Text = $"{image.SharpnessOverall:F3}";
            TxtSubjects.Text = image.SubjectCount?.ToString() ?? "0";
            TxtEyesOpen.Text = image.EyesOpen?.ToString() ?? "Unknown";
            TxtConfidence.Text = $"{image.PredictionConfidence:F1}%";
            TxtAnalyzed.Text = $"Analyzed: {image.AnalysisDate:yyyy-MM-dd HH:mm}";

            // Extended data
            if (!string.IsNullOrEmpty(image.ExtendedAnalysisJson))
            {
                TxtExtendedData.Text = image.ExtendedAnalysisJson;
            }
            else
            {
                TxtExtendedData.Text = "No extended analysis data";
            }
        }
        else
        {
            TxtAiRating.Text = "Not analyzed";
            TxtSharpness.Text = "-";
            TxtSubjects.Text = "-";
            TxtEyesOpen.Text = "-";
            TxtConfidence.Text = "-";
            TxtAnalyzed.Text = "Not analyzed";
            TxtExtendedData.Text = "Image has not been analyzed yet";
        }

        // Update action buttons
        BtnAnalyzeThis.IsEnabled = !_isOperationRunning;
        BtnOpenInExplorer.IsEnabled = true;
        BtnOpenXmp.IsEnabled = image.HasXmp;
    }

    private void BtnOpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedImage == null) return;

        try
        {
            var imagePath = Path.Combine(_cullingService.CurrentFolderPath, _selectedImage.Filename);
            if (File.Exists(imagePath))
            {
                Process.Start("explorer.exe", $"/select,\"{imagePath}\"");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening explorer: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnOpenXmp_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedImage == null) return;

        try
        {
            var xmpPath = Path.Combine(_cullingService.CurrentFolderPath, _selectedImage.Filename + ".xmp");
            if (File.Exists(xmpPath))
            {
                Process.Start(new ProcessStartInfo(xmpPath) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show("XMP file not found.", "File Not Found",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening XMP file: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
    }

    private async Task RefreshImageListAsync()
    {
        if (string.IsNullOrEmpty(_cullingService.CurrentFolderPath))
            return;

        try
        {
            var images = await _cullingService.GetFilteredImagesAsync(new ImageFilter());

            _allImages.Clear();
            _filteredImages.Clear();

            foreach (var image in images)
            {
                _allImages.Add(image);
                _filteredImages.Add(image);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading images: {ex.Message}", "Load Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task UpdateFolderStatsAsync()
    {
        if (string.IsNullOrEmpty(_cullingService.CurrentFolderPath))
            return;

        try
        {
            var stats = await _cullingService.GetFolderStatisticsAsync();

            TxtFolderStats.Text = $"Images: {stats.TotalImages} | " +
                                  $"Analyzed: {stats.AnalyzedImages} ({stats.AnalysisProgress:F0}%) | " +
                                  $"RAW: {stats.RawImages} | " +
                                  $"Size: {stats.FormattedFileSize}";
        }
        catch (Exception ex)
        {
            TxtFolderStats.Text = "Error loading statistics";
        }
    }

    private void UpdateAnalysisProgress(AnalysisProgress progress)
    {
        Dispatcher.Invoke(() =>
        {
            if (progress.TotalImages > 0)
            {
                ProgressBar.Value = progress.PercentComplete;
                TxtProgressDetail.Text = $"{progress.ProcessedImages}/{progress.TotalImages} - {progress.CurrentImage}";

                if (progress.EstimatedTimeRemaining > TimeSpan.Zero)
                {
                    TxtProgressDetail.Text += $" (ETA: {progress.EstimatedTimeRemaining:mm\\:ss})";
                }
            }
            else
            {
                TxtProgressDetail.Text = progress.CurrentImage;
            }
        });
    }

    private void SetOperationRunning(bool isRunning, string statusText = "")
    {
        _isOperationRunning = isRunning;

        // Update UI state
        BtnSelectFolder.IsEnabled = !isRunning;
        BtnAnalyzeAll.IsEnabled = !isRunning && !string.IsNullOrEmpty(_cullingService.CurrentFolderPath);
        BtnAnalyzeSelected.IsEnabled = !isRunning && _selectedImage != null;
        BtnShowKeepers.IsEnabled = !isRunning && !string.IsNullOrEmpty(_cullingService.CurrentFolderPath);
        BtnShowAll.IsEnabled = !isRunning && !string.IsNullOrEmpty(_cullingService.CurrentFolderPath);
        BtnAnalyzeThis.IsEnabled = !isRunning && _selectedImage != null;

        // Progress indicators
        ProgressBar.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
        BtnCancel.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrEmpty(statusText))
        {
            TxtStatus.Text = statusText;
        }

        if (!isRunning)
        {
            ProgressBar.Value = 0;
            TxtProgressDetail.Text = "";
        }
    }

    private void UpdateUI()
    {
        var hasFolderLoaded = !string.IsNullOrEmpty(_cullingService?.CurrentFolderPath);

        BtnAnalyzeAll.IsEnabled = hasFolderLoaded && !_isOperationRunning;
        BtnAnalyzeSelected.IsEnabled = hasFolderLoaded && _selectedImage != null && !_isOperationRunning;
        BtnShowKeepers.IsEnabled = hasFolderLoaded && !_isOperationRunning;
        BtnShowAll.IsEnabled = hasFolderLoaded && !_isOperationRunning;
    }

    private static string FormatFileSize(long bytes)
    {
        var units = new[] { "B", "KB", "MB", "GB" };
        var size = (double)bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:F1} {units[unitIndex]}";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        _cullingService?.Dispose();
        _fileWatcherService?.Dispose();
        base.OnClosing(e);
    }

    private async void CullingService_XmpFileChanged(object sender, XmpFileChangedEventArgs e)
    {
        // Handle XMP file changes on UI thread
        await Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                // Update status
                TxtStatus.Text = $"XMP updated: {e.ImageFilename}";

                // Refresh the image in our collections if it's currently loaded
                var updatedImage = await _cullingService.GetImageAnalysisAsync(e.ImageFilename);
                if (updatedImage != null)
                {
                    // Find and update in collections
                    UpdateImageInCollections(updatedImage);

                    // If this is the currently selected image, refresh the display
                    if (_selectedImage?.Filename == e.ImageFilename)
                    {
                        _selectedImage = updatedImage;
                        await DisplayImageDetails(updatedImage);
                    }
                }

                // Update folder stats
                await UpdateFolderStatsAsync();

                // Show notification in status for a moment
                await Task.Delay(3000);
                if (TxtStatus.Text == $"XMP updated: {e.ImageFilename}")
                {
                    TxtStatus.Text = "Ready";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling XMP change: {ex.Message}");
            }
        }));
    }

    private void UpdateImageInCollections(ImageAnalysis updatedImage)
    {
        // Update in all images collection
        for (int i = 0; i < _allImages.Count; i++)
        {
            if (_allImages[i].Filename == updatedImage.Filename)
            {
                _allImages[i] = updatedImage;
                break;
            }
        }

        // Update in filtered images collection
        for (int i = 0; i < _filteredImages.Count; i++)
        {
            if (_filteredImages[i].Filename == updatedImage.Filename)
            {
                _filteredImages[i] = updatedImage;
                break;
            }
        }

        // Update in the custom control
        ImageListControl.UpdateItem(updatedImage);
    }
    private async void OnXmpFileChanged(object sender, XmpFileChangedEventArgs e)
    {
        // Handle XMP file changes on UI thread
        await Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                // Update status
                TxtStatus.Text = $"XMP updated: {e.ImageFilename}";

                await RegenerateCache();

                // Refresh the image in our collections if it's currently loaded
                var updatedImage = await _cullingService.GetImageAnalysisAsync(e.ImageFilename);
                if (updatedImage != null)
                {
                    // Find and update in collections
                    UpdateImageInCollections(updatedImage);

                    // If this is the currently selected image, refresh the display
                    if (_selectedImage?.Filename == e.ImageFilename)
                    {
                        _selectedImage = updatedImage;
                        await DisplayImageDetails(updatedImage);
                    }
                }

                // Update folder stats
                await UpdateFolderStatsAsync();

                // Show notification in status for a moment
                await Task.Delay(3000);
                if (TxtStatus.Text == $"XMP updated: {e.ImageFilename}")
                {
                    TxtStatus.Text = "Ready";
                }

                if (ImageListControl.SelectedItem is ImageAnalysis selectedImage)
                {
                    _selectedImage = selectedImage;
                    await DisplayImageAsync(selectedImage);
                    await DisplayImageDetails(selectedImage);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling XMP change: {ex.Message}");
            }
        }));
    }

}