using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using FluidDecks.Core.Win32;

namespace FluidDecks
{
    public partial class MainWindow : Window
    {
        private IntPtr _workerW = IntPtr.Zero;
        
        private Core.Configuration.ConfigManager _configManager;
        private Services.DesktopWatcherService _watcherService;
        private UI.ViewModels.MainViewModel _mainViewModel;

        private System.Collections.Generic.Dictionary<UI.ViewModels.PanelViewModel, UI.Windows.DeckWindow> _deckWindows = new();

        public MainWindow()
        {
            InitializeComponent();
            
            _configManager = new Core.Configuration.ConfigManager();
            _watcherService = new Services.DesktopWatcherService();
            _mainViewModel = new UI.ViewModels.MainViewModel(_configManager, _watcherService);
            
            this.DataContext = _mainViewModel;

            _mainViewModel.Panels.CollectionChanged += Panels_CollectionChanged;

            foreach (var panel in _mainViewModel.Panels)
            {
                var win = new UI.Windows.DeckWindow { DataContext = panel };
                _deckWindows[panel] = win;
            }

            this.Loaded += MainWindow_Loaded;
        }

        private void Panels_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                var windowsToClose = _deckWindows.Values.ToList();
                _deckWindows.Clear();
                Application.Current.Dispatcher.InvokeAsync(() => {
                    foreach (var win in windowsToClose) { win.Close(); }
                    GC.Collect();
                });
                return;
            }

            if (e.NewItems != null)
            {
                foreach (UI.ViewModels.PanelViewModel p in e.NewItems)
                {
                    var win = new UI.Windows.DeckWindow { DataContext = p };
                    _deckWindows[p] = win;
                    if (!_configManager.CurrentConfig.IsPaused) 
                    {
                        win.Show();
                        EmbedInDesktop(win);
                    }
                }
            }
            if (e.OldItems != null)
            {
                foreach (UI.ViewModels.PanelViewModel p in e.OldItems)
                {
                    if (_deckWindows.TryGetValue(p, out var win))
                    {
                        _deckWindows.Remove(p);
                        Application.Current.Dispatcher.InvokeAsync(() => {
                            win.Close();
                            GC.Collect();
                        });
                    }
                }
            }
        }

        private UI.SettingsWindow _settingsWindow;

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try { SysTrayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName); } catch { }
            
            if (_configManager.CurrentConfig.IsFirstRun)
            {
                var welcome = new UI.Windows.WelcomeWindow();
                welcome.ShowDialog(); // Blocks until the user closes it
            }

            _configManager.OnConfigChanged += (config) => ForceApplyDesktopMode();
            ForceApplyDesktopMode();
            
            // Hook WndProc to strictly enforce HWND_BOTTOM
            WindowInteropHelper helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            HwndSource source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
        }

        private void EmbedInDesktop(Window win)
        {
            IntPtr progman = User32.FindWindow("Progman", null);
            if (progman == IntPtr.Zero)
            {
                Core.Logging.Logger.Log("EmbedInDesktop: Progman not found.", "WARN");
                return;
            }

            IntPtr childHwnd = new WindowInteropHelper(win).Handle;
            if (childHwnd != IntPtr.Zero)
            {
                // Instead of using SetParent to WorkerW (which causes Win+D inversion bugs with WPF transparent windows),
                // we set the window's Owner to the desktop (Progman). 
                // This makes it immune to Win+D minimization while allowing WndProc to force it to the bottom.
                new WindowInteropHelper(win).Owner = progman;

                if (win is UI.Windows.DeckWindow deckWin)
                {
                    // False means WndProc WILL force HWND_BOTTOM to keep it pinned behind other apps.
                    deckWin.IsEmbedded = false;
                }
            }
        }

        public void ForceApplyDesktopMode()
        {
            var config = _configManager.CurrentConfig;
            
            if (config.IsPaused)
            {
                this.Hide();
                foreach (var win in _deckWindows.Values)
                {
                    // Cleanly remove DWM blur effects before hiding to prevent visual artifacts
                    if (win is UI.Windows.DeckWindow dw)
                    {
                        dw.DisableBlur();
                        dw.IsEmbedded = false;
                    }
                    win.Hide();
                }
                return;
            }
            else
            {
                this.Hide(); // MainWindow itself is ALWAYS hidden now.
                foreach (var win in _deckWindows.Values) 
                {
                    win.Show();
                    EmbedInDesktop(win);
                    // Restore blur for any panels that were expanded before pausing
                    if (win is UI.Windows.DeckWindow dw)
                    {
                        dw.RefreshVisuals();
                    }
                }
            }

            WindowInteropHelper helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;

            int GWL_EXSTYLE = -20;
            long WS_EX_TOOLWINDOW = 0x00000080L;
            IntPtr exStyle = IntPtr.Size == 8 ? User32.GetWindowLongPtr(hwnd, GWL_EXSTYLE) : User32.GetWindowLong(hwnd, GWL_EXSTYLE);
            User32.SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_TOOLWINDOW));

            // MainWindow no longer becomes the desktop
            User32.SetParent(hwnd, IntPtr.Zero);
            
            ApplyWindows11Effects();
        }

        private void MenuItemSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow == null || !_settingsWindow.IsLoaded)
            {
                _settingsWindow = new UI.SettingsWindow(_configManager);
                _settingsWindow.Show();
            }
            else
            {
                _settingsWindow.Activate();
            }
        }



        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // No blur on MainWindow anymore
        }

        private void SettingsItem_Click(object sender, RoutedEventArgs e)
        {
            _configManager.CurrentConfig.IsPaused = !_configManager.CurrentConfig.IsPaused;
            _configManager.SaveConfig();
            ForceApplyDesktopMode();
        }

        private void MenuItemPause_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is UI.ViewModels.MainViewModel vm)
            {
                vm.AppConfigManager.CurrentConfig.IsPaused = !vm.AppConfigManager.CurrentConfig.IsPaused;
                vm.AppConfigManager.SaveConfig();
                ForceApplyDesktopMode();
            }
        }

        private void MenuItemAddVirtual_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is UI.ViewModels.MainViewModel vm)
            {
                if (vm.AppConfigManager.CurrentConfig.FolderLayoutMode != Core.Configuration.FolderMode.VirtualDecks)
                {
                    MessageBox.Show("To add a virtual folder, switch to 'Virtual Decks' mode in Settings.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Create a quick native WPF Input Dialog
                var inputWindow = new Window
                {
                    Title = "Add Virtual Folder",
                    Width = 300,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    Topmost = true
                };

                var grid = new System.Windows.Controls.Grid { Margin = new Thickness(10) };
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

                var textBox = new System.Windows.Controls.TextBox
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 10),
                    Text = "New Folder"
                };
                grid.Children.Add(textBox);
                System.Windows.Controls.Grid.SetRow(textBox, 0);

                var btnStack = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var okBtn = new System.Windows.Controls.Button { Content = "Add", Width = 75, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
                okBtn.Click += (s, ev) => { inputWindow.DialogResult = true; inputWindow.Close(); };
                
                var cancelBtn = new System.Windows.Controls.Button { Content = "Cancel", Width = 75, IsCancel = true };
                
                btnStack.Children.Add(okBtn);
                btnStack.Children.Add(cancelBtn);
                
                grid.Children.Add(btnStack);
                System.Windows.Controls.Grid.SetRow(btnStack, 1);

                inputWindow.Content = grid;
                inputWindow.Loaded += (s, ev) => { textBox.SelectAll(); textBox.Focus(); };

                if (inputWindow.ShowDialog() == true)
                {
                    string folderName = textBox.Text;
                    if (!string.IsNullOrWhiteSpace(folderName))
                    {
                        vm.AddVirtualPanel(folderName);
                    }
                }
            }
        }

        private void MenuItemExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == User32.WM_WINDOWPOSCHANGING)
            {
                // Force Z-Order to bottom
                var wndPos = (User32.WINDOWPOS)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(User32.WINDOWPOS));
                wndPos.hwndInsertAfter = new IntPtr(1); // HWND_BOTTOM
                
                // Prevent Win+D from hiding us in Hybrid mode
                wndPos.flags &= ~0x0080u; // SWP_HIDEWINDOW is 0x0080

                System.Runtime.InteropServices.Marshal.StructureToPtr(wndPos, lParam, false);
            }
            return IntPtr.Zero;
        }

        private void ApplyWindows11Effects()
        {
            WindowInteropHelper helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;

            int cornerPreference = DwmApi.DWMWCP_ROUND;
            DwmApi.DwmSetWindowAttribute(hwnd, DwmApi.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
            
            int backdropType = 1; // 1 = DWMSBT_DISABLE (None) for MainWindow
            DwmApi.DwmSetWindowAttribute(hwnd, DwmApi.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            SysTrayIcon?.Dispose();
            foreach(var win in _deckWindows.Values) win.Close();
            try { DetachFromDesktop(); } catch { }
            base.OnClosing(e);
        }

        private void DetachFromDesktop()
        {
            // Dispose services before unparenting
            _watcherService?.Dispose();
            _configManager?.Dispose();

            WindowInteropHelper helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;

            // 1. We no longer use SetParent, but we should remove our WndProc hook
            HwndSource source = HwndSource.FromHwnd(hwnd);
            source?.RemoveHook(WndProc);

            // Redraw desktop just in case
            IntPtr progman = User32.FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                User32.RedrawWindow(progman, IntPtr.Zero, IntPtr.Zero, User32.RedrawWindowFlags.Invalidate | User32.RedrawWindowFlags.UpdateNow | User32.RedrawWindowFlags.AllChildren);
            }

            // Alternatively, notify the shell to update associations, which often causes a full desktop refresh
            Shell32.SHChangeNotify(Shell32.SHCNE_ASSOCCHANGED, Shell32.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            
            // Also redraw desktop
            IntPtr desktopWindow = User32.FindWindow("Progman", null);
            if (desktopWindow != IntPtr.Zero)
            {
                User32.RedrawWindow(desktopWindow, IntPtr.Zero, IntPtr.Zero, User32.RedrawWindowFlags.Invalidate | User32.RedrawWindowFlags.UpdateNow | User32.RedrawWindowFlags.AllChildren);
            }
        }
    }
}