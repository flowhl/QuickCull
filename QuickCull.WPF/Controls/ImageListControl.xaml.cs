using QuickCull.Core.Services.Logging;
using QuickCull.Core.Services.Thumbnail;
using QuickCull.Models;
using System;
using System.Collections;
using System.Collections.Concurrent;
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

namespace QuickCull.WPF.Controls
{
    public partial class ImageListControl : UserControl
    {
        private readonly IThumbnailService _thumbnailService;
        private readonly ConcurrentDictionary<string, WeakReference<BitmapSource>> _thumbnailCache;
        private readonly SemaphoreSlim _thumbnailLoadingSemaphore;

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

        // Observable collections for binding
        private ObservableCollection<ImageListItemViewModel> _wrappedItems;
        private ObservableCollection<ImageGroupViewModel> _groupedItems;
        private INotifyCollectionChanged _currentCollection;

        public ImageListControl()
        {
            InitializeComponent();

            _thumbnailCache = new ConcurrentDictionary<string, WeakReference<BitmapSource>>();
            _thumbnailLoadingSemaphore = new SemaphoreSlim(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
            _thumbnailService = new ThumbnailService(new TraceLoggingService());

            _wrappedItems = new ObservableCollection<ImageListItemViewModel>();
            _groupedItems = new ObservableCollection<ImageGroupViewModel>();

            // Bind to virtualized ListView instead of ItemsControl
            VirtualizedListView.ItemsSource = _wrappedItems;
            GroupsContainer.ItemsSource = _groupedItems;

            // Handle selection in ListView
            VirtualizedListView.SelectionChanged += OnListViewSelectionChanged;
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
            // Handle collection changes efficiently
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                switch (e.Action)
                {
                    case NotifyCollectionChangedAction.Add:
                        if (e.NewItems != null)
                        {
                            foreach (ImageAnalysis item in e.NewItems)
                            {
                                var wrapper = new ImageListItemViewModel(item, _thumbnailService, _thumbnailCache, _thumbnailLoadingSemaphore);
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
                                    wrapper.Dispose(); // Clean up resources
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
                                    if (oldItem.GroupID != newItem.GroupID)
                                    {
                                        RemoveFromGroup(wrapper);
                                    }

                                    wrapper.ImageData = newItem;

                                    if (oldItem.GroupID != newItem.GroupID)
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
            }));
        }

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageListControl control)
            {
                control.UpdateSelection(e.OldValue, e.NewValue);
            }
        }

        // Handle ListView selection changes
        private void OnListViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is ImageListItemViewModel selectedWrapper)
            {
                var oldSelection = SelectedItem;
                var newSelection = selectedWrapper.ImageData;

                if (!ReferenceEquals(oldSelection, newSelection))
                {
                    SelectedItem = newSelection;

                    // Fire custom selection changed event
                    var args = new SelectionChangedEventArgs(SelectionChangedEvent,
                        oldSelection != null ? new[] { oldSelection } : new object[0],
                        new[] { newSelection });
                    RaiseEvent(args);
                }
            }
        }

        // Refresh the wrapped items collection efficiently
        private void RefreshItems()
        {
            // Clear existing items and dispose resources
            foreach (var item in _wrappedItems)
            {
                item.Dispose();
            }
            _wrappedItems.Clear();
            _groupedItems.Clear();

            if (ItemsSource != null)
            {
                // Create items in batches to avoid UI freezing
                var items = ItemsSource.Cast<ImageAnalysis>().ToList();

                // Process in chunks to maintain UI responsiveness
                ProcessItemsInBatches(items);
            }

            // Reapply selection if there was one
            if (SelectedItem != null)
            {
                UpdateSelection(null, SelectedItem);
            }
        }

        private async void ProcessItemsInBatches(List<ImageAnalysis> items)
        {
            const int batchSize = 100;

            for (int i = 0; i < items.Count; i += batchSize)
            {
                var batch = items.Skip(i).Take(batchSize);

                await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var item in batch)
                    {
                        var wrapper = new ImageListItemViewModel(item, _thumbnailService, _thumbnailCache, _thumbnailLoadingSemaphore);
                        _wrappedItems.Add(wrapper);
                        AddToGroup(wrapper);
                    }
                }));

                // Small delay to keep UI responsive
                if (i + batchSize < items.Count)
                {
                    await Task.Delay(1);
                }
            }
        }

        // Add item to appropriate group
        private void AddToGroup(ImageListItemViewModel wrapper)
        {
            var groupNumber = wrapper.ImageData.GroupID;
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
            var groupNumber = wrapper.ImageData.GroupID;
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

                    // Update ListView selection without triggering events
                    VirtualizedListView.SelectionChanged -= OnListViewSelectionChanged;
                    VirtualizedListView.SelectedItem = newWrapper;
                    VirtualizedListView.SelectionChanged += OnListViewSelectionChanged;

                    // Scroll to selected item
                    VirtualizedListView.ScrollIntoView(newWrapper);
                }
            }
        }

        // Handle mouse clicks on grouped items (since they're not in ListView)
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
                var oldGroup = wrapper.ImageData.GroupID;
                wrapper.ImageData = updatedImage;

                // Handle group changes
                if (oldGroup != updatedImage.GroupID)
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

        // Clean up resources
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_currentCollection != null)
            {
                _currentCollection.CollectionChanged -= OnSourceCollectionChanged;
                _currentCollection = null;
            }

            // Dispose all view models
            foreach (var item in _wrappedItems)
            {
                item.Dispose();
            }

            _thumbnailLoadingSemaphore?.Dispose();
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            this.Unloaded += OnUnloaded;
        }
    }

    // Enhanced ViewModel with efficient thumbnail loading and caching
    public class ImageListItemViewModel : INotifyPropertyChanged, IDisposable
    {
        private ImageAnalysis _imageData;
        private bool _isSelected;
        private BitmapSource _thumbnailSource;
        private readonly IThumbnailService _thumbnailService;
        private readonly ConcurrentDictionary<string, WeakReference<BitmapSource>> _thumbnailCache;
        private readonly SemaphoreSlim _thumbnailLoadingSemaphore;
        private volatile bool _thumbnailLoading = false;
        private volatile bool _disposed = false;
        private CancellationTokenSource _loadCancellation;

        public ImageListItemViewModel(ImageAnalysis imageData, IThumbnailService thumbnailService,
            ConcurrentDictionary<string, WeakReference<BitmapSource>> thumbnailCache,
            SemaphoreSlim thumbnailLoadingSemaphore)
        {
            _imageData = imageData;
            _thumbnailService = thumbnailService;
            _thumbnailCache = thumbnailCache;
            _thumbnailLoadingSemaphore = thumbnailLoadingSemaphore;
        }

        public ImageAnalysis ImageData
        {
            get => _imageData;
            set
            {
                _imageData = value;

                // Reset thumbnail when data changes
                _thumbnailSource = null;
                _thumbnailLoading = false;
                _loadCancellation?.Cancel();

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

        // Efficient thumbnail loading with caching and async loading
        public BitmapSource ThumbnailSource
        {
            get
            {
                if (_disposed || _imageData?.FilePath == null)
                    return null;

                // Check cache first
                if (_thumbnailCache.TryGetValue(_imageData.FilePath, out var weakRef) &&
                    weakRef.TryGetTarget(out var cachedThumbnail))
                {
                    _thumbnailSource = cachedThumbnail;
                    return _thumbnailSource;
                }

                // Return existing thumbnail if we have one
                if (_thumbnailSource != null)
                    return _thumbnailSource;

                // Load thumbnail asynchronously if not already loading
                if (!_thumbnailLoading)
                {
                    LoadThumbnailAsync();
                }

                return null; // Will be updated via PropertyChanged when loaded
            }
        }

        private async void LoadThumbnailAsync()
        {
            if (_disposed || _thumbnailLoading || _imageData?.FilePath == null)
                return;

            _thumbnailLoading = true;
            _loadCancellation?.Cancel();
            _loadCancellation = new CancellationTokenSource();
            var cancellationToken = _loadCancellation.Token;

            try
            {
                await _thumbnailLoadingSemaphore.WaitAsync(cancellationToken);

                try
                {
                    if (cancellationToken.IsCancellationRequested || _disposed)
                        return;

                    // Double-check cache in case another thread loaded it
                    if (_thumbnailCache.TryGetValue(_imageData.FilePath, out var weakRef) &&
                        weakRef.TryGetTarget(out var cachedThumbnail))
                    {
                        _thumbnailSource = cachedThumbnail;
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (!_disposed)
                                NotifyPropertyChanged(nameof(ThumbnailSource));
                        }));
                        return;
                    }

                    // Load thumbnail on background thread
                    var thumbnail = await Task.Run(() =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return null;

                        try
                        {
                            var bitmap = _thumbnailService.GetThumbnailImage(_imageData.FilePath);

                            // Freeze the bitmap for cross-thread access
                            if (bitmap != null && bitmap.CanFreeze)
                            {
                                bitmap.Freeze();
                            }

                            return bitmap;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error loading thumbnail for {_imageData.FilePath}: {ex.Message}");
                            return null;
                        }
                    }, cancellationToken);

                    if (cancellationToken.IsCancellationRequested || _disposed || thumbnail == null)
                        return;

                    // Cache the thumbnail with weak reference
                    _thumbnailCache.TryAdd(_imageData.FilePath, new WeakReference<BitmapSource>(thumbnail));
                    _thumbnailSource = thumbnail;

                    // Update UI on main thread
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!_disposed)
                            NotifyPropertyChanged(nameof(ThumbnailSource));
                    }));
                }
                finally
                {
                    _thumbnailLoadingSemaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in thumbnail loading process: {ex.Message}");
            }
            finally
            {
                _thumbnailLoading = false;
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
        public int Group => _imageData?.GroupID ?? 0;

        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = null;

            // Don't dispose the thumbnail source as it might be cached and used elsewhere
            _thumbnailSource = null;
        }
    }

    // ViewModel for grouped images with virtualization support
    public class ImageGroupViewModel : INotifyPropertyChanged
    {
        private int _groupNumber;
        private ObservableCollection<ImageListItemViewModel> _items;

        public ImageGroupViewModel(int groupNumber)
        {
            _groupNumber = groupNumber;
            _items = new ObservableCollection<ImageListItemViewModel>();
            _items.CollectionChanged += OnItemsChanged;
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