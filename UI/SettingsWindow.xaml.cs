using System;
using System.Windows;
using FluidDecks.Core.Configuration;

namespace FluidDecks.UI
{
    public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
    {
        private ConfigManager _configManager;
        private bool _isInitializing = true;

        public SettingsWindow(ConfigManager configManager)
        {
            InitializeComponent();
            _configManager = configManager;
            
            // Load current settings
            FolderModeComboBox.SelectedIndex = _configManager.CurrentConfig.FolderLayoutMode == FolderMode.MirrorDesktop ? 0 : (_configManager.CurrentConfig.FolderLayoutMode == FolderMode.CategoryBased ? 1 : 2);
            MaxDepthSlider.Value = _configManager.CurrentConfig.MaxDepth;
            IconScaleSlider.Value = _configManager.CurrentConfig.IconScale;
            PopupRatioSlider.Value = _configManager.CurrentConfig.PopupMaxScreenRatio;
            PauseCheckBox.IsChecked = _configManager.CurrentConfig.IsPaused;
            BlurCheckBox.IsChecked = _configManager.CurrentConfig.EnableBlurEffect;
            DevModeCheckBox.IsChecked = _configManager.CurrentConfig.DeveloperModeLogging;
            ExperimentalCheckBox.IsChecked = _configManager.CurrentConfig.EnableExperimentalFeatures;
            VirtualFolderAppCheckBox.IsChecked = _configManager.CurrentConfig.OpenVirtualFoldersInApp;
            
            // Animations & Physics
            PhysicsCheckBox.IsChecked = _configManager.CurrentConfig.EnablePhysics;
            EasingComboBox.SelectedIndex = _configManager.CurrentConfig.AnimationEasing;
            AnimSpeedSlider.Value = _configManager.CurrentConfig.AnimationSpeed;
            
            // Appearance
            CollapsedRadiusSlider.Value = _configManager.CurrentConfig.CollapsedCornerRadius;
            ExpandedRadiusSlider.Value = _configManager.CurrentConfig.ExpandedCornerRadius;
            BgOpacitySlider.Value = _configManager.CurrentConfig.BackgroundOpacity;
            BlurTintOpacitySlider.Value = _configManager.CurrentConfig.BlurTintOpacity;
            BlurModeComboBox.SelectedIndex = (int)_configManager.CurrentConfig.BackgroundBlurMode;
            DwmCornerComboBox.SelectedIndex = _configManager.CurrentConfig.BlurCornerPreference;
            
            // Set color radio button
            switch (_configManager.CurrentConfig.BlurTintColor?.ToLower())
            {
                case "#ffffff": ColorWhite.IsChecked = true; break;
                case "#1a3a6b": ColorBlue.IsChecked = true; break;
                case "#3a1a5c": ColorPurple.IsChecked = true; break;
                default: ColorBlack.IsChecked = true; break;
            }
            
            UpdateExperimentalUI();
            UpdateBlurSettingsEnabled();
            _isInitializing = false;
        }

        private void UpdateExperimentalUI()
        {
            bool exp = _configManager.CurrentConfig.EnableExperimentalFeatures;
            if (FolderModeComboBox.Items.Count >= 3)
            {
                if (FolderModeComboBox.Items[0] is System.Windows.Controls.ComboBoxItem i1) i1.IsEnabled = exp;
                if (FolderModeComboBox.Items[1] is System.Windows.Controls.ComboBoxItem i2) i2.IsEnabled = exp;
            }

            if (!exp && FolderModeComboBox.SelectedIndex != 2)
            {
                FolderModeComboBox.SelectedIndex = 2; // Force Virtual Decks
            }
            
            // Mode Isolation
            var mode = _configManager.CurrentConfig.FolderLayoutMode;
            MaxDepthSlider.IsEnabled = mode == FolderMode.MirrorDesktop;
            RefreshBtn.IsEnabled = mode == FolderMode.MirrorDesktop;
        }

        private void UpdateBlurSettingsEnabled()
        {
            bool blurOn = _configManager.CurrentConfig.EnableBlurEffect;
            BlurModeComboBox.IsEnabled = blurOn;
            BlurModeLabel.IsEnabled = blurOn;
            BgOpacitySlider.IsEnabled = blurOn;
            BgOpacityLabel.IsEnabled = blurOn;
            BlurTintOpacitySlider.IsEnabled = blurOn;
            BlurTintLabel.IsEnabled = blurOn;
            BlurColorLabel.IsEnabled = blurOn;
            ColorBlack.IsEnabled = blurOn;
            ColorWhite.IsEnabled = blurOn;
            ColorBlue.IsEnabled = blurOn;
            ColorPurple.IsEnabled = blurOn;

            // Handle Corner Radius visibility and disabling
            bool isDwmBlur = blurOn && (BlurModeComboBox.SelectedIndex == 1 || BlurModeComboBox.SelectedIndex == 2);
            CollapsedRadiusSlider.IsEnabled = !isDwmBlur;
            ExpandedRadiusSlider.IsEnabled = !isDwmBlur;
            CollapsedRadiusLabel.Opacity = isDwmBlur ? 0.5 : 1.0;
            ExpandedRadiusLabel.Opacity = isDwmBlur ? 0.5 : 1.0;
            DwmCornerPanel.Visibility = isDwmBlur ? Visibility.Visible : Visibility.Collapsed;
        }

        private void FolderModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_configManager == null) return;
            
            var newMode = FolderModeComboBox.SelectedIndex == 0 ? FolderMode.MirrorDesktop : (FolderModeComboBox.SelectedIndex == 1 ? FolderMode.CategoryBased : FolderMode.VirtualDecks);
            if (_configManager.CurrentConfig.FolderLayoutMode != newMode)
            {
                _configManager.CurrentConfig.FolderLayoutMode = newMode;
                UpdateExperimentalUI();
                _configManager.SaveConfig();
                
                if (Application.Current.MainWindow is MainWindow mainWindow && mainWindow.DataContext is ViewModels.MainViewModel mainVM)
                {
                    mainVM.RestartDesktopWatcher();
                }
            }
        }

        private void MaxDepthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _configManager == null) return;
            _configManager.CurrentConfig.MaxDepth = (int)e.NewValue;
            _configManager.SaveConfig();
            
            if (Application.Current.MainWindow is MainWindow mainWindow && mainWindow.DataContext is ViewModels.MainViewModel mainVM)
            {
                mainVM.RestartDesktopWatcher();
            }
        }

        private void IconScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _configManager == null) return;
            _configManager.CurrentConfig.IconScale = e.NewValue;
            _configManager.SaveConfig();
            
            if (Application.Current.MainWindow is MainWindow mainWindow && mainWindow.DataContext is ViewModels.MainViewModel mainVM)
            {
                mainVM.RestartDesktopWatcher();
            }
        }

        private void PopupRatioSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _configManager == null) return;
            _configManager.CurrentConfig.PopupMaxScreenRatio = e.NewValue;
            _configManager.SaveConfig();
            
            if (Application.Current.MainWindow is MainWindow mainWindow && mainWindow.DataContext is ViewModels.MainViewModel mainVM)
            {
                mainVM.RestartDesktopWatcher();
            }
        }

        private void CollapsedRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _configManager == null) return;
            _configManager.CurrentConfig.CollapsedCornerRadius = e.NewValue;
            _configManager.SaveConfig();
            LiveUpdateDecksBlur();
        }

        private void ExpandedRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _configManager == null) return;
            _configManager.CurrentConfig.ExpandedCornerRadius = e.NewValue;
            _configManager.SaveConfig();
            LiveUpdateDecksBlur();
        }

        private void PhysicsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_configManager == null || _isInitializing) return;
            _configManager.CurrentConfig.EnablePhysics = PhysicsCheckBox.IsChecked ?? false;
            _configManager.SaveConfig();
        }

        private void EasingComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_configManager == null || _isInitializing) return;
            _configManager.CurrentConfig.AnimationEasing = EasingComboBox.SelectedIndex;
            _configManager.SaveConfig();
        }

        private void AnimSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_configManager == null || _isInitializing) return;
            _configManager.CurrentConfig.AnimationSpeed = e.NewValue;
            _configManager.SaveConfig();
        }

        private void BlurTintOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _configManager == null) return;
            _configManager.CurrentConfig.BlurTintOpacity = e.NewValue;
            _configManager.SaveConfig();
            LiveUpdateDecksBlur();
        }

        private void BlurColor_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _configManager == null) return;
            
            string color = "#000000"; // Default dark
            if (ColorWhite.IsChecked == true) color = "#FFFFFF";
            else if (ColorBlue.IsChecked == true) color = "#1A3A6B";
            else if (ColorPurple.IsChecked == true) color = "#3A1A5C";
            
            _configManager.CurrentConfig.BlurTintColor = color;
            _configManager.SaveConfig();
            LiveUpdateDecksBlur();
        }

        private void PauseCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_configManager == null) return;
            
            bool isPaused = PauseCheckBox.IsChecked ?? false;
            if (_configManager.CurrentConfig.IsPaused != isPaused)
            {
                _configManager.CurrentConfig.IsPaused = isPaused;
                _configManager.SaveConfig();
                ApplyToMainWindow();
            }
        }

        private void BlurCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_configManager == null || _isInitializing) return;

            bool isBlurEnabled = BlurCheckBox.IsChecked ?? true;
            if (_configManager.CurrentConfig.EnableBlurEffect != isBlurEnabled)
            {
                _configManager.CurrentConfig.EnableBlurEffect = isBlurEnabled;
                _configManager.SaveConfig();
                UpdateBlurSettingsEnabled();
                LiveUpdateDecksBlur();
            }
        }

        private void BlurModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_configManager == null || _isInitializing) return;

            var newMode = (BlurMode)BlurModeComboBox.SelectedIndex;
            if (_configManager.CurrentConfig.BackgroundBlurMode != newMode)
            {
                _configManager.CurrentConfig.BackgroundBlurMode = newMode;
                _configManager.SaveConfig();
                UpdateBlurSettingsEnabled();
                LiveUpdateDecksBlur();
            }
        }

        private void DwmCornerComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_configManager == null || _isInitializing) return;

            int pref = DwmCornerComboBox.SelectedIndex;
            if (_configManager.CurrentConfig.BlurCornerPreference != pref)
            {
                _configManager.CurrentConfig.BlurCornerPreference = pref;
                _configManager.SaveConfig();
                LiveUpdateDecksBlur();
            }
        }

        private void BgOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_configManager == null || _isInitializing) return;

            _configManager.CurrentConfig.BackgroundOpacity = e.NewValue;
            _configManager.SaveConfig();
            LiveUpdateDecksBlur();
        }

        private void DevModeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_configManager == null || _isInitializing) return;
            
            bool isDevMode = DevModeCheckBox.IsChecked ?? false;
            if (_configManager.CurrentConfig.DeveloperModeLogging != isDevMode)
            {
                _configManager.CurrentConfig.DeveloperModeLogging = isDevMode;
                FluidDecks.Core.Logging.Logger.IsEnabled = isDevMode;
                _configManager.SaveConfig();
            }
        }

        private void ExperimentalCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_configManager == null || _isInitializing) return;
            
            bool isExp = ExperimentalCheckBox.IsChecked ?? false;
            if (isExp && !_configManager.CurrentConfig.EnableExperimentalFeatures)
            {
                var result = MessageBox.Show("Warning: Experimental features might not be fully stable and are currently in development. Are you sure you want to enable them?", "Experimental Features", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No)
                {
                    _isInitializing = true;
                    ExperimentalCheckBox.IsChecked = false;
                    _isInitializing = false;
                    return;
                }
            }

            if (_configManager.CurrentConfig.EnableExperimentalFeatures != isExp)
            {
                _configManager.CurrentConfig.EnableExperimentalFeatures = isExp;
                _configManager.SaveConfig();
                UpdateExperimentalUI();
            }
        }

        private void VirtualFolderAppCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_configManager == null || _isInitializing) return;
            
            bool inApp = VirtualFolderAppCheckBox.IsChecked ?? false;
            if (_configManager.CurrentConfig.OpenVirtualFoldersInApp != inApp)
            {
                _configManager.CurrentConfig.OpenVirtualFoldersInApp = inApp;
                _configManager.SaveConfig();
            }
        }

        private void ApplyToMainWindow()
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ForceApplyDesktopMode();
            }
        }

        private void LiveUpdateDecksBlur()
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is Windows.DeckWindow deck)
                {
                    deck.RefreshVisuals();
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void RefreshPositions_Click(object sender, RoutedEventArgs e)
        {
            if (_configManager.CurrentConfig.FolderLayoutMode != FolderMode.MirrorDesktop)
            {
                MessageBox.Show("Position syncing is only available in 'Mirror Desktop' mode.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (Application.Current.MainWindow is MainWindow mainWindow && mainWindow.DataContext is ViewModels.MainViewModel mainVM)
            {
                mainVM.SyncPositionsWithDesktop();
                MessageBox.Show("Widget positions synchronized with the classic Windows desktop!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ExportConfig_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export FluidDecks Configuration",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".json",
                FileName = "FluidDecksBackup"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _configManager.ExportConfig(dialog.FileName);
                    MessageBox.Show("Configuration exported successfully!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export configuration:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportConfig_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import FluidDecks Configuration",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _configManager.ImportConfig(dialog.FileName);
                    MessageBox.Show("Configuration imported successfully! The application will now apply the new settings.", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Force UI reload
                    ApplyToMainWindow();
                    this.Close(); // Close settings window so it can be re-opened with new bindings
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to import configuration:\n{ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Slider_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Slider slider)
            {
                // Skip if user clicked on the Thumb itself (let default drag behavior work)
                if (e.OriginalSource is System.Windows.FrameworkElement fe)
                {
                    var thumb = FindVisualParent<System.Windows.Controls.Primitives.Thumb>(fe);
                    if (thumb != null) return;
                }

                var point = e.GetPosition(slider);
                double ratio = point.X / slider.ActualWidth;
                if (ratio < 0) ratio = 0;
                if (ratio > 1) ratio = 1;
                double value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
                slider.Value = Math.Round(value / slider.TickFrequency) * slider.TickFrequency;
                e.Handled = true;
            }
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T found) return found;
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
