using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using FluidDecks.Core.Configuration;
using FluidDecks.Services;
using FluidDecks.UI.Controls;

namespace FluidDecks.UI.ViewModels
{
    public class MainViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public double GlobalScale => System.Windows.SystemParameters.PrimaryScreenWidth / 1920.0;

        public ObservableCollection<PanelViewModel> Panels { get; set; } = new ObservableCollection<PanelViewModel>();

        private readonly ConfigManager _configManager;
        private readonly DesktopWatcherService _watcherService;
        public ConfigManager AppConfigManager => _configManager;

        public MainViewModel(ConfigManager configManager, DesktopWatcherService watcherService)
        {
            _configManager = configManager;
            _watcherService = watcherService;

            _configManager.OnConfigChanged += ConfigManager_OnConfigChanged;
            
            _watcherService.OnFileAdded += WatcherService_OnFileAdded;
            _watcherService.OnFilesBatchAdded += WatcherService_OnFilesBatchAdded;
            _watcherService.OnFileRemoved += WatcherService_OnFileRemoved;
            _watcherService.OnCategoryAdded += WatcherService_OnCategoryAdded;
            
            LoadPanelsFromConfig();
            
            _watcherService.FolderMode = _configManager.CurrentConfig.FolderLayoutMode;
            _watcherService.Start();
        }

        private void ConfigManager_OnConfigChanged(AppConfig config)
        {
            // Update panel properties dynamically
            foreach (var panelConfig in config.Panels)
            {
                var existingPanel = Panels.FirstOrDefault(p => p.CategoryName == panelConfig.CategoryName);
                if (existingPanel != null)
                {
                    existingPanel.X = panelConfig.X;
                    existingPanel.Y = panelConfig.Y;
                    existingPanel.Width = panelConfig.Width;
                }
            }
            
            // Propagate icon scale to all panels so bindings update live
            foreach (var panel in Panels)
            {
                panel.IconScale = config.IconScale;
            }
            
            // Update extension map and watcher settings
            _watcherService.ExtensionToCategoryMap = config.ExtensionToCategoryMap;
            _watcherService.MaxDepth = config.MaxDepth;
            _watcherService.FolderMode = config.FolderLayoutMode;
        }

        public void RestartDesktopWatcher()
        {
            FluidDecks.Core.Logging.Logger.Log("RestartDesktopWatcher called.", "INFO");
            try
            {
                _watcherService.Stop();
                foreach (var panel in Panels)
                {
                    foreach (var item in panel.Items) item.ReleaseIcon();
                    panel.Items.Clear();
                }
                LoadPanelsFromConfig();
                _watcherService.FolderMode = _configManager.CurrentConfig.FolderLayoutMode;
                _watcherService.Start();
                FluidDecks.Core.Logging.Logger.Log("RestartDesktopWatcher completed successfully.", "INFO");
            }
            catch (Exception ex)
            {
                FluidDecks.Core.Logging.Logger.Log("Error in RestartDesktopWatcher", "ERROR", ex);
                throw;
            }
        }

        private double _lastPlacedX = 50;
        private double _lastPlacedY = 50;
        private readonly object _placementLock = new object();

        private void LoadPanelsFromConfig()
        {
            FluidDecks.Core.Logging.Logger.Log("LoadPanelsFromConfig started.", "INFO");
            try
            {
                Panels.Clear();
                _lastPlacedX = 50;
                _lastPlacedY = 50;
                
                var panelsToRemove = new System.Collections.Generic.List<Core.Configuration.PanelConfig>();
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                
                foreach (var pConfig in _configManager.CurrentConfig.Panels)
                {
                    if (string.IsNullOrEmpty(pConfig.CategoryName)) continue;
                    
                    if (pConfig.ModeOwner != _configManager.CurrentConfig.FolderLayoutMode)
                    {
                        continue; // Strict isolation between modes
                    }
                    
                    if (_configManager.CurrentConfig.FolderLayoutMode == Core.Configuration.FolderMode.MirrorDesktop)
                    {
                        string folderPath = System.IO.Path.Combine(desktopPath, pConfig.CategoryName);
                        if (!System.IO.Directory.Exists(folderPath))
                        {
                            continue; // Skip loading it, but DO NOT delete it from config!
                        }
                    }
                    
                    var panel = new PanelViewModel
                    {
                        CategoryName = pConfig.CategoryName,
                        X = pConfig.X,
                        Y = pConfig.Y,
                        Width = pConfig.Width,
                        IconScale = _configManager.CurrentConfig.IconScale
                    };
                    
                    if (pConfig.VirtualFolders != null)
                    {
                        foreach (var vf in pConfig.VirtualFolders)
                            panel.VirtualFolders.Add(vf);
                    }
                    
                    if (pConfig.VirtualItems != null && _configManager.CurrentConfig.FolderLayoutMode == Core.Configuration.FolderMode.VirtualDecks)
                    {
                        foreach (var vi in pConfig.VirtualItems)
                        {
                            var isDir = System.IO.Directory.Exists(vi);
                            string displayName = System.IO.Path.GetFileName(vi);
                            if (pConfig.VirtualItemNames != null && pConfig.VirtualItemNames.TryGetValue(vi, out string customName))
                            {
                                displayName = customName;
                            }

                            var itemVM = new FileItemViewModel
                            {
                                FilePath = vi,
                                FileName = displayName,
                                IsVirtualFolder = isDir
                            };
                            itemVM.LoadIcon();
                            panel.Items.Add(itemVM);
                        }
                    }
                    
                    panel.LoadFolderIcon();
                    Panels.Add(panel);
                }
                
                FluidDecks.Core.Logging.Logger.Log("LoadPanelsFromConfig completed.", "INFO");
            }
            catch (Exception ex)
            {
                FluidDecks.Core.Logging.Logger.Log("Error in LoadPanelsFromConfig", "ERROR", ex);
                throw;
            }
        }

        private void WatcherService_OnFilesBatchAdded(System.Collections.Generic.List<(string FilePath, string Category)> batch)
        {
            var categoryGroups = batch.GroupBy(b => b.Category);

            foreach (var group in categoryGroups)
            {
                string category = group.Key;
                PanelViewModel targetPanel = null;
                var parentPanel = Panels.FirstOrDefault(p => p.VirtualFolders.Contains(category, StringComparer.OrdinalIgnoreCase));
                
                if (parentPanel != null)
                {
                    targetPanel = parentPanel;
                }
                else
                {
                    targetPanel = Panels.FirstOrDefault(p => p.CategoryName.Equals(category, StringComparison.OrdinalIgnoreCase));
                }

                if (targetPanel == null)
                {
                    lock (_placementLock)
                    {
                        if (Panels.Count == 0 && _lastPlacedX == 50 && _lastPlacedY == 50) { }
                        else
                        {
                            _lastPlacedX += 270;
                            if (_lastPlacedX + 250 > SystemParameters.VirtualScreenWidth - 100)
                            {
                                _lastPlacedX = 50;
                                _lastPlacedY += 350;
                            }
                        }

                        targetPanel = new PanelViewModel { 
                            CategoryName = category, X = _lastPlacedX, Y = _lastPlacedY, Width = 250
                        };
                        targetPanel.LoadFolderIcon();
                        
                        _configManager.CurrentConfig.Panels.Add(new PanelConfig { 
                            CategoryName = category, X = _lastPlacedX, Y = _lastPlacedY, Width = 250, ModeOwner = _configManager.CurrentConfig.FolderLayoutMode 
                        });
                        
                        Panels.Add(targetPanel);
                    }
                }

                if (targetPanel != null)
                {
                    foreach (var item in group)
                    {
                        if (parentPanel != null && !System.IO.Path.GetFileName(item.FilePath).Equals(category, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // Skip files inside a virtualized folder unless it's the folder itself
                        }

                        if (!targetPanel.Items.Any(i => i.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase)))
                        {
                            var fileItem = new FileItemViewModel
                            {
                                FilePath = item.FilePath,
                                FileName = System.IO.Path.GetFileName(item.FilePath),
                                IsVirtualFolder = System.IO.Directory.Exists(item.FilePath)
                            };
                            
                            fileItem.LoadIcon();
                            targetPanel.Items.Add(fileItem);
                        }
                    }
                }
            }
        }

        private void WatcherService_OnFileAdded(string filePath, string category)
        {
            // First check if this category (folder) is virtually grouped inside another panel!
            PanelViewModel targetPanel = null;
            
            // Wait, category is the name of the folder being detected (if depth=1) or the top level folder (if depth=2).
            // But if the folder itself (e.g. "FolderA") is virtually inside "FolderB", 
            // when "FolderA" is processed by InitialScan, GetCategoryForFile returns "FolderA".
            // Let's see if "FolderA" is in any panel's VirtualFolders list.
            var parentPanel = Panels.FirstOrDefault(p => p.VirtualFolders.Contains(category, StringComparer.OrdinalIgnoreCase));
            
            if (parentPanel != null)
            {
                // This entire category is virtually grouped inside parentPanel!
                // So instead of a standalone panel, it belongs as a FileItem inside parentPanel.
                // Wait! If the detected file is the "FolderA" directory ITSELF, we add it as an item.
                // If it's a file inside "FolderA", we do NOT add it to the parentPanel (we only show the folder as an item).
                string name = System.IO.Path.GetFileName(filePath);
                if (name.Equals(category, StringComparison.OrdinalIgnoreCase)) 
                {
                    targetPanel = parentPanel;
                }
                else
                {
                    // It's a file INSIDE FolderA. Since FolderA is just an item now, we don't display its contents on the main UI.
                    return; 
                }
            }
            else
            {
                targetPanel = Panels.FirstOrDefault(p => p.CategoryName.Equals(category, StringComparison.OrdinalIgnoreCase));
            }
            
            if (targetPanel == null)
            {
                lock (_placementLock)
                {
                    if (Panels.Count == 0 && _lastPlacedX == 50 && _lastPlacedY == 50)
                    {
                        // Starting point
                    }
                    else
                    {
                        _lastPlacedX += 270; // Width + margin
                        if (_lastPlacedX + 250 > SystemParameters.VirtualScreenWidth - 100)
                        {
                            _lastPlacedX = 50;
                            _lastPlacedY += 350;
                        }
                    }

                    // Dynamically create a panel for this category!
                    targetPanel = new PanelViewModel { 
                        CategoryName = category, 
                        X = _lastPlacedX, 
                        Y = _lastPlacedY, 
                        Width = 250
                    };
                    targetPanel.LoadFolderIcon();
                    
                    // Add it to config so it remembers position later
                    _configManager.CurrentConfig.Panels.Add(new PanelConfig { 
                        CategoryName = category, 
                        X = _lastPlacedX, 
                        Y = _lastPlacedY, 
                        Width = 250 
                    });
                    
                    // Needs to run on UI thread if we are modifying ObservableCollection from watcher thread
                    Application.Current.Dispatcher.Invoke(() => {
                        Panels.Add(targetPanel);
                    });
                }
            }

            if (targetPanel != null)
            {
                // Check if already exists
                if (!targetPanel.Items.Any(i => i.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    var fileItem = new FileItemViewModel
                    {
                        FilePath = filePath,
                        FileName = System.IO.Path.GetFileName(filePath),
                        IsVirtualFolder = System.IO.Directory.Exists(filePath)
                    };
                    
                    fileItem.LoadIcon();
                    
                    targetPanel.Items.Add(fileItem);
                }
            }
        }

        private void WatcherService_OnCategoryAdded(string category)
        {
            var targetPanel = Panels.FirstOrDefault(p => p.CategoryName.Equals(category, StringComparison.OrdinalIgnoreCase));
            
            if (targetPanel == null)
            {
                // Create NEW panel for this folder!
                var newConfig = new Core.Configuration.PanelConfig
                {
                    CategoryName = category,
                    X = _lastPlacedX,
                    Y = _lastPlacedY,
                    Width = 250
                };
                
                targetPanel = new PanelViewModel
                {
                    CategoryName = category,
                    X = _lastPlacedX,
                    Y = _lastPlacedY,
                    Width = 250,
                    IconScale = _configManager.CurrentConfig.IconScale
                };
                targetPanel.LoadFolderIcon();
                
                _lastPlacedX += 30;
                _lastPlacedY += 30;
                
                Panels.Add(targetPanel);
                
                _configManager.CurrentConfig.Panels.Add(new Core.Configuration.PanelConfig 
                {
                    CategoryName = category,
                    X = targetPanel.X,
                    Y = targetPanel.Y,
                    Width = targetPanel.Width,
                    ModeOwner = Core.Configuration.FolderMode.MirrorDesktop
                });
                _configManager.SaveConfig();
            }
        }

        public void AddVirtualPanel(string categoryName)
        {
            FluidDecks.Core.Logging.Logger.Log($"AddVirtualPanel called for category: {categoryName}", "INFO");
            try
            {
                var targetPanel = Panels.FirstOrDefault(p => p.CategoryName.Equals(categoryName, StringComparison.OrdinalIgnoreCase));
                if (targetPanel != null)
                {
                    MessageBox.Show("A virtual folder with this name already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                lock (_placementLock)
                {
                    if (Panels.Count == 0 && _lastPlacedX == 50 && _lastPlacedY == 50) { }
                    else
                    {
                        _lastPlacedX += 270;
                        if (_lastPlacedX + 250 > SystemParameters.VirtualScreenWidth - 100)
                        {
                            _lastPlacedX = 50;
                            _lastPlacedY += 350;
                        }
                    }

                    targetPanel = new PanelViewModel { 
                        CategoryName = categoryName, 
                        X = _lastPlacedX, 
                        Y = _lastPlacedY, 
                        Width = 250, 
                        IsTemporaryPopup = false,
                        IconScale = _configManager.CurrentConfig.IconScale
                    };
                    
                    targetPanel.LoadFolderIcon(); // ADDED FOLDER ICON LOADING

                    _configManager.CurrentConfig.Panels.Add(new Core.Configuration.PanelConfig { 
                        CategoryName = categoryName, 
                        X = _lastPlacedX, 
                        Y = _lastPlacedY, 
                        Width = 250,
                        ModeOwner = Core.Configuration.FolderMode.VirtualDecks,
                        VirtualItems = new System.Collections.Generic.List<string>(),
                        VirtualFolders = new System.Collections.Generic.List<string>()
                    });
                    
                    Application.Current.Dispatcher.Invoke(() => Panels.Add(targetPanel));
                    _configManager.SaveConfig();
                    
                    FluidDecks.Core.Logging.Logger.Log($"Successfully added Virtual Panel: {categoryName}", "INFO");
                }
            }
            catch (Exception ex)
            {
                FluidDecks.Core.Logging.Logger.Log($"Error in AddVirtualPanel for {categoryName}", "ERROR", ex);
                throw;
            }
        }

        private void WatcherService_OnFileRemoved(string filePath)
        {
            // Check if a top-level panel folder was removed
            var panelToRemove = Panels.FirstOrDefault(p => p.CategoryName.Equals(System.IO.Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase));
            if (panelToRemove != null && _configManager.CurrentConfig.FolderLayoutMode == FolderMode.MirrorDesktop)
            {
                panelToRemove.ReleaseFolderIcon();
                foreach (var item in panelToRemove.Items) item.ReleaseIcon();
                Panels.Remove(panelToRemove);
                
                var cfg = _configManager.CurrentConfig.Panels.FirstOrDefault(c => c.CategoryName == panelToRemove.CategoryName);
                if (cfg != null)
                {
                    _configManager.CurrentConfig.Panels.Remove(cfg);
                    _configManager.SaveConfig();
                }
                return;
            }

            foreach (var panel in Panels)
            {
                var item = panel.Items.FirstOrDefault(i => i.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    item.ReleaseIcon(); // Clean native handles before removing
                    panel.Items.Remove(item);
                    break;
                }
            }
        }

        public void SavePanelPosition(string categoryName, double x, double y)
        {
            var configPanel = _configManager.CurrentConfig.Panels.FirstOrDefault(p => p.CategoryName == categoryName);
            if (configPanel != null)
            {
                configPanel.X = x;
                configPanel.Y = y;
                _configManager.SaveConfig();
            }
        }

        public void SyncPositionsWithDesktop()
        {
            if (_configManager.CurrentConfig.FolderLayoutMode != FolderMode.MirrorDesktop)
                return;

            var positions = DesktopIconPositioner.GetDesktopIconPositions();
            if (positions.Count == 0) return;

            foreach (var panel in Panels)
            {
                // The category name in MirrorDesktop is the folder name.
                // We check if we have a position for this folder.
                if (positions.TryGetValue(panel.CategoryName, out var pt))
                {
                    panel.X = pt.x;
                    panel.Y = pt.y;
                    
                    var configPanel = _configManager.CurrentConfig.Panels.FirstOrDefault(p => p.CategoryName == panel.CategoryName);
                    if (configPanel != null)
                    {
                        configPanel.X = pt.x;
                        configPanel.Y = pt.y;
                    }
                }
            }
            _configManager.SaveConfig();
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    public class PanelViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public bool IsTemporaryPopup { get; set; }
        
        public double IconScale { get; set; } = 1.0;

        public PanelViewModel()
        {
            Items.CollectionChanged += (s, e) => {
                OnPropertyChanged(nameof(ComputedColumnCount));
                OnPropertyChanged(nameof(ItemWidth));
            };
        }

        private string _categoryName;
        public string CategoryName
        {
            get => _categoryName;
            set { _categoryName = value; OnPropertyChanged(nameof(CategoryName)); }
        }

        private double _x;
        public double X
        {
            get => _x;
            set { _x = value; OnPropertyChanged(nameof(X)); }
        }

        private double _y;
        public double Y
        {
            get => _y;
            set { _y = value; OnPropertyChanged(nameof(Y)); }
        }

        private double _width;
        public double Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(nameof(Width)); }
        }

        public ObservableCollection<string> VirtualFolders { get; set; } = new ObservableCollection<string>();

        // Removed ViewMode

        public int ComputedColumnCount => Math.Max(2, (int)Math.Ceiling(Math.Sqrt(Math.Max(1, Items.Count))));

        // Calculate item width for grid view. Remove 60px lower bound to allow square approach for many items
        public double ItemWidth => ((Width - 20) / ComputedColumnCount);
        
        public double MaxPanelHeight => System.Windows.SystemParameters.PrimaryScreenHeight * 0.7;

        private System.Windows.Media.ImageSource _folderIcon;
        public System.Windows.Media.ImageSource FolderIcon
        {
            get => _folderIcon;
            set { _folderIcon = value; OnPropertyChanged(nameof(FolderIcon)); }
        }

        private IntPtr _hIcon = IntPtr.Zero;

        public void LoadFolderIcon()
        {
            if (_folderIcon == null && !string.IsNullOrEmpty(CategoryName))
            {
                string folderPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), CategoryName);
                if (System.IO.Directory.Exists(folderPath))
                {
                    FolderIcon = Services.IconExtractor.GetIcon(folderPath, out _hIcon);
                }
            }
        }
        
        public void ReleaseFolderIcon()
        {
            FolderIcon = null;
            if (_hIcon != IntPtr.Zero)
            {
                Services.IconExtractor.ReleaseIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
        }


        public ObservableCollection<FileItemViewModel> Items { get; set; } = new ObservableCollection<FileItemViewModel>();

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
