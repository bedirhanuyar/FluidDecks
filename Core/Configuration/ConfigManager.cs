using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Threading;

namespace FluidDecks.Core.Configuration
{
    /// <summary>
    /// Manages the application's configuration state, providing atomic file operations,
    /// automatic backups, and import/export capabilities to ensure data integrity.
    /// </summary>
    public class ConfigManager : IDisposable
    {
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fluid_config.json");
        private static readonly string TempConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fluid_config.tmp.json");
        private static readonly string BackupConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fluid_config.bak.json");
        
        private readonly FileSystemWatcher _watcher;
        private AppConfig _currentConfig;
        
        public AppConfig CurrentConfig => _currentConfig;
        
        /// <summary>
        /// Fires when the configuration has been updated either programmatically or externally via the file system.
        /// </summary>
        public event Action<AppConfig> OnConfigChanged;

        public ConfigManager()
        {
            LoadConfig();

            _watcher = new FileSystemWatcher(AppDomain.CurrentDomain.BaseDirectory, "fluid_config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            
            _watcher.Changed += Watcher_Changed;
        }

        /// <summary>
        /// Loads the configuration from the disk. Falls back to a backup if the primary file is corrupted.
        /// </summary>
        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Config file is empty.");
                    
                    _currentConfig = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                    
                    // Create a backup upon successful load
                    File.Copy(ConfigPath, BackupConfigPath, overwrite: true);
                }
                else if (File.Exists(BackupConfigPath))
                {
                    // Recover from backup
                    string json = File.ReadAllText(BackupConfigPath);
                    _currentConfig = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                    SaveConfig(); // Restore the main file
                }
                else
                {
                    _currentConfig = new AppConfig();
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading config: {ex.Message}. Attempting recovery.");
                
                // Attempt to recover from backup on deserialization error
                if (File.Exists(BackupConfigPath))
                {
                    try
                    {
                        string backupJson = File.ReadAllText(BackupConfigPath);
                        _currentConfig = JsonSerializer.Deserialize<AppConfig>(backupJson) ?? new AppConfig();
                        SaveConfig(); // Overwrite corrupted main config with backup
                        return;
                    }
                    catch { /* Backup is also corrupt */ }
                }

                MessageBox.Show($"Error loading config: {ex.Message}\nStarting with default settings.", "FluidDecks Config Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                _currentConfig = new AppConfig();
            }
        }

        /// <summary>
        /// Saves the configuration atomically to prevent corruption during sudden application termination.
        /// </summary>
        public void SaveConfig()
        {
            try
            {
                if (_watcher != null) _watcher.EnableRaisingEvents = false;
                
                string json = JsonSerializer.Serialize(_currentConfig, new JsonSerializerOptions { WriteIndented = true });
                
                // Atomic Write: Write to temp file first, then move (replace)
                File.WriteAllText(TempConfigPath, json);
                File.Move(TempConfigPath, ConfigPath, overwrite: true);

                if (_watcher != null) _watcher.EnableRaisingEvents = true;

                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    OnConfigChanged?.Invoke(_currentConfig);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving config: {ex.Message}");
                if (_watcher != null) _watcher.EnableRaisingEvents = true;
                MessageBox.Show($"Error saving config: {ex.Message}", "FluidDecks Config Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Exports the current configuration state to a specified file path.
        /// </summary>
        /// <param name="exportPath">The destination file path.</param>
        public void ExportConfig(string exportPath)
        {
            try
            {
                string json = JsonSerializer.Serialize(_currentConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(exportPath, json);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to export configuration: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Imports configuration from a specified file path and reloads the application state.
        /// </summary>
        /// <param name="importPath">The source file path to import from.</param>
        public void ImportConfig(string importPath)
        {
            try
            {
                if (!File.Exists(importPath)) throw new FileNotFoundException("Import file not found.");
                
                string json = File.ReadAllText(importPath);
                var importedConfig = JsonSerializer.Deserialize<AppConfig>(json);
                
                if (importedConfig != null)
                {
                    _currentConfig = importedConfig;
                    SaveConfig(); // This will trigger UI updates
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to import configuration: {ex.Message}", ex);
            }
        }

        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            // Give the file system a moment to release the file lock after saving
            Thread.Sleep(100);

            var oldHardwareAccel = _currentConfig?.EnableHardwareAcceleration ?? true;

            LoadConfig();

            Application.Current.Dispatcher.Invoke(() =>
            {
                // If a critical setting changed that requires app restart (e.g. graphics context change)
                if (oldHardwareAccel != _currentConfig.EnableHardwareAcceleration)
                {
                    RestartApplication();
                }
                else
                {
                    // Live update UI elements
                    OnConfigChanged?.Invoke(_currentConfig);
                }
            });
        }

        private void RestartApplication()
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                System.Diagnostics.Process.Start(exePath);
                Application.Current.Shutdown();
            }
        }

        public void Dispose()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
        }
    }
}
