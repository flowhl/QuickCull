using QuickCull.Core.Services.ImageCulling;
using QuickCull.Core.Services.XMP;
using QuickCull.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickCull.WPF.Controls
{
    public partial class ImageDetailControl : UserControl
    {
        private ImageAnalysis _currentImage;
        private IImageCullingService _cullingService;
        private IXmpService _xmpService;
        private IXmpFileWatcherService _xmpFileWatchingService;
        private string _folderPath;
        private bool _isUpdating = false;

        // Events for communicating back to parent
        public event EventHandler<ImageAnalysis> ImageAnalyzed;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<ImageAnalysis> ImageUpdated;

        public ImageDetailControl()
        {
            InitializeComponent();
            this.KeyDown += OnKeyDown;
            this.Focusable = true;
        }

        /// <summary>
        /// Initialize the control with required services
        /// </summary>
        public void Initialize(IImageCullingService cullingService, IXmpService xmpService, IXmpFileWatcherService xmpFileWatcher, string folderPath)
        {
            _cullingService = cullingService ?? throw new ArgumentNullException(nameof(cullingService));
            _xmpService = xmpService ?? throw new ArgumentNullException(nameof(xmpService));
            _xmpFileWatchingService = xmpFileWatcher ?? throw new ArgumentNullException(nameof(xmpFileWatcher));
            _folderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
        }

        /// <summary>
        /// Load and display image details
        /// </summary>
        public async Task LoadImageAsync(ImageAnalysis image)
        {
            _currentImage = image;

            if (image == null)
            {
                ClearDisplay();
                return;
            }

            await DisplayImageDetails(image);
        }

        /// <summary>
        /// Handle keyboard shortcuts for pick/reject and rating
        /// </summary>
        private async void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_currentImage == null) return;

            switch (e.Key)
            {
                case Key.P:
                    await SetPickStatusAsync(true);
                    e.Handled = true;
                    break;
                case Key.X:
                    await SetPickStatusAsync(false);
                    e.Handled = true;
                    break;
                case Key.U:
                    await SetPickStatusAsync(null);
                    e.Handled = true;
                    break;
                case Key.D0:
                case Key.NumPad0:
                    await SetRatingAsync(0);
                    e.Handled = true;
                    break;
                case Key.D1:
                case Key.NumPad1:
                    await SetRatingAsync(1);
                    e.Handled = true;
                    break;
                case Key.D2:
                case Key.NumPad2:
                    await SetRatingAsync(2);
                    e.Handled = true;
                    break;
                case Key.D3:
                case Key.NumPad3:
                    await SetRatingAsync(3);
                    e.Handled = true;
                    break;
                case Key.D4:
                case Key.NumPad4:
                    await SetRatingAsync(4);
                    e.Handled = true;
                    break;
                case Key.D5:
                case Key.NumPad5:
                    await SetRatingAsync(5);
                    e.Handled = true;
                    break;
            }
        }

        #region Pick/Reject Button Handlers

        private async void BtnPick_Click(object sender, RoutedEventArgs e)
        {
            await SetPickStatusAsync(true);
        }

        private async void BtnReject_Click(object sender, RoutedEventArgs e)
        {
            await SetPickStatusAsync(false);
        }

        private async void BtnNeutral_Click(object sender, RoutedEventArgs e)
        {
            await SetPickStatusAsync(null);
        }

        #endregion

        #region Rating Button Handlers

        private async void BtnRating0_Click(object sender, RoutedEventArgs e)
        {
            await SetRatingAsync(0);
        }

        private async void BtnRating1_Click(object sender, RoutedEventArgs e)
        {
            await SetRatingAsync(1);
        }

        private async void BtnRating2_Click(object sender, RoutedEventArgs e)
        {
            await SetRatingAsync(2);
        }

        private async void BtnRating3_Click(object sender, RoutedEventArgs e)
        {
            await SetRatingAsync(3);
        }

        private async void BtnRating4_Click(object sender, RoutedEventArgs e)
        {
            await SetRatingAsync(4);
        }

        private async void BtnRating5_Click(object sender, RoutedEventArgs e)
        {
            await SetRatingAsync(5);
        }

        #endregion

        #region Rating Functionality

        /// <summary>
        /// Set the rating for the current image
        /// </summary>
        private async Task SetRatingAsync(int rating)
        {
            if (_currentImage == null || _cullingService == null || _isUpdating) return;

            try
            {
                _isUpdating = true;
                SetStatus($"Setting rating to {rating} stars...");

                var updatedImage = await _cullingService.SetRatingAsync(_currentImage.Filename, rating);

                // Update our local reference with the fresh data from cache
                _currentImage = updatedImage;

                // Update UI
                UpdateRatingDisplay();
                UpdateRatingButtonStates();

                // Notify parent of change
                ImageUpdated?.Invoke(this, updatedImage);

                SetStatus($"Rating set to {rating} stars");

                // Focus back to this control to maintain keyboard shortcuts
                this.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error setting rating: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Failed to set rating");
            }
            finally
            {
                _isUpdating = false;
            }
        }

        #endregion

        #region Action Button Handlers

        private async void BtnAnalyzeThis_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null || _cullingService == null) return;

            try
            {
                SetStatus("Analyzing image...");
                SetControlsEnabled(false);

                await _cullingService.AnalyzeImageAsync(_currentImage.Filename);

                // Reload the updated image data
                var updatedImage = await _cullingService.GetImageAnalysisAsync(_currentImage.Filename);
                if (updatedImage != null)
                {
                    await LoadImageAsync(updatedImage);
                    ImageAnalyzed?.Invoke(this, updatedImage);
                }

                SetStatus($"Analysis complete: {_currentImage.Filename}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error analyzing image: {ex.Message}", "Analysis Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Analysis failed");
            }
            finally
            {
                SetControlsEnabled(true);
            }
        }

        private void BtnOpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null || string.IsNullOrEmpty(_folderPath)) return;

            try
            {
                var imagePath = Path.Combine(_folderPath, _currentImage.Filename);
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
            if (_currentImage == null || string.IsNullOrEmpty(_folderPath)) return;

            try
            {
                var xmpPath = Path.Combine(_folderPath, Path.GetFileNameWithoutExtension(_currentImage.Filename) + ".xmp");
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

        #endregion

        #region Pick/Reject Functionality

        /// <summary>
        /// Set the pick status for the current image
        /// </summary>
        private async Task SetPickStatusAsync(bool? pickStatus)
        {
            if (_currentImage == null || _cullingService == null || _isUpdating) return;

            try
            {
                _isUpdating = true;
                SetStatus($"Setting pick status...");

                var updatedImage = await _cullingService.SetPickStatusAsync(_currentImage.Filename, pickStatus);

                // Update our local reference with the fresh data from cache
                _currentImage = updatedImage;

                // Update UI
                UpdatePickStatusDisplay();
                UpdatePickButtonStates();

                // Notify parent of change
                ImageUpdated?.Invoke(this, updatedImage);

                var statusText = pickStatus switch
                {
                    true => "Image marked as Pick",
                    false => "Image marked as Reject",
                    null => "Pick status cleared"
                };
                SetStatus(statusText);

                // Focus back to this control to maintain keyboard shortcuts
                this.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error setting pick status: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Failed to set pick status");
            }
            finally
            {
                _isUpdating = false;
            }
        }

        #endregion

        #region Display Methods

        private async Task DisplayImageDetails(ImageAnalysis image)
        {
            try
            {
                // File Info
                TxtFileName.Text = image.Filename;
                TxtFileSize.Text = FormatFileSize(image.FileSize);
                TxtFileFormat.Text = $"{image.ImageFormat?.ToUpper()} {(image.IsRaw ? "(RAW)" : "")}";

                // Try to get actual dimensions from the image file
                try
                {
                    var imagePath = Path.Combine(_folderPath, image.Filename);
                    if (File.Exists(imagePath))
                    {
                        // Quick metadata read
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(imagePath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.CreateOptions = BitmapCreateOptions.DelayCreation;
                        bitmap.EndInit();

                        TxtFileDimensions.Text = $"{bitmap.PixelWidth} x {bitmap.PixelHeight}";
                    }
                    else
                    {
                        TxtFileDimensions.Text = "File not found";
                    }
                }
                catch
                {
                    TxtFileDimensions.Text = "Unknown";
                }

                // Lightroom Data
                TxtLrRating.Text = image.LightroomRating?.ToString() ?? "None";
                TxtLrPick.Text = FormatPickStatus(image.LightroomPick);
                TxtLrLabel.Text = string.IsNullOrEmpty(image.LightroomLabel) ? "None" : image.LightroomLabel;

                // AI Analysis
                if (image.AnalysisDate.HasValue)
                {
                    TxtAiRating.Text = $"{image.PredictedRating} stars";
                    TxtSharpness.Text = $"{image.SharpnessOverall:F3}";
                    TxtSubjects.Text = image.SubjectCount?.ToString() ?? "0";
                    TxtEyesOpen.Text = image.EyesOpen?.ToString() ?? "Unknown";
                    TxtConfidence.Text = $"{image.PredictionConfidence:F1}%";
                    TxtGroup.Text = image.GroupID > 0 ? $"Group {image.GroupID}" : "Ungrouped";
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
                    TxtGroup.Text = "-";
                    TxtAnalyzed.Text = "Not analyzed";
                    TxtExtendedData.Text = "Image has not been analyzed yet";
                }

                // Update pick status display and button states
                UpdatePickStatusDisplay();
                UpdatePickButtonStates();

                // Update rating display and button states
                UpdateRatingDisplay();
                UpdateRatingButtonStates();

                // Update action buttons
                SetControlsEnabled(true);
                BtnOpenXmp.IsEnabled = image.HasXmp;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error displaying image details: {ex.Message}", "Display Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdatePickStatusDisplay()
        {
            if (_currentImage?.LightroomPick == null)
            {
                TxtPickStatus.Text = "Status: None";
                TxtPickStatus.Foreground = new SolidColorBrush(Colors.Gray);
            }
            else if (_currentImage.LightroomPick == true)
            {
                TxtPickStatus.Text = "Status: Picked ✓";
                TxtPickStatus.Foreground = new SolidColorBrush(Colors.Green);
            }
            else
            {
                TxtPickStatus.Text = "Status: Rejected ✗";
                TxtPickStatus.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        private void UpdatePickButtonStates()
        {
            if (_currentImage == null) return;

            // Reset all buttons to inactive style first
            BtnPick.Style = (Style)FindResource("InactiveButtonStyle");
            BtnNeutral.Style = (Style)FindResource("InactiveButtonStyle");
            BtnReject.Style = (Style)FindResource("InactiveButtonStyle");

            // Set the active button style
            switch (_currentImage.LightroomPick)
            {
                case true:
                    BtnPick.Style = (Style)FindResource("PickButtonStyle");
                    break;
                case false:
                    BtnReject.Style = (Style)FindResource("RejectButtonStyle");
                    break;
                case null:
                    BtnNeutral.Style = (Style)FindResource("NeutralButtonStyle");
                    break;
            }
        }

        private void UpdateRatingDisplay()
        {
            var rating = _currentImage?.LightroomRating;
            if (rating.HasValue && rating > 0)
            {
                var stars = new string('★', rating.Value);
                TxtCurrentRating.Text = $"Rating: {stars} ({rating})";
                TxtCurrentRating.Foreground = new SolidColorBrush(Colors.Orange);
            }
            else
            {
                TxtCurrentRating.Text = "Rating: None";
                TxtCurrentRating.Foreground = new SolidColorBrush(Colors.Gray);
            }
        }

        private void UpdateRatingButtonStates()
        {
            if (_currentImage == null) return;

            var rating = _currentImage.LightroomRating ?? 0;

            // Reset all buttons to inactive style first
            BtnRating0.Style = (Style)FindResource("InactiveButtonStyle");
            BtnRating1.Style = (Style)FindResource("InactiveButtonStyle");
            BtnRating2.Style = (Style)FindResource("InactiveButtonStyle");
            BtnRating3.Style = (Style)FindResource("InactiveButtonStyle");
            BtnRating4.Style = (Style)FindResource("InactiveButtonStyle");
            BtnRating5.Style = (Style)FindResource("InactiveButtonStyle");

            // Set the active button style
            switch (rating)
            {
                case 0:
                    BtnRating0.Style = (Style)FindResource("RejectButtonStyle");
                    break;
                case 1:
                    BtnRating1.Style = (Style)FindResource("PickButtonStyle");
                    break;
                case 2:
                    BtnRating2.Style = (Style)FindResource("PickButtonStyle");
                    break;
                case 3:
                    BtnRating3.Style = (Style)FindResource("PickButtonStyle");
                    break;
                case 4:
                    BtnRating4.Style = (Style)FindResource("PickButtonStyle");
                    break;
                case 5:
                    BtnRating5.Style = (Style)FindResource("PickButtonStyle");
                    break;
            }
        }

        private void ClearDisplay()
        {
            // Clear all text fields
            TxtFileName.Text = "";
            TxtFileSize.Text = "";
            TxtFileDimensions.Text = "";
            TxtFileFormat.Text = "";
            TxtLrRating.Text = "";
            TxtLrPick.Text = "";
            TxtLrLabel.Text = "";
            TxtAiRating.Text = "";
            TxtSharpness.Text = "";
            TxtSubjects.Text = "";
            TxtEyesOpen.Text = "";
            TxtConfidence.Text = "";
            TxtGroup.Text = "";
            TxtAnalyzed.Text = "";
            TxtExtendedData.Text = "";
            TxtPickStatus.Text = "Status: No image selected";
            TxtPickStatus.Foreground = new SolidColorBrush(Colors.Gray);
            TxtCurrentRating.Text = "Rating: None";
            TxtCurrentRating.Foreground = new SolidColorBrush(Colors.Gray);

            // Reset button states
            BtnPick.Style = (Style)FindResource("InactiveButtonStyle");
            BtnNeutral.Style = (Style)FindResource("InactiveButtonStyle");
            BtnReject.Style = (Style)FindResource("InactiveButtonStyle");
            BtnRating0.Style = (Style)FindResource("InactiveButtonStyle");
            BtnRating1.Style = (Style)FindResource("InactiveButtonStyle");
            BtnRating2.Style = (Style)FindResource("InactiveButtonStyle");
            BtnRating3.Style = (Style)FindResource("InactiveButtonStyle");
            BtnRating4.Style = (Style)FindResource("InactiveButtonStyle");
            BtnRating5.Style = (Style)FindResource("InactiveButtonStyle");

            // Disable action buttons
            SetControlsEnabled(false);
        }

        #endregion

        #region Helper Methods

        private void SetControlsEnabled(bool enabled)
        {
            BtnPick.IsEnabled = enabled && _currentImage != null;
            BtnNeutral.IsEnabled = enabled && _currentImage != null;
            BtnReject.IsEnabled = enabled && _currentImage != null;
            BtnRating0.IsEnabled = enabled && _currentImage != null;
            BtnRating1.IsEnabled = enabled && _currentImage != null;
            BtnRating2.IsEnabled = enabled && _currentImage != null;
            BtnRating3.IsEnabled = enabled && _currentImage != null;
            BtnRating4.IsEnabled = enabled && _currentImage != null;
            BtnRating5.IsEnabled = enabled && _currentImage != null;
            BtnAnalyzeThis.IsEnabled = enabled && _currentImage != null;
            BtnOpenInExplorer.IsEnabled = enabled && _currentImage != null;
            BtnOpenXmp.IsEnabled = enabled && _currentImage != null && _currentImage.HasXmp;
        }

        private void SetStatus(string status)
        {
            StatusChanged?.Invoke(this, status);
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

        private static string FormatPickStatus(bool? pick)
        {
            return pick switch
            {
                true => "Picked ✓",
                false => "Rejected ✗",
                null => "None"
            };
        }

        /// <summary>
        /// Update the folder path when it changes
        /// </summary>
        public void UpdateFolderPath(string folderPath)
        {
            _folderPath = folderPath;
        }

        /// <summary>
        /// Get the currently displayed image
        /// </summary>
        public ImageAnalysis CurrentImage => _currentImage;

        #endregion
    }
}