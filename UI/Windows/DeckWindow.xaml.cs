using System;
using System.Windows;
using System.Windows.Interop;
using FluidDecks.Core.Win32;
using FluidDecks.Core.Configuration;

namespace FluidDecks.UI.Windows
{
    public partial class DeckWindow : Window
    {
        // Tracks whether this window is embedded as a child of the desktop WorkerW
        public bool IsEmbedded { get; set; } = true;

        private struct PosRecord { public System.Windows.Point P; public DateTime T; }
        private System.Collections.Generic.List<PosRecord> _posHistory = new System.Collections.Generic.List<PosRecord>();
        private double _velocityX = 0;
        private double _velocityY = 0;
        private bool _isDragging = false;
        private System.Windows.Threading.DispatcherTimer _physicsTimer;

        public DeckWindow()
        {
            InitializeComponent();
            _physicsTimer = new System.Windows.Threading.DispatcherTimer();
            _physicsTimer.Interval = TimeSpan.FromMilliseconds(16);
            _physicsTimer.Tick += PhysicsTimer_Tick;
        }

        private void PhysicsTimer_Tick(object sender, EventArgs e)
        {
            if (Math.Abs(_velocityX) < 0.5 && Math.Abs(_velocityY) < 0.5)
            {
                _physicsTimer.Stop();
                return;
            }

            double newLeft = this.Left + _velocityX;
            double newTop = this.Top + _velocityY;

            // Apply friction
            _velocityX *= 0.90;
            _velocityY *= 0.90;

            // Bounds bouncing
            var workArea = SystemParameters.WorkArea;
            if (newLeft < workArea.Left)
            {
                newLeft = workArea.Left;
                _velocityX *= -0.6; // Bounce and dampen
            }
            else if (newLeft + this.ActualWidth > workArea.Right)
            {
                newLeft = workArea.Right - this.ActualWidth;
                _velocityX *= -0.6;
            }

            if (newTop < workArea.Top)
            {
                newTop = workArea.Top;
                _velocityY *= -0.6;
            }
            else if (newTop + this.ActualHeight > workArea.Bottom)
            {
                newTop = workArea.Bottom - this.ActualHeight;
                _velocityY *= -0.6;
            }

            this.Left = newLeft;
            this.Top = newTop;
        }

        private void ApplyAcrylicBlur()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Read blur tint settings from config
            var mainVM = Application.Current?.MainWindow?.DataContext as UI.ViewModels.MainViewModel;
            var config = mainVM?.AppConfigManager?.CurrentConfig;
            double tintOpacity = config?.BlurTintOpacity ?? 0.12;
            string hexColor = config?.BlurTintColor ?? "#000000";
            var blurMode = config?.BackgroundBlurMode ?? BlurMode.Standard;
            double bgOpacity = config?.BackgroundOpacity ?? 0.5;

            // WIN11 BUGFIX: Standard Blur (Mica or WCA BlurBehind) renders as solid black on frameless overlays.
            // Force Standard Blur to Acrylic to prevent the black screen.
            if (blurMode == BlurMode.Standard) 
            {
                blurMode = BlurMode.Acrylic;
            }

            // Parse hex color
            byte r = 0, g = 0, b = 0;
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
                r = color.R; g = color.G; b = color.B;
            }
            catch { }

            int backdropType = (blurMode == BlurMode.Acrylic) ? User32.DWMSBT_TRANSIENTWINDOW : User32.DWMSBT_NONE;
            
            if (blurMode != BlurMode.None && blurMode != BlurMode.Transparent)
            {
                // Disable WCA first to prevent DWM crashes
                var disableAccent = new AccentPolicy { AccentState = AccentState.ACCENT_DISABLED, AccentFlags = 0, GradientColor = 0 };
                int disableAccentSize = System.Runtime.InteropServices.Marshal.SizeOf(disableAccent);
                IntPtr disableAccentPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(disableAccentSize);
                System.Runtime.InteropServices.Marshal.StructureToPtr(disableAccent, disableAccentPtr, false);
                var disableData = new WindowCompositionAttributeData { Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY, SizeOfData = disableAccentSize, Data = disableAccentPtr };
                User32.SetWindowCompositionAttribute(hwnd, ref disableData);
                System.Runtime.InteropServices.Marshal.FreeHGlobal(disableAccentPtr);

                // Apply DWM Corner Preference from config
                int dwmCorner = User32.DWMWCP_ROUND;
                int pref = config?.BlurCornerPreference ?? 2;
                if (pref == 0) dwmCorner = User32.DWMWCP_DONOTROUND;
                else if (pref == 1) dwmCorner = User32.DWMWCP_ROUNDSMALL;
                User32.DwmSetWindowAttribute(hwnd, User32.DWMWA_WINDOW_CORNER_PREFERENCE, ref dwmCorner, sizeof(int));

                // Set modern DWM Backdrop
                User32.DwmSetWindowAttribute(hwnd, User32.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, System.Runtime.InteropServices.Marshal.SizeOf(typeof(int)));

                // Force Dark Mode so Mica base is dark and doesn't get stuck on light mode
                int darkMode = 1;
                User32.DwmSetWindowAttribute(hwnd, User32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, System.Runtime.InteropServices.Marshal.SizeOf(typeof(int)));
            }
            else
            {
                // Disable Backdrop first to prevent DWM crashes
                int clearBackdropType = User32.DWMSBT_NONE;
                User32.DwmSetWindowAttribute(hwnd, User32.DWMWA_SYSTEMBACKDROP_TYPE, ref clearBackdropType, System.Runtime.InteropServices.Marshal.SizeOf(typeof(int)));

                // Fallback to WCA transparent gradient if we just want a transparent window
                var accentState = blurMode == BlurMode.Transparent ? AccentState.ACCENT_ENABLE_TRANSPARENTGRADIENT : AccentState.ACCENT_DISABLED;
                var accent = new AccentPolicy { AccentState = accentState, AccentFlags = 0, GradientColor = 0 };
                int accentStructSize = System.Runtime.InteropServices.Marshal.SizeOf(accent);
                IntPtr accentPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(accentStructSize);
                System.Runtime.InteropServices.Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData { Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY, SizeOfData = accentStructSize, Data = accentPtr };
                User32.SetWindowCompositionAttribute(hwnd, ref data);
                System.Runtime.InteropServices.Marshal.FreeHGlobal(accentPtr);
            }

            if (MainGrid != null)
            {
                // Correctly composite the Black dimming (bgOpacity) and the Tint color (tintOpacity)
                // This ensures the two sliders do distinct things (Dimming vs Color intensity).
                double aBlack = blurMode == BlurMode.None ? 0 : bgOpacity;
                double aTint = blurMode == BlurMode.None ? 0 : tintOpacity;

                double aFinal = aTint + aBlack * (1.0 - aTint);
                byte finalAlpha = (byte)(aFinal * 255);

                byte finalR = 0, finalG = 0, finalB = 0;
                if (aFinal > 0)
                {
                    finalR = (byte)((r * aTint) / aFinal);
                    finalG = (byte)((g * aTint) / aFinal);
                    finalB = (byte)((b * aTint) / aFinal);
                }

                MainGrid.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(finalAlpha, finalR, finalG, finalB));
            }
        }

        private void RemoveAcrylicBlur()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Clear WCA first
            var accent = new AccentPolicy 
            { 
                AccentState = AccentState.ACCENT_DISABLED,
                AccentFlags = 0,
                GradientColor = 0x00000000 
            };
            int accentStructSize = System.Runtime.InteropServices.Marshal.SizeOf(accent);
            IntPtr accentPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(accentStructSize);
            System.Runtime.InteropServices.Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = accentStructSize,
                Data = accentPtr
            };

            User32.SetWindowCompositionAttribute(hwnd, ref data);
            System.Runtime.InteropServices.Marshal.FreeHGlobal(accentPtr);

            // Then clear Backdrop
            int backdropType = User32.DWMSBT_NONE;
            User32.DwmSetWindowAttribute(hwnd, User32.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, System.Runtime.InteropServices.Marshal.SizeOf(typeof(int)));

            if (MainGrid != null)
            {
                MainGrid.Background = System.Windows.Media.Brushes.Transparent;
            }

            // Force the window to repaint itself cleanly
            User32.RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, 
                User32.RedrawWindowFlags.Invalidate | User32.RedrawWindowFlags.UpdateNow | User32.RedrawWindowFlags.AllChildren);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            
            // Allow drag and drop from Explorer even if app is elevated
            User32.ChangeWindowMessageFilterEx(hwnd, User32.WM_DROPFILES, User32.MSGFLT_ALLOW, IntPtr.Zero);
            User32.ChangeWindowMessageFilterEx(hwnd, User32.WM_COPYDATA, User32.MSGFLT_ALLOW, IntPtr.Zero);
            User32.ChangeWindowMessageFilterEx(hwnd, User32.WM_COPYGLOBALDATA, User32.MSGFLT_ALLOW, IntPtr.Zero);
            
            // Prevent Alt+Tab and Win+D minimize by setting WS_EX_TOOLWINDOW
            int GWL_EXSTYLE = -20;
            long WS_EX_TOOLWINDOW = 0x00000080L;
            IntPtr exStyle = IntPtr.Size == 8 ? User32.GetWindowLongPtr(hwnd, GWL_EXSTYLE) : User32.GetWindowLong(hwnd, GWL_EXSTYLE);
            User32.SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_TOOLWINDOW));
            
            // Add WndProc hook
            HwndSource source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);

            // Apply DWM rounded corners
            int preference = User32.DWMWCP_ROUND;
            User32.DwmSetWindowAttribute(hwnd, User32.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

            EnableBlur();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            // Prevent Windows from minimizing the widget (Win+D)
            if (this.WindowState == WindowState.Minimized)
            {
                this.WindowState = WindowState.Normal;
            }
        }

        /// <summary>
        /// Enables blur behind the window if the blur setting is on.
        /// Called when the popup expands to give the frosted glass effect.
        /// </summary>
        public void EnableBlur()
        {
            // Check config — blur is optional
            var mainVM = Application.Current?.MainWindow?.DataContext as UI.ViewModels.MainViewModel;
            bool blurEnabled = mainVM?.AppConfigManager?.CurrentConfig?.EnableBlurEffect ?? true;
            var blurMode = mainVM?.AppConfigManager?.CurrentConfig?.BackgroundBlurMode ?? BlurMode.Standard;
            
            if (blurEnabled && blurMode != BlurMode.None)
            {
                ApplyAcrylicBlur();
            }
        }

        /// <summary>
        /// Disables blur behind the window.
        /// </summary>
        public void DisableBlur()
        {
            // Blur is now always on, we don't disable it on collapse
        }

        public void UpdateBlurIfExpanded()
        {
            EnableBlur();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == User32.WM_ENTERSIZEMOVE)
            {
                _isDragging = true;
                _physicsTimer.Stop();
                _velocityX = 0;
                _velocityY = 0;
                _posHistory.Clear();
                _posHistory.Add(new PosRecord { P = new System.Windows.Point(this.Left, this.Top), T = DateTime.Now });
            }
            else if (msg == User32.WM_EXITSIZEMOVE)
            {
                _isDragging = false;
                
                var now = DateTime.Now;
                var validRecords = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(_posHistory, r => (now - r.T).TotalMilliseconds < 150));
                
                if (validRecords.Count > 1)
                {
                    var first = validRecords[0];
                    var last = validRecords[validRecords.Count - 1];
                    var dt = (last.T - first.T).TotalSeconds;
                    if (dt > 0.01)
                    {
                        double physicalVx = (last.P.X - first.P.X) / dt;
                        double physicalVy = (last.P.Y - first.P.Y) / dt;
                        
                        double dpiX = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
                        _velocityX = (physicalVx / 60.0) / (dpiX > 0 ? dpiX : 1.0);
                        _velocityY = (physicalVy / 60.0) / (dpiX > 0 ? dpiX : 1.0);
                    }
                }
                else
                {
                    _velocityX = 0;
                    _velocityY = 0;
                }
                _posHistory.Clear();

                var mainVM = Application.Current?.MainWindow?.DataContext as UI.ViewModels.MainViewModel;
                if (mainVM?.AppConfigManager?.CurrentConfig?.EnablePhysics == true)
                {
                    if (Math.Abs(_velocityX) > 2 || Math.Abs(_velocityY) > 2)
                    {
                        _physicsTimer.Start();
                    }
                }
            }
            else if (msg == User32.WM_WINDOWPOSCHANGING)
            {
                var wndPos = (User32.WINDOWPOS)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(User32.WINDOWPOS));
                
                var panelConfig = this.DataContext as PanelConfig;
                if (panelConfig != null && panelConfig.IsPositionLocked)
                {
                    wndPos.flags |= 0x0002; // SWP_NOMOVE
                }

                // Track position history for velocity
                if (_isDragging)
                {
                    _posHistory.Add(new PosRecord { P = new System.Windows.Point(wndPos.x, wndPos.y), T = DateTime.Now });
                    if (_posHistory.Count > 20) _posHistory.RemoveAt(0);
                }

                // Screen bounds clamping with DPI awareness
                var workArea = SystemParameters.WorkArea;
                double dpiX = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
                double dpiY = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleY;
                
                int physWorkLeft = (int)(workArea.Left * dpiX);
                int physWorkRight = (int)(workArea.Right * dpiX);
                int physWorkTop = (int)(workArea.Top * dpiY);
                int physWorkBottom = (int)(workArea.Bottom * dpiY);

                if ((wndPos.flags & 0x0002) == 0) // SWP_NOMOVE is not set, meaning window is moving
                {
                    int width = wndPos.cx > 0 ? wndPos.cx : (int)(this.ActualWidth * dpiX);
                    int height = wndPos.cy > 0 ? wndPos.cy : (int)(this.ActualHeight * dpiY);

                    if (wndPos.x < physWorkLeft) wndPos.x = physWorkLeft;
                    if (wndPos.x + width > physWorkRight) wndPos.x = physWorkRight - width;
                    if (wndPos.y < physWorkTop) wndPos.y = physWorkTop;
                    if (wndPos.y + height > physWorkBottom) wndPos.y = physWorkBottom - height;
                }

                // Only force HWND_BOTTOM when NOT embedded in desktop WorkerW.
                // When embedded, the parent (WorkerW) already manages z-order.
                if (!IsEmbedded)
                {
                    wndPos.hwndInsertAfter = new IntPtr(1); // HWND_BOTTOM
                }
                
                // Prevent Win+D from hiding us (SWP_HIDEWINDOW)
                wndPos.flags &= ~0x0080u; 

                System.Runtime.InteropServices.Marshal.StructureToPtr(wndPos, lParam, false);
            }
            else if (msg == 0x0112) // WM_SYSCOMMAND
            {
                if (((int)wParam & 0xFFF0) == 0xF020) // SC_MINIMIZE
                {
                    handled = true; // Block minimize
                    return IntPtr.Zero;
                }
            }
            return IntPtr.Zero;
        }
    }
}
