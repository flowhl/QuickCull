using ImageCullingTool.Services.Cache;
using ImageCullingTool.Services.FileSystem;
using ImageCullingTool.Services.ImageCulling;
using ImageCullingTool.Services.XMP;
using ImageCullingTool.Services.Analysis;
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
using System.Windows.Shapes;
using ImageCullingTool.Models;
using ImageCullingTool.Core.Services.Logging;

namespace ImageCullingTool.WPF;

public partial class MainWindow : Window
{
    // Services (manually instantiated - no DI for now)
    private IImageCullingService _cullingService;
    private IFileSystemService _fileSystemService;
    private ICacheService _cacheService;
    private IXmpService _xmpService;
    private IAnalysisService _analysisService;
    private ILoggingService _loggingService;

    // UI State
    private ObservableCollection<ImageAnalysis> _images;
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
        _fileSystemService = new FileSystemService();
        _xmpService = new XmpService();
        _cacheService = new CacheService(_fileSystemService, _xmpService);
        _analysisService = new AnalysisService();
        _loggingService = new TraceLoggingService();

        _cullingService = new ImageCullingService(
            _analysisService, _cacheService, _xmpService,
            _fileSystemService, _loggingService);
    }

    private void InitializeUI()
    {
        _images = new ObservableCollection<ImageAnalysis>();
        DgImages.ItemsSource = _images;

        // Set default filter values
        CmbMinRating.SelectedIndex = 0; // "Any"
        CmbMinSharpness.SelectedIndex = 0; // "Any"

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
            SetOperationRunning(true, "Analyzing images...");

            var progress = new Progress<AnalysisProgress>(UpdateAnalysisProgress);

            await _cullingService.AnalyzeAllImagesAsync(progress, _cancellationTokenSource.Token);

            await RefreshImageListAsync();
            await UpdateFolderStatsAsync();

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

    private async void BtnRefreshCache_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_cullingService.CurrentFolderPath))
        {
            MessageBox.Show("Please select a folder first.", "No Folder Selected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            SetOperationRunning(true, "Refreshing cache...");

            await _cullingService.RefreshCacheAsync();
            await RefreshImageListAsync();
            await UpdateFolderStatsAsync();

            TxtStatus.Text = "Cache refreshed successfully";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error refreshing cache: {ex.Message}", "Cache Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = "Cache refresh failed";
        }
        finally
        {
            SetOperationRunning(false);
        }
    }

    private async void BtnGetKeepers_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_cullingService.CurrentFolderPath))
        {
            MessageBox.Show("Please select a folder first.", "No Folder Selected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            SetOperationRunning(true, "Finding recommended keepers...");

            var keepers = await _cullingService.GetRecommendedKeepersAsync(50);
            var keepersList = keepers.ToList();

            if (keepersList.Any())
            {
                var message = $"Found {keepersList.Count} recommended keepers:\n\n" +
                              string.Join("\n", keepersList.Take(10)) +
                              (keepersList.Count > 10 ? $"\n... and {keepersList.Count - 10} more" : "");

                MessageBox.Show(message, "Recommended Keepers",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                // Apply filter to show only keepers
                await ApplyKeepersFilterAsync(keepersList);
            }
            else
            {
                MessageBox.Show("No recommended keepers found. Try analyzing more images first.",
                    "No Keepers", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            TxtStatus.Text = $"Found {keepersList.Count} recommended keepers";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error finding keepers: {ex.Message}", "Keepers Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = "Keepers search failed";
        }
        finally
        {
            SetOperationRunning(false);
        }
    }

    private async void BtnApplyFilter_Click(object sender, RoutedEventArgs e)
    {
        await ApplyCurrentFilterAsync();
    }

    private async void BtnClearFilter_Click(object sender, RoutedEventArgs e)
    {
        // Reset all filter controls
        CmbMinRating.SelectedIndex = 0;
        CmbMinSharpness.SelectedIndex = 0;
        ChkEyesOpen.IsChecked = false;
        ChkAnalyzedOnly.IsChecked = false;
        ChkRawOnly.IsChecked = false;
        TxtSearch.Text = "";

        await RefreshImageListAsync();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
    }

    private async Task ApplyCurrentFilterAsync()
    {
        if (string.IsNullOrEmpty(_cullingService.CurrentFolderPath))
            return;

        try
        {
            var filter = new ImageFilter();

            // Min Rating
            if (CmbMinRating.SelectedItem is ComboBoxItem ratingItem &&
                int.TryParse(ratingItem.Tag?.ToString(), out var minRating))
            {
                filter.MinRating = minRating;
            }

            // Min Sharpness
            if (CmbMinSharpness.SelectedItem is ComboBoxItem sharpnessItem &&
                double.TryParse(sharpnessItem.Tag?.ToString(), out var minSharpness))
            {
                filter.MinSharpness = minSharpness;
            }

            // Checkboxes
            filter.EyesOpenOnly = ChkEyesOpen.IsChecked;
            filter.AnalyzedOnly = ChkAnalyzedOnly.IsChecked;
            filter.RawOnly = ChkRawOnly.IsChecked;

            // Search text
            filter.SearchText = TxtSearch.Text?.Trim();

            var filteredImages = await _cullingService.GetFilteredImagesAsync(filter);

            _images.Clear();
            foreach (var image in filteredImages)
            {
                _images.Add(image);
            }

            TxtStatus.Text = $"Filter applied: {_images.Count} images shown";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error applying filter: {ex.Message}", "Filter Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ApplyKeepersFilterAsync(IEnumerable<string> keeperFilenames)
    {
        try
        {
            var allImages = await _cullingService.GetFilteredImagesAsync(new ImageFilter());
            var keepers = allImages.Where(img => keeperFilenames.Contains(img.Filename));

            _images.Clear();
            foreach (var image in keepers)
            {
                _images.Add(image);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error filtering keepers: {ex.Message}", "Filter Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshImageListAsync()
    {
        if (string.IsNullOrEmpty(_cullingService.CurrentFolderPath))
            return;

        try
        {
            var images = await _cullingService.GetFilteredImagesAsync(new ImageFilter());

            _images.Clear();
            foreach (var image in images)
            {
                _images.Add(image);
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
            var validation = await _cullingService.ValidateFolderAsync();

            TxtFolderStats.Text = $"Total: {stats.TotalImages} | " +
                                  $"Analyzed: {stats.AnalyzedImages} ({stats.AnalysisProgress:F0}%) | " +
                                  $"RAW: {stats.RawImages} | " +
                                  $"Size: {stats.FormattedFileSize}";

            TxtCacheStatus.Text = validation.IsValid ?
                "Cache: Valid" :
                $"Cache: Invalid ({validation.Issues.Count} issues)";
        }
        catch (Exception ex)
        {
            TxtFolderStats.Text = "Error loading statistics";
            TxtCacheStatus.Text = "Cache: Error";
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
        BtnRefreshCache.IsEnabled = !isRunning && !string.IsNullOrEmpty(_cullingService.CurrentFolderPath);
        BtnGetKeepers.IsEnabled = !isRunning && !string.IsNullOrEmpty(_cullingService.CurrentFolderPath);
        BtnApplyFilter.IsEnabled = !isRunning;

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
        BtnRefreshCache.IsEnabled = hasFolderLoaded && !_isOperationRunning;
        BtnGetKeepers.IsEnabled = hasFolderLoaded && !_isOperationRunning;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        _cullingService?.Dispose();
        base.OnClosing(e);
    }
}
