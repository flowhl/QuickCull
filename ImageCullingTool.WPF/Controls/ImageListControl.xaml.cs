using ImageCullingTool.Core.Services.Thumbnail;
using ImageCullingTool.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ImageCullingTool.WPF.Controls
{
    public partial class ImageListControl : UserControl
    {
        private readonly IThumbnailService _thumbnailService;

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(IEnumerable),
                typeof(ImageListControl),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem),
                typeof(object),
                typeof(ImageListControl),
                new PropertyMetadata(null, OnSelectedItemChanged));

        // Selection changed event
        public static readonly RoutedEvent SelectionChangedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(SelectionChanged),
                RoutingStrategy.Bubble,
                typeof(SelectionChangedEventHandler),
                typeof(ImageListControl));

        // ObservableCollection to wrap the items and add selection state
        private ObservableCollection<ImageListItemViewModel> _wrappedItems;
        private ObservableCollection<ImageGroupViewModel> _groupedItems;
        private INotifyCollectionChanged _currentCollection;
        private CancellationTokenSource _preloadCancellation;

        public ImageListControl()
        {
            InitializeComponent();
            _wrappedItems = new ObservableCollection<ImageListItemViewModel>();
            _groupedItems = new ObservableCollection<ImageGroupViewModel>();
            ItemsContainer.ItemsSource = _wrappedItems;
            GroupsContainer.ItemsSource = _groupedItems;
            _thumbnailService = new ThumbnailService();

            // Subscribe to scroll events for preloading
            MainScrollViewer.ScrollChanged += OnScrollChanged;
            GroupScrollViewer.ScrollChanged += OnGroupScrollChanged;
        }

        // Properties
        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public object SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        // Events
        public event SelectionChangedEventHandler SelectionChanged
        {
            add => AddHandler(SelectionChangedEvent, value);
            remove => RemoveHandler(SelectionChangedEvent, value);
        }

        // Property change handlers
        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageListControl control)
            {
                control.OnItemsSourceChanged(e.OldValue as IEnumerable, e.NewValue as IEnumerable);
            }
        }

        private void OnItemsSourceChanged(IEnumerable oldValue, IEnumerable newValue)
        {
            // Unsubscribe from old collection
            if (_currentCollection != null)
            {
                _currentCollection.CollectionChanged -= OnSourceCollectionChanged;
                _currentCollection = null;
            }

            // Subscribe to new collection if it supports change notifications
            if (newValue is INotifyCollectionChanged newCollection)
            {
                _currentCollection = newCollection;
                _currentCollection.CollectionChanged += OnSourceCollectionChanged;
            }

            // Refresh the items
            RefreshItems();
        }

        private void OnSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Handle collection changes
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null)
                    {
                        foreach (ImageAnalysis item in e.NewItems)
                        {
                            var wrapper = new ImageListItemViewModel(item, _thumbnailService);
                            _wrappedItems.Add(wrapper);
                            AddToGroup(wrapper);
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                    {
                        foreach (ImageAnalysis item in e.OldItems)
                        {
                            var wrapper = _wrappedItems.FirstOrDefault(w => w.ImageData.Filename == item.Filename);
                            if (wrapper != null)
                            {
                                _wrappedItems.Remove(wrapper);
                                RemoveFromGroup(wrapper);
                            }
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Replace:
                    if (e.OldItems != null && e.NewItems != null)
                    {
                        for (int i = 0; i < e.OldItems.Count; i++)
                        {
                            var oldItem = (ImageAnalysis)e.OldItems[i];
                            var newItem = (ImageAnalysis)e.NewItems[i];

                            var wrapper = _wrappedItems.FirstOrDefault(w => w.ImageData.Filename == oldItem.Filename);
                            if (wrapper != null)
                            {
                                // Remove from old group if group changed
                                if (oldItem.Group != newItem.Group)
                                {
                                    RemoveFromGroup(wrapper);
                                }

                                wrapper.ImageData = newItem;

                                // Add to new group if group changed
                                if (oldItem.Group != newItem.Group)
                                {
                                    AddToGroup(wrapper);
                                }
                            }
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Reset:
                    RefreshItems();
                    break;
            }
        }

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageListControl control)
            {
                control.UpdateSelection(e.OldValue, e.NewValue);
            }
        }

        // Refresh the wrapped items collection
        private void RefreshItems()
        {
            _wrappedItems.Clear();
            _groupedItems.Clear();

            if (ItemsSource != null)
            {
                foreach (var item in ItemsSource.Cast<ImageAnalysis>())
                {
                    var wrapper = new ImageListItemViewModel(item, _thumbnailService);
                    _wrappedItems.Add(wrapper);
                    AddToGroup(wrapper);
                }
            }

            // Reapply selection if there was one
            if (SelectedItem != null)
            {
                UpdateSelection(null, SelectedItem);
            }
        }

        // Add item to appropriate group
        private void AddToGroup(ImageListItemViewModel wrapper)
        {
            var groupNumber = wrapper.ImageData.Group;
            var group = _groupedItems.FirstOrDefault(g => g.GroupNumber == groupNumber);

            if (group == null)
            {
                group = new ImageGroupViewModel(groupNumber);

                // Insert in sorted order
                int insertIndex = 0;
                while (insertIndex < _groupedItems.Count && _groupedItems[insertIndex].GroupNumber < groupNumber)
                {
                    insertIndex++;
                }
                _groupedItems.Insert(insertIndex, group);
            }

            group.Items.Add(wrapper);
        }

        // Remove item from its group
        private void RemoveFromGroup(ImageListItemViewModel wrapper)
        {
            var groupNumber = wrapper.ImageData.Group;
            var group = _groupedItems.FirstOrDefault(g => g.GroupNumber == groupNumber);

            if (group != null)
            {
                group.Items.Remove(wrapper);

                // Remove empty groups
                if (group.Items.Count == 0)
                {
                    _groupedItems.Remove(group);
                }
            }
        }

        // Update selection state
        private void UpdateSelection(object oldItem, object newItem)
        {
            // Clear old selection
            if (oldItem is ImageAnalysis oldImage)
            {
                var oldWrapper = _wrappedItems.FirstOrDefault(w => w.ImageData.Filename == oldImage.Filename);
                if (oldWrapper != null)
                {
                    oldWrapper.IsSelected = false;
                }
            }

            // Set new selection
            if (newItem is ImageAnalysis newImage)
            {
                var newWrapper = _wrappedItems.FirstOrDefault(w => w.ImageData.Filename == newImage.Filename);
                if (newWrapper != null)
                {
                    newWrapper.IsSelected = true;

                    // Scroll to selected item
                    ScrollToItem(newWrapper);
                }
            }
        }

        // Scroll to make the selected item visible
        private void ScrollToItem(ImageListItemViewModel item)
        {
            try
            {
                // For list view
                if (MainTabControl.SelectedIndex == 1) // List tab
                {
                    var index = _wrappedItems.IndexOf(item);
                    if (index >= 0)
                    {
                        // Calculate approximate scroll position
                        var itemHeight = 50; // Approximate height per item
                        var scrollPosition = index * itemHeight;
                        var viewportHeight = MainScrollViewer.ViewportHeight;

                        // Scroll to make item visible
                        if (scrollPosition < MainScrollViewer.VerticalOffset ||
                            scrollPosition > MainScrollViewer.VerticalOffset + viewportHeight - itemHeight)
                        {
                            MainScrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollPosition - viewportHeight / 2));
                        }
                    }
                }
                else // Groups tab
                {
                    // Find the group containing this item
                    var group = _groupedItems.FirstOrDefault(g => g.Items.Contains(item));
                    if (group != null)
                    {
                        var groupIndex = _groupedItems.IndexOf(group);
                        var scrollPosition = groupIndex * 160; // Approximate height per group
                        GroupScrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollPosition));
                    }
                }
            }
            catch
            {
                // Ignore scrolling errors
            }
        }

        // Scroll event handlers for preloading
        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (MainTabControl.SelectedIndex == 1) // List tab
            {
                PreloadVisibleThumbnails();
            }
        }

        private void OnGroupScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (MainTabControl.SelectedIndex == 0) // Groups tab
            {
                PreloadVisibleGroupThumbnails();
            }
        }

        private async void PreloadVisibleThumbnails()
        {
            _preloadCancellation?.Cancel();
            _preloadCancellation = new CancellationTokenSource();

            try
            {
                var visibleItems = GetVisibleListItems();
                await Task.Run(() =>
                {
                    foreach (var item in visibleItems)
                    {
                        if (_preloadCancellation.Token.IsCancellationRequested)
                            break;

                        // Trigger thumbnail loading
                        _ = item.ThumbnailSource;
                    }
                }, _preloadCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when scrolling quickly
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error preloading thumbnails: {ex.Message}");
            }
        }

        private async void PreloadVisibleGroupThumbnails()
        {
            _preloadCancellation?.Cancel();
            _preloadCancellation = new CancellationTokenSource();

            try
            {
                var visibleGroups = GetVisibleGroups();
                var allItems = visibleGroups.SelectMany(group => group.Items);

                await Task.Run(() =>
                {
                    foreach (var item in allItems)
                    {
                        if (_preloadCancellation.Token.IsCancellationRequested)
                            break;

                        // Trigger thumbnail loading
                        _ = item.ThumbnailSource;
                    }
                }, _preloadCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when scrolling quickly
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error preloading group thumbnails: {ex.Message}");
            }
        }

        private List<ImageListItemViewModel> GetVisibleListItems()
        {
            var visibleItems = new List<ImageListItemViewModel>();

            try
            {
                var itemHeight = 50.0; // Approximate height per item
                var viewportTop = MainScrollViewer.VerticalOffset;
                var viewportBottom = viewportTop + MainScrollViewer.ViewportHeight;

                var startIndex = Math.Max(0, (int)(viewportTop / itemHeight) - 5); // Extra buffer
                var endIndex = Math.Min(_wrappedItems.Count - 1, (int)(viewportBottom / itemHeight) + 5); // Extra buffer

                for (int i = startIndex; i <= endIndex; i++)
                {
                    if (i >= 0 && i < _wrappedItems.Count)
                        visibleItems.Add(_wrappedItems[i]);
                }
            }
            catch
            {
                // Fallback: return first 20 items
                visibleItems.AddRange(_wrappedItems.Take(20));
            }

            return visibleItems;
        }

        private List<ImageGroupViewModel> GetVisibleGroups()
        {
            var visibleGroups = new List<ImageGroupViewModel>();

            try
            {
                var groupHeight = 160.0; // Approximate height per group
                var viewportTop = GroupScrollViewer.VerticalOffset;
                var viewportBottom = viewportTop + GroupScrollViewer.ViewportHeight;

                var startIndex = Math.Max(0, (int)(viewportTop / groupHeight) - 2); // Extra buffer
                var endIndex = Math.Min(_groupedItems.Count - 1, (int)(viewportBottom / groupHeight) + 2); // Extra buffer

                for (int i = startIndex; i <= endIndex; i++)
                {
                    if (i >= 0 && i < _groupedItems.Count)
                        visibleGroups.Add(_groupedItems[i]);
                }
            }
            catch
            {
                // Fallback: return first 5 groups
                visibleGroups.AddRange(_groupedItems.Take(5));
            }

            return visibleGroups;
        }

        // Handle mouse clicks on items
        private void ImageItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is ImageListItemViewModel wrapper)
            {
                var oldSelection = SelectedItem;
                var newSelection = wrapper.ImageData;

                // Update the selected item
                SelectedItem = newSelection;

                // Fire selection changed event
                var args = new SelectionChangedEventArgs(SelectionChangedEvent,
                    oldSelection != null ? new[] { oldSelection } : new object[0],
                    new[] { newSelection });
                RaiseEvent(args);
            }
        }

        // Update an item in the list (for real-time updates)
        public void UpdateItem(ImageAnalysis updatedImage)
        {
            var wrapper = _wrappedItems.FirstOrDefault(w => w.ImageData.Filename == updatedImage.Filename);
            if (wrapper != null)
            {
                var oldGroup = wrapper.ImageData.Group;
                wrapper.ImageData = updatedImage;
                wrapper.NotifyPropertyChanged();

                // Handle group changes
                if (oldGroup != updatedImage.Group)
                {
                    RemoveFromGroup(wrapper);
                    AddToGroup(wrapper);
                }

                // Update selection if this was the selected item
                if (SelectedItem is ImageAnalysis selected && selected.Filename == updatedImage.Filename)
                {
                    SelectedItem = updatedImage;
                }
            }
        }

        // Clean up event subscriptions
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_currentCollection != null)
            {
                _currentCollection.CollectionChanged -= OnSourceCollectionChanged;
                _currentCollection = null;
            }

            _preloadCancellation?.Cancel();
            _preloadCancellation?.Dispose();
        }

        // Override to handle cleanup
        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            this.Unloaded += OnUnloaded;
        }
    }

    // ViewModel wrapper to add selection state and thumbnail loading
    public class ImageListItemViewModel : INotifyPropertyChanged
    {
        private ImageAnalysis _imageData;
        private bool _isSelected;
        private BitmapSource _thumbnailSource;
        private readonly IThumbnailService _thumbnailService;
        private bool _thumbnailLoaded = false;

        public ImageListItemViewModel(ImageAnalysis imageData, IThumbnailService thumbnailService)
        {
            _imageData = imageData;
            _thumbnailService = thumbnailService;
        }

        public ImageAnalysis ImageData
        {
            get => _imageData;
            set
            {
                _imageData = value;
                _thumbnailLoaded = false; // Reset thumbnail when data changes
                _thumbnailSource = null;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(Filename));
                NotifyPropertyChanged(nameof(ImageFormat));
                NotifyPropertyChanged(nameof(IsRaw));
                NotifyPropertyChanged(nameof(HasXmp));
                NotifyPropertyChanged(nameof(LightroomRating));
                NotifyPropertyChanged(nameof(LightroomPick));
                NotifyPropertyChanged(nameof(PredictedRating));
                NotifyPropertyChanged(nameof(AnalysisDate));
                NotifyPropertyChanged(nameof(SharpnessOverall));
                NotifyPropertyChanged(nameof(Group));
                NotifyPropertyChanged(nameof(ThumbnailSource));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                NotifyPropertyChanged();
            }
        }

        // Thumbnail that loads immediately
        public BitmapSource ThumbnailSource
        {
            get
            {
                if (_thumbnailSource == null && _imageData?.FilePath != null)
                {
                    try
                    {
                        _thumbnailSource = _thumbnailService.GetThumbnailImage(_imageData.FilePath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading thumbnail for {_imageData.FilePath}: {ex.Message}");
                    }
                }
                return _thumbnailSource;
            }
        }

        // Expose ImageAnalysis properties for binding
        public string Filename => _imageData?.Filename;
        public string ImageFormat => _imageData?.ImageFormat;
        public bool IsRaw => _imageData?.IsRaw ?? false;
        public bool HasXmp => _imageData?.HasXmp ?? false;
        public int? LightroomRating => _imageData?.LightroomRating;
        public bool? LightroomPick => _imageData?.LightroomPick;
        public int? PredictedRating => _imageData?.PredictedRating;
        public DateTime? AnalysisDate => _imageData?.AnalysisDate;
        public double? SharpnessOverall => _imageData?.SharpnessOverall;
        public int Group => _imageData?.Group ?? 0;

        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // ViewModel for grouped images
    public class ImageGroupViewModel : INotifyPropertyChanged
    {
        private int _groupNumber;
        private ObservableCollection<ImageListItemViewModel> _items;

        public ImageGroupViewModel(int groupNumber)
        {
            _groupNumber = groupNumber;
            _items = new ObservableCollection<ImageListItemViewModel>();
        }

        public int GroupNumber
        {
            get => _groupNumber;
            set
            {
                _groupNumber = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(GroupName));
            }
        }

        public string GroupName => $"Group {_groupNumber} ({_items.Count} images)";

        public ObservableCollection<ImageListItemViewModel> Items
        {
            get => _items;
            set
            {
                if (_items != null)
                {
                    _items.CollectionChanged -= OnItemsChanged;
                }

                _items = value;

                if (_items != null)
                {
                    _items.CollectionChanged += OnItemsChanged;
                }

                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(GroupName));
            }
        }

        private void OnItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            NotifyPropertyChanged(nameof(GroupName));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void NotifyPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}