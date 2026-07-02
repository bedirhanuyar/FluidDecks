using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FluidDecks.UI.Controls
{
    public partial class DeckPanel : UserControl
    {
        // Collapsed dimensions (width x height including label area)
        private const double COLLAPSED_WIDTH = 80;
        private const double COLLAPSED_HEIGHT = 110;

        private bool _isRolledUp = false;
        private bool _isAnimating = false; // Guard to prevent rapid-click desync

        // Drag-vs-click tracking for file items
        private Point _itemDragStartPoint;
        private bool _isItemDragging = false;
        private const double DRAG_THRESHOLD = 5.0;

        public UI.ViewModels.PanelViewModel ViewModel => DataContext as UI.ViewModels.PanelViewModel;

        public DeckPanel()
        {
            InitializeComponent();
            this.Loaded += DeckPanel_Loaded;
            this.DataContextChanged += DeckPanel_DataContextChanged;
        }

        private void DeckPanel_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (ViewModel != null)
            {
                FileListBox.ItemsSource = ViewModel.Items;
            }
        }

        private void DeckPanel_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure FileListBox has a ScaleTransform
            FileListBox.RenderTransformOrigin = new Point(0.5, 0);
            FileListBox.RenderTransform = new ScaleTransform(1.0, 1.0);
            
            // Force start rolled up (collapsed) by default
            FileListBox.Visibility = Visibility.Collapsed;
            FileListBox.Opacity = 0;
            _isRolledUp = true;
            
            RootBorder.Width = COLLAPSED_WIDTH;
            RootBorder.Height = COLLAPSED_HEIGHT;
            ExpandedView.Visibility = Visibility.Collapsed;
            CollapsedView.Visibility = Visibility.Visible;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var win = Window.GetWindow(this);
            if (win == null || ViewModel == null) return;

            if (e.ClickCount == 1)
            {
                double oldX = win.Left;
                double oldY = win.Top;

                var deckWindow = win as UI.Windows.DeckWindow;

                // Native window drag (blur state is managed by expand/collapse, not drag)
                win.DragMove();

                // If window barely moved, treat as a click to expand/collapse
                if (Math.Abs(win.Left - oldX) < 3 && Math.Abs(win.Top - oldY) < 3)
                {
                    ToggleRollUp();
                }
                else
                {
                    // Update ViewModel position
                    ViewModel.X = win.Left;
                    ViewModel.Y = win.Top;

                    if (Application.Current.MainWindow?.DataContext is UI.ViewModels.MainViewModel mainViewModel)
                    {
                        // Check for merge using correct center offsets for collapsed dimensions
                        double myCenterX = ViewModel.X + (COLLAPSED_WIDTH / 2.0);
                        double myCenterY = ViewModel.Y + (COLLAPSED_HEIGHT / 2.0);
                        var targetPanel = mainViewModel.Panels.FirstOrDefault(p => 
                            p != ViewModel && 
                            Math.Abs(myCenterX - (p.X + COLLAPSED_WIDTH / 2.0)) < (COLLAPSED_WIDTH / 2.0) &&
                            Math.Abs(myCenterY - (p.Y + COLLAPSED_HEIGHT / 2.0)) < (COLLAPSED_HEIGHT / 2.0));

                        if (targetPanel != null)
                        {
                            if (ViewModel.CategoryName == "Desktop" || targetPanel.CategoryName == "Desktop")
                            {
                                MessageBox.Show("The 'Desktop' panel is not a physical folder and cannot be merged.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }

                            var result = MessageBox.Show($"Do you want to move '{ViewModel.CategoryName}' inside '{targetPanel.CategoryName}'?", "Merge Folders", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            if (result == MessageBoxResult.Yes)
                            {
                                string sourceDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), ViewModel.CategoryName);
                            string targetDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), targetPanel.CategoryName);

                            try
                            {
                                if (System.IO.Directory.Exists(sourceDir))
                                {
                                    if (!System.IO.Directory.Exists(targetDir))
                                    {
                                        MessageBox.Show($"The target folder was not found on the desktop.\n\nSearched Path:\n{targetDir}\n\nNote: This operation only works between real folders in 'Mirror Desktop' mode.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                                    }
                                    else
                                    {
                                        targetPanel.VirtualFolders.Add(ViewModel.CategoryName);
                                        
                                        var _configManager = mainViewModel.AppConfigManager;
                                        var targetConfig = _configManager.CurrentConfig.Panels.FirstOrDefault(p => p.CategoryName == targetPanel.CategoryName);
                                        if (targetConfig != null) {
                                            if (targetConfig.VirtualFolders == null) targetConfig.VirtualFolders = new System.Collections.Generic.List<string>();
                                            if (!targetConfig.VirtualFolders.Contains(ViewModel.CategoryName))
                                                targetConfig.VirtualFolders.Add(ViewModel.CategoryName);
                                        }

                                        var panelToRemove = mainViewModel.Panels.FirstOrDefault(p => p.CategoryName == ViewModel.CategoryName);
                                        if (panelToRemove != null) mainViewModel.Panels.Remove(panelToRemove);
                                        
                                        var fileItem = new FileItemViewModel
                                        {
                                            FilePath = sourceDir,
                                            FileName = ViewModel.CategoryName,
                                            IsVirtualFolder = true
                                        };
                                        fileItem.LoadIcon();
                                        targetPanel.Items.Add(fileItem);
                                        
                                        _configManager.SaveConfig();
                                        return;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Failed to merge folders: {ex.Message}", "Merge Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                            } // End of if (result == MessageBoxResult.Yes)
                        }
                        
                        mainViewModel.SavePanelPosition(ViewModel.CategoryName, ViewModel.X, ViewModel.Y);
                    }
                }
            }
        }

        private void ToggleRollUp()
        {
            FluidDecks.Core.Logging.Logger.Log($"ToggleRollUp called for {ViewModel?.CategoryName}", "INFO");
            try
            {
                if (ViewModel == null) return;

                // Prevent re-entry during animation
                if (_isAnimating) return;
                _isAnimating = true;

                var deckWindow = Window.GetWindow(this) as UI.Windows.DeckWindow;
                var mainViewModel = Application.Current.MainWindow.DataContext as UI.ViewModels.MainViewModel;
                double scale = mainViewModel != null ? mainViewModel.GlobalScale : 1.0;
                double workAreaWidth = SystemParameters.WorkArea.Width / scale;
                double workAreaHeight = SystemParameters.WorkArea.Height / scale;

                if (_isRolledUp)
                {
                    // EXPAND POPUP — apply blur for frosted glass backdrop
                    
                    // Dynamic Sizing Algorithm:
                    // - Columns grow based on item count, capped by what fits on screen
                    // - Height grows with rows, capped by screen bounds (scroll activates beyond that)
                    int itemCount = ViewModel.Items.Count;
                    if (itemCount == 0) itemCount = 1;

                    double itemWidth = 83;   // item border (75) + margins (4*2)
                    double itemHeight = 95;  // item minHeight (60) + icon + text + margins
                    double padding = 30;     // horizontal padding (margins + border)
                    double headerHeight = 50; // category name header

                    double maxRatio = mainViewModel != null ? mainViewModel.AppConfigManager.CurrentConfig.PopupMaxScreenRatio : 0.6;
                    double folderScale = mainViewModel != null ? mainViewModel.AppConfigManager.CurrentConfig.FolderPanelScale : 1.0;

                    // Calculate maximum columns that physically fit on screen
                    double maxAvailWidth = workAreaWidth * maxRatio;
                    double maxAvailHeight = workAreaHeight * maxRatio;
                    int maxVisibleCols = Math.Max(2, (int)Math.Floor((maxAvailWidth - padding) / itemWidth));

                    // Ideal columns: square-ish layout based on item count
                    int idealCols = (int)Math.Ceiling(Math.Sqrt(itemCount));
                    int cols = Math.Clamp(idealCols, 2, maxVisibleCols);

                    int rows = (int)Math.Ceiling((double)itemCount / cols);

                    double targetWidth = cols * itemWidth + padding;
                    double targetHeight = rows * itemHeight + headerHeight + padding;

                    // Apply folder scale
                    targetWidth *= folderScale;
                    targetHeight *= folderScale;

                    // Ensure minimum readable size
                    targetWidth = Math.Max(200, targetWidth);
                    targetHeight = Math.Max(160, targetHeight);

                    // Clamp width to available screen space
                    if (targetWidth > maxAvailWidth) targetWidth = maxAvailWidth;
                    // Clamp height — beyond this, the ListBox ScrollViewer handles overflow
                    if (targetHeight > maxAvailHeight) targetHeight = maxAvailHeight;

                    // Adjust window position if expansion would overflow the screen edges
                    if (deckWindow != null)
                    {
                        if (deckWindow.Left + targetWidth > SystemParameters.WorkArea.Right)
                            deckWindow.Left = Math.Max(0, SystemParameters.WorkArea.Right - targetWidth - 10);
                        
                        if (deckWindow.Top + targetHeight > SystemParameters.WorkArea.Bottom)
                            deckWindow.Top = Math.Max(0, SystemParameters.WorkArea.Bottom - targetHeight - 10);
                    }

                    FileListBox.Visibility = Visibility.Visible;
                    FileListBox.Opacity = 1;
                    FileListBox.Height = double.NaN;
                    
                    CollapsedView.Visibility = Visibility.Collapsed;
                    ExpandedView.Visibility = Visibility.Visible;
                    ExpandedView.Opacity = 1;

                    // Apply dynamic corner radius from config
                    double expandedRadius = mainViewModel?.AppConfigManager?.CurrentConfig?.ExpandedCornerRadius ?? 16.0;
                    
                    // Windows 11 DWM Synchronization: If blur is enabled, we must lock the WPF corner radius 
                    // to perfectly match the Windows 11 DWM native corner radius so they don't visually desync.
                    if (deckWindow != null && mainViewModel?.AppConfigManager?.CurrentConfig?.EnableBlurEffect == true)
                    {
                        var blurMode = mainViewModel.AppConfigManager.CurrentConfig.BackgroundBlurMode;
                        if (blurMode == FluidDecks.Core.Configuration.BlurMode.Acrylic || blurMode == FluidDecks.Core.Configuration.BlurMode.Standard)
                        {
                            int pref = mainViewModel.AppConfigManager.CurrentConfig.BlurCornerPreference;
                            if (pref == 0) expandedRadius = 0;
                            else if (pref == 1) expandedRadius = 4;
                            else if (pref == 2) expandedRadius = 8;
                        }
                    }

                    RootBorder.CornerRadius = new CornerRadius(expandedRadius);
                    
                    // Fixed size before animation to prevent WrapPanel reflow
                    ExpandedView.Width = targetWidth;
                    ExpandedView.Height = targetHeight;

                    // Read animation settings
                    int easingType = mainViewModel?.AppConfigManager?.CurrentConfig?.AnimationEasing ?? 0;
                    double speedMult = mainViewModel?.AppConfigManager?.CurrentConfig?.AnimationSpeed ?? 1.0;
                    if (speedMult <= 0) speedMult = 1.0;
                    double durationMs = 300 / speedMult;

                    System.Windows.Media.Animation.IEasingFunction easingFunc;
                    if (easingType == 1) easingFunc = new CubicEase { EasingMode = EasingMode.EaseOut };
                    else if (easingType == 2) easingFunc = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
                    else easingFunc = new QuarticEase { EasingMode = EasingMode.EaseOut };

                    double animFromWidth = deckWindow != null ? deckWindow.ActualWidth : COLLAPSED_WIDTH;
                    double animFromHeight = deckWindow != null ? deckWindow.ActualHeight : COLLAPSED_HEIGHT;

                    // INSTANTLY resize the transparent window, so we only animate the internal WPF elements
                    if (deckWindow != null)
                    {
                        deckWindow.BeginAnimation(Window.WidthProperty, null);
                        deckWindow.BeginAnimation(Window.HeightProperty, null);
                        deckWindow.Width = targetWidth;
                        deckWindow.Height = targetHeight;
                    }
                    
                    // Set starting size for the border before animation
                    RootBorder.Width = animFromWidth;
                    RootBorder.Height = animFromHeight;

                    var widthAnim = new DoubleAnimation(animFromWidth, targetWidth, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easingFunc };
                    var heightAnim = new DoubleAnimation(animFromHeight, targetHeight, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easingFunc };
                    
                    var sb = new System.Windows.Media.Animation.Storyboard();
                    System.Windows.Media.Animation.Storyboard.SetTarget(widthAnim, RootBorder);
                    System.Windows.Media.Animation.Storyboard.SetTargetProperty(widthAnim, new PropertyPath(FrameworkElement.WidthProperty));
                    
                    System.Windows.Media.Animation.Storyboard.SetTarget(heightAnim, RootBorder);
                    System.Windows.Media.Animation.Storyboard.SetTargetProperty(heightAnim, new PropertyPath(FrameworkElement.HeightProperty));
                    
                    sb.Children.Add(widthAnim);
                    sb.Children.Add(heightAnim);

                    sb.Completed += (s, e) => {
                        RootBorder.BeginAnimation(FrameworkElement.WidthProperty, null);
                        RootBorder.BeginAnimation(FrameworkElement.HeightProperty, null);
                        RootBorder.Width = targetWidth;
                        RootBorder.Height = targetHeight;
                        ReloadResources();
                        // Apply blur AFTER expansion animation completes
                        deckWindow?.EnableBlur();
                        _isAnimating = false;
                    };

                    sb.Begin();
                    _isRolledUp = false;
                    FluidDecks.Core.Logging.Logger.Log($"Expanded folder {ViewModel.CategoryName} to size {targetWidth}x{targetHeight} (cols={cols}, rows={rows})", "INFO");
                }
                else
                {
                    // COLLAPSE TO SQUARE
                    if (ViewModel != null && ViewModel.IsTemporaryPopup)
                    {
                        if (mainViewModel != null)
                        {
                            deckWindow?.DisableBlur();
                            mainViewModel.Panels.Remove(ViewModel);
                            ReleaseResources();
                            _isAnimating = false;
                            return;
                        }
                    }

                    deckWindow?.DisableBlur();
                    _isRolledUp = true;

                    // Apply dynamic corner radius from config
                    double collapsedRadius = mainViewModel?.AppConfigManager?.CurrentConfig?.CollapsedCornerRadius ?? 8.0;
                    RootBorder.CornerRadius = new CornerRadius(collapsedRadius);
                    
                    CollapsedView.Visibility = Visibility.Visible;
                    ExpandedView.Visibility = Visibility.Collapsed;

                    // Read animation settings
                    int easingType = mainViewModel?.AppConfigManager?.CurrentConfig?.AnimationEasing ?? 0;
                    double speedMult = mainViewModel?.AppConfigManager?.CurrentConfig?.AnimationSpeed ?? 1.0;
                    if (speedMult <= 0) speedMult = 1.0;
                    double durationMs = 250 / speedMult;

                    System.Windows.Media.Animation.IEasingFunction easingFunc;
                    if (easingType == 1) easingFunc = new CubicEase { EasingMode = EasingMode.EaseOut };
                    else if (easingType == 2) easingFunc = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
                    else easingFunc = new QuarticEase { EasingMode = EasingMode.EaseOut };

                    var widthAnim = new DoubleAnimation(RootBorder.ActualWidth, COLLAPSED_WIDTH, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easingFunc };
                    var heightAnim = new DoubleAnimation(RootBorder.ActualHeight, COLLAPSED_HEIGHT, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easingFunc };
                    
                    var sb = new System.Windows.Media.Animation.Storyboard();
                    System.Windows.Media.Animation.Storyboard.SetTarget(widthAnim, RootBorder);
                    System.Windows.Media.Animation.Storyboard.SetTargetProperty(widthAnim, new PropertyPath(FrameworkElement.WidthProperty));
                    
                    System.Windows.Media.Animation.Storyboard.SetTarget(heightAnim, RootBorder);
                    System.Windows.Media.Animation.Storyboard.SetTargetProperty(heightAnim, new PropertyPath(FrameworkElement.HeightProperty));
                    
                    sb.Children.Add(widthAnim);
                    sb.Children.Add(heightAnim);
                    
                    sb.Completed += (s, e) => {
                        RootBorder.BeginAnimation(FrameworkElement.WidthProperty, null);
                        RootBorder.BeginAnimation(FrameworkElement.HeightProperty, null);
                        RootBorder.Width = COLLAPSED_WIDTH;
                        RootBorder.Height = COLLAPSED_HEIGHT;
                        
                        // INSTANTLY snap the transparent window down to match AFTER the visual animation is done
                        if (deckWindow != null) {
                            deckWindow.BeginAnimation(Window.WidthProperty, null);
                            deckWindow.BeginAnimation(Window.HeightProperty, null);
                            deckWindow.Width = COLLAPSED_WIDTH;
                            deckWindow.Height = COLLAPSED_HEIGHT;
                        }
                        
                        ExpandedView.Width = double.NaN;
                        ExpandedView.Height = double.NaN;
                        FileListBox.Visibility = Visibility.Collapsed;
                        ReleaseResources();
                        deckWindow?.DisableBlur();
                        _isAnimating = false;
                        FluidDecks.Core.Logging.Logger.Log($"Collapsed folder {ViewModel?.CategoryName}", "INFO");
                    };

                    sb.Begin();
                }
            }
            catch (Exception ex)
            {
                _isAnimating = false;
                FluidDecks.Core.Logging.Logger.Log($"Error in ToggleRollUp for {ViewModel?.CategoryName}", "ERROR", ex);
                throw;
            }
        }

        private void ReleaseResources()
        {
            if (ViewModel == null) return;
            // Set image sources to null to release memory for GDI/Bitmap objects
            // Skip the first 4 items because they are needed for the MiniGrid in collapsed mode!
            for (int i = 0; i < ViewModel.Items.Count; i++)
            {
                if (i >= 4)
                {
                    ViewModel.Items[i].ReleaseIcon();
                }
            }
            // Suggest GC
            GC.Collect(0, GCCollectionMode.Optimized);
        }

        private void ReloadResources()
        {
            if (ViewModel == null) return;
            // Re-fetch system icons when unfolded
            foreach (var item in ViewModel.Items)
            {
                item.LoadIcon();
            }
        }

        // --- Click-on-release pattern for file items ---

        private void FileItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Record start position for drag threshold detection
            _itemDragStartPoint = e.GetPosition(null);
            _isItemDragging = false;
            // Capture mouse so we reliably get MouseUp even if cursor leaves the element
            if (sender is FrameworkElement fe)
            {
                fe.CaptureMouse();
            }
        }

        private void FileItem_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                fe.ReleaseMouseCapture();

                // Only open if the item was NOT dragged
                if (!_isItemDragging && fe.DataContext is FileItemViewModel item)
                {
                    OpenFileItem(item);
                }
            }
            _isItemDragging = false;
        }

        private void FileItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement fe && fe.DataContext is FileItemViewModel item)
            {
                Point currentPos = e.GetPosition(null);
                Vector diff = currentPos - _itemDragStartPoint;

                // Only start drag if the mouse moved beyond the threshold
                if (Math.Abs(diff.X) > DRAG_THRESHOLD || Math.Abs(diff.Y) > DRAG_THRESHOLD)
                {
                    _isItemDragging = true;
                    fe.ReleaseMouseCapture();
                    // Start drag and drop
                    DragDrop.DoDragDrop(fe, new DataObject(DataFormats.FileDrop, new string[] { item.FilePath }), DragDropEffects.Move);
                }
            }
        }

        private void OpenFileItem(FileItemViewModel item)
        {
            try
            {
                if (item.IsVirtualFolder)
                {
                    var mainViewModel = Application.Current.MainWindow.DataContext as UI.ViewModels.MainViewModel;
                    if (mainViewModel != null)
                    {
                        if (!mainViewModel.AppConfigManager.CurrentConfig.OpenVirtualFoldersInApp)
                        {
                            System.Diagnostics.Process.Start("explorer.exe", item.FilePath);
                            return;
                        }

                        // Prevent duplicate popups: if a temporary popup for this folder already exists, toggle it closed
                        var existingPopup = mainViewModel.Panels.FirstOrDefault(p =>
                            p.IsTemporaryPopup && p.CategoryName == item.FileName);
                        if (existingPopup != null)
                        {
                            mainViewModel.Panels.Remove(existingPopup);
                            return;
                        }

                        // Create temporary popup panel
                        var popupPanel = new UI.ViewModels.PanelViewModel
                        {
                            CategoryName = item.FileName,
                            X = ViewModel.X + (ExpandedView.IsVisible ? ExpandedView.ActualWidth : COLLAPSED_WIDTH) + 15,
                            Y = ViewModel.Y,
                            Width = ViewModel.Width,
                            IsTemporaryPopup = true
                        };
                        popupPanel.LoadFolderIcon();
                        
                        // Load physical items in this virtual folder manually since it's a popup
                        if (System.IO.Directory.Exists(item.FilePath))
                        {
                            foreach (var file in System.IO.Directory.GetFiles(item.FilePath))
                            {
                                var fi = new FileItemViewModel { FilePath = file, FileName = System.IO.Path.GetFileName(file) };
                                fi.LoadIcon();
                                popupPanel.Items.Add(fi);
                            }
                            foreach (var dir in System.IO.Directory.GetDirectories(item.FilePath))
                            {
                                var di = new FileItemViewModel { FilePath = dir, FileName = System.IO.Path.GetFileName(dir), IsVirtualFolder = true };
                                di.LoadIcon();
                                popupPanel.Items.Add(di);
                            }
                        }
                        
                        mainViewModel.Panels.Add(popupPanel);
                    }
                }
                else
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.FilePath) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Panel_Drop(object sender, DragEventArgs e)
        {
            if (ViewModel == null) return;
            var mainViewModel = Application.Current.MainWindow?.DataContext as UI.ViewModels.MainViewModel;
            
            if (mainViewModel?.AppConfigManager.CurrentConfig.FolderLayoutMode == Core.Configuration.FolderMode.VirtualDecks)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0)
                    {
                        var panelConfig = mainViewModel.AppConfigManager.CurrentConfig.Panels.FirstOrDefault(p => p.CategoryName == ViewModel.CategoryName);
                        if (panelConfig != null)
                        {
                            foreach (var file in files)
                            {
                                if (!panelConfig.VirtualItems.Contains(file))
                                {
                                    panelConfig.VirtualItems.Add(file);
                                    
                                    var isDir = System.IO.Directory.Exists(file);
                                    var itemVM = new FileItemViewModel
                                    {
                                        FilePath = file,
                                        FileName = System.IO.Path.GetFileName(file),
                                        IsVirtualFolder = isDir
                                    };
                                    itemVM.LoadIcon();
                                    ViewModel.Items.Add(itemVM);
                                }
                                else
                                {
                                    // Reordering existing item
                                    var droppedItem = ViewModel.Items.FirstOrDefault(i => i.FilePath == file);
                                    if (droppedItem != null)
                                    {
                                        int oldIndex = ViewModel.Items.IndexOf(droppedItem);
                                        int newIndex = -1;
                                        
                                        // Hit test to find drop target
                                        Point dropPosition = e.GetPosition(FileListBox);
                                        var hit = System.Windows.Media.VisualTreeHelper.HitTest(FileListBox, dropPosition);
                                        if (hit != null && hit.VisualHit is FrameworkElement element && element.DataContext is FileItemViewModel targetItem)
                                        {
                                            newIndex = ViewModel.Items.IndexOf(targetItem);
                                        }
                                        else
                                        {
                                            // Dropped at the end
                                            newIndex = ViewModel.Items.Count - 1;
                                        }

                                        if (newIndex >= 0 && newIndex != oldIndex)
                                        {
                                            ViewModel.Items.Move(oldIndex, newIndex);
                                            
                                            // Update config list order
                                            panelConfig.VirtualItems.RemoveAt(oldIndex);
                                            panelConfig.VirtualItems.Insert(newIndex, file);
                                        }
                                    }
                                }
                            }
                            mainViewModel.AppConfigManager.SaveConfig();
                        }
                    }
                }
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                string targetDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), ViewModel.CategoryName);

                if (System.IO.Directory.Exists(targetDir))
                {
                    foreach (string file in files)
                    {
                        try
                        {
                            string fileName = System.IO.Path.GetFileName(file);
                            string destFile = System.IO.Path.Combine(targetDir, fileName);
                            if (file != destFile)
                            {
                                if (System.IO.Directory.Exists(file))
                                    System.IO.Directory.Move(file, destFile);
                                else
                                    System.IO.File.Move(file, destFile);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Failed to move file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
            e.Handled = true;
        }

        private void MenuItemDeleteFolder_Click(object sender, RoutedEventArgs e)
        {
            var mainViewModel = Application.Current.MainWindow?.DataContext as UI.ViewModels.MainViewModel;
            if (mainViewModel?.AppConfigManager.CurrentConfig.FolderLayoutMode == Core.Configuration.FolderMode.VirtualDecks)
            {
                var result = MessageBox.Show($"Are you sure you want to delete the virtual folder '{ViewModel.CategoryName}'?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    var panelConfig = mainViewModel.AppConfigManager.CurrentConfig.Panels.FirstOrDefault(p => p.CategoryName == ViewModel.CategoryName);
                    if (panelConfig != null)
                    {
                        mainViewModel.AppConfigManager.CurrentConfig.Panels.Remove(panelConfig);
                        mainViewModel.AppConfigManager.SaveConfig();
                    }
                    mainViewModel.Panels.Remove(ViewModel);
                }
            }
        }

        private void MenuItemRemoveFile_Click(object sender, RoutedEventArgs e)
        {
            var mainViewModel = Application.Current.MainWindow?.DataContext as UI.ViewModels.MainViewModel;
            if (mainViewModel?.AppConfigManager.CurrentConfig.FolderLayoutMode == Core.Configuration.FolderMode.VirtualDecks)
            {
                if (sender is MenuItem menuItem && menuItem.DataContext is FileItemViewModel item)
                {
                    var panelConfig = mainViewModel.AppConfigManager.CurrentConfig.Panels.FirstOrDefault(p => p.CategoryName == ViewModel.CategoryName);
                    if (panelConfig != null)
                    {
                        panelConfig.VirtualItems.Remove(item.FilePath);
                        
                        // Clean up custom name if it exists
                        if (panelConfig.VirtualItemNames.ContainsKey(item.FilePath))
                        {
                            panelConfig.VirtualItemNames.Remove(item.FilePath);
                        }
                        
                        mainViewModel.AppConfigManager.SaveConfig();
                    }
                    ViewModel.Items.Remove(item);
                }
            }
        }

        private void MenuItemOpenLocation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is FileItemViewModel item)
            {
                if (System.IO.File.Exists(item.FilePath) || System.IO.Directory.Exists(item.FilePath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
                }
            }
        }

        private void MenuItemRename_Click(object sender, RoutedEventArgs e)
        {
            var mainViewModel = Application.Current.MainWindow?.DataContext as UI.ViewModels.MainViewModel;
            if (mainViewModel?.AppConfigManager.CurrentConfig.FolderLayoutMode == Core.Configuration.FolderMode.VirtualDecks)
            {
                if (sender is MenuItem menuItem && menuItem.DataContext is FileItemViewModel item)
                {
                    var inputDialog = new UI.Windows.InputDialog("Enter a new name:", item.FileName);
                    if (inputDialog.ShowDialog() == true)
                    {
                        string newName = inputDialog.InputText;
                        if (!string.IsNullOrWhiteSpace(newName))
                        {
                            item.FileName = newName;
                            
                            var panelConfig = mainViewModel.AppConfigManager.CurrentConfig.Panels.FirstOrDefault(p => p.CategoryName == ViewModel.CategoryName);
                            if (panelConfig != null)
                            {
                                panelConfig.VirtualItemNames[item.FilePath] = newName;
                                mainViewModel.AppConfigManager.SaveConfig();
                            }
                        }
                    }
                }
            }
        }

        private void Panel_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Move;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    /// <summary>
    /// ViewModel for items in the Deck.
    /// Implements logic to lazy load and release high quality Windows icons to save RAM.
    /// </summary>
    public class FileItemViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public bool IsVirtualFolder { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }

        private IntPtr _hIcon = IntPtr.Zero;

        private System.Windows.Media.ImageSource _icon;
        public System.Windows.Media.ImageSource Icon
        {
            get => _icon;
            set
            {
                _icon = value;
                OnPropertyChanged(nameof(Icon));
            }
        }

        public void LoadIcon()
        {
            if (_icon == null && !string.IsNullOrEmpty(FilePath))
            {
                // Uses IconExtractor which calls SHGetFileInfo asynchronously to prevent UI freeze
                System.Threading.Tasks.Task.Run(() =>
                {
                    var iconSource = Services.IconExtractor.GetIcon(FilePath, out IntPtr hIcon);
                    
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _hIcon = hIcon;
                        Icon = iconSource;
                    });
                });
            }
        }

        public void ReleaseIcon()
        {
            Icon = null;
            if (_hIcon != IntPtr.Zero)
            {
                // Safely destroy the native icon handle to prevent unmanaged RAM leakage
                Services.IconExtractor.ReleaseIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
