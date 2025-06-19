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
using Velopack;

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
        VelopackApp.Build().Run();
        Loaded += MainWindow_Loaded;
        InitializeComponent();
        InitializeServices();
        InitializeUI();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            AppUpdateManager.CheckForUpdates();
        }
        catch (Exception ex)
        {
#if DEBUG
#else
            MessageBox.Show($"Error checking for updates: {ex.Message}", "Update Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
#endif
        }
    }

    private void InitializeServices()
    {
        // Manually create service instances (no DI container)
        _loggingService = new TraceLoggingService();
        _thumbnailService = new ThumbnailService(_loggingService);
        _fileSystemService = new FileSystemService();
        _xmpService = new XmpService();
        _cacheService = new CacheService(_fileSystemService, _xmpService, _loggingService);
        _analysisService = new AnalysisService(_thumbnailService);
        _fileWatcherService = new XmpFileWatcherService(_loggingService);

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

        // Initialize the detail control (will be properly initialized when folder is loaded)
        ImageDetailControl.ImageAnalyzed += ImageDetailControl_ImageAnalyzed;
        ImageDetailControl.StatusChanged += ImageDetailControl_StatusChanged;
        ImageDetailControl.ImageUpdated += ImageDetailControl_ImageUpdated;

        // Show the "no image" overlay initially
        NoImageOverlay.Visibility = Visibility.Visible;
        MainImage.Source = null;

        UpdateUI();
    }

    #region Event Handlers - ImageDetailControl

    private async void ImageDetailControl_ImageAnalyzed(object sender, ImageAnalysis updatedImage)
    {
        // Update the image in our collections
        UpdateImageInCollections(updatedImage);
        await UpdateFolderStatsAsync();
    }

    private void ImageDetailControl_StatusChanged(object sender, string status)
    {
        TxtStatus.Text = status;
    }

    private async void ImageDetailControl_ImageUpdated(object sender, ImageAnalysis updatedImage)
    {
        // Update the image in our collections
        UpdateImageInCollections(updatedImage);

        // Update the selected image reference
        if (_selectedImage?.Filename == updatedImage.Filename)
        {
            _selectedImage = updatedImage;
        }

        await UpdateFolderStatsAsync();
    }

    #endregion

    #region Keyboard Handling

    private async void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        // Only handle keys if we have a selected image and are not in an operation
        if (_selectedImage == null || _isOperationRunning) return;

        // Let the detail control handle pick/reject keys
        if (e.Key == Key.P || e.Key == Key.X || e.Key == Key.U)
        {
            // Forward the key event to the detail control
            ImageDetailControl.Focus();
            var args = new KeyEventArgs(e.KeyboardDevice, e.InputSource, e.Timestamp, e.Key)
            {
                RoutedEvent = KeyDownEvent
            };
            ImageDetailControl.RaiseEvent(args);
            e.Handled = true;
        }
        // Add navigation keys
        else if (e.Key == Key.Left || e.Key == Key.Right)
        {
            await NavigateImage(e.Key == Key.Right);
            e.Handled = true;
        }
    }

    private async Task NavigateImage(bool forward)
    {
        if (_selectedImage == null || _filteredImages.Count == 0) return;

        var currentIndex = _filteredImages.ToList().FindIndex(i => i.Filename == _selectedImage.Filename);
        if (currentIndex == -1) return;

        var newIndex = forward ? currentIndex + 1 : currentIndex - 1;

        // Wrap around
        if (newIndex >= _filteredImages.Count) newIndex = 0;
        if (newIndex < 0) newIndex = _filteredImages.Count - 1;

        var newImage = _filteredImages[newIndex];

        // Update selection in list control
        ImageListControl.SelectedItem = newImage;

        // This will trigger the selection changed event which handles the rest
    }

    #endregion

    #region Folder and File Operations

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

            // Initialize the detail control with services and folder path
            ImageDetailControl.Initialize(_cullingService, _xmpService, _fileWatcherService, folderPath);

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

    #endregion

    #region Analysis Operations

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

            await _cullingService.AnalyzeAllImagesAsync(progress, _cancellationTokenSource.Token, true);

            await RefreshImageListAsync();
            await UpdateFolderStatsAsync();

            // Update selected image if it was analyzed
            if (_selectedImage != null)
            {
                var updatedImage = _allImages.FirstOrDefault(i => i.Filename == _selectedImage.Filename);
                if (updatedImage != null)
                {
                    await DisplayImageAndDetails(updatedImage);
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
                UpdateImageInCollections(updatedImage);

                // Update display if this is the selected image
                if (_selectedImage.Filename == filename)
                {
                    await DisplayImageAndDetails(updatedImage);
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

    #endregion

    #region Filtering Operations

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

    #endregion

    #region Image Selection and Display

    private async void ImageListControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems?.Count > 0 && e.AddedItems[0] is ImageAnalysis selectedImage)
        {
            _selectedImage = selectedImage;
            await DisplayImageAndDetails(selectedImage);
        }
    }

    private async Task DisplayImageAndDetails(ImageAnalysis image)
    {
        // Display the image in the main viewer
        await DisplayImageAsync(image);

        // Load details in the detail control
        await ImageDetailControl.LoadImageAsync(image);
    }

    private async Task DisplayImageAsync(ImageAnalysis image)
    {
        try
        {
            string rawFilePath = Path.Combine(_cullingService.CurrentFolderPath, image.Filename);
            string thumbnailPath = _thumbnailService.GetThumbnailPath(rawFilePath);
            bool showThumbnail = SettingsService.Settings.DisplayAsThumbnails && _fileSystemService.FileExistsAsync(thumbnailPath).Result;

            string imagePath = showThumbnail ? thumbnailPath : rawFilePath;

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

    #endregion

    #region Progress and Cancellation

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
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

    #endregion

    #region Data Management

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

    #endregion

    #region UI State Management

    private void SetOperationRunning(bool isRunning, string statusText = "")
    {
        _isOperationRunning = isRunning;

        // Update UI state
        BtnSelectFolder.IsEnabled = !isRunning;
        BtnAnalyzeAll.IsEnabled = !isRunning && !string.IsNullOrEmpty(_cullingService.CurrentFolderPath);
        BtnAnalyzeSelected.IsEnabled = !isRunning && _selectedImage != null;
        BtnShowKeepers.IsEnabled = !isRunning && !string.IsNullOrEmpty(_cullingService.CurrentFolderPath);
        BtnShowAll.IsEnabled = !isRunning && !string.IsNullOrEmpty(_cullingService.CurrentFolderPath);

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

    #endregion

    #region XMP File Change Handling

    private async void OnXmpFileChanged(object sender, XmpFileChangedEventArgs e)
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
                    UpdateImageInCollections(updatedImage);

                    // If this is the currently selected image, refresh the display
                    if (_selectedImage?.Filename == e.ImageFilename)
                    {
                        _selectedImage = updatedImage;
                        await ImageDetailControl.LoadImageAsync(updatedImage);
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

    #endregion

    #region Helper Methods

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

    #endregion

    #region Cleanup

    protected override void OnClosing(CancelEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        _cullingService?.Dispose();
        _fileWatcherService?.Dispose();
        base.OnClosing(e);
    }

    #endregion
}