using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FluidDecks.Services
{
    public class DesktopWatcherService : IDisposable
    {
        private readonly string _desktopPath;
        private readonly FileSystemWatcher _watcher;
        private readonly ConcurrentQueue<FileSystemEventArgs> _eventQueue;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _processorTask;

        public int MaxDepth { get; set; } = 1; // Default depth 1 (only Desktop root)
        public Core.Configuration.FolderMode FolderMode { get; set; } = Core.Configuration.FolderMode.MirrorDesktop;

        // Maps file extensions to Panel categories
        public Dictionary<string, string> ExtensionToCategoryMap { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".lnk", "Shortcuts" },
            { ".url", "Shortcuts" },
            { ".exe", "Programs" },
            { ".docx", "Documents" },
            { ".pdf", "Documents" },
            { ".txt", "Documents" },
            { ".png", "Images" },
            { ".jpg", "Images" },
            { ".jpeg", "Images" },
            { ".zip", "Archives" },
            { ".rar", "Archives" }
        };

        // Actions to inform the UI
        public Action<string, string> OnFileAdded; // FilePath, Category
        public Action<List<(string FilePath, string Category)>> OnFilesBatchAdded;
        public Action<string> OnFileRemoved; // FilePath
        public Action<string> OnCategoryAdded; // CategoryName (Folder Name)

        public DesktopWatcherService()
        {
            _desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            _eventQueue = new ConcurrentQueue<FileSystemEventArgs>();
            _cancellationTokenSource = new CancellationTokenSource();

            _watcher = new FileSystemWatcher(_desktopPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true // We need this to detect items moved INTO folders
            };

            _watcher.Created += Watcher_Event;
            _watcher.Deleted += Watcher_Event;
            _watcher.Renamed += Watcher_Event;

            // Start the background processor for the event queue
            _processorTask = Task.Run(() => ProcessQueueAsync(_cancellationTokenSource.Token));
        }

        public void Stop()
        {
            _watcher.EnableRaisingEvents = false;
        }

        public void Start()
        {
            if (FolderMode == Core.Configuration.FolderMode.VirtualDecks) return; // Do not scan or watch in Virtual mode

            // Initial asynchronous scan
            Task.Run(() => InitialScanAsync(_desktopPath, 0, _cancellationTokenSource.Token));

            // Start watching
            _watcher.EnableRaisingEvents = true;
        }

        private void Watcher_Event(object sender, FileSystemEventArgs e)
        {
            // Enqueue the event quickly to avoid dropping rapid events
            _eventQueue.Enqueue(e);
        }

        private async Task ProcessQueueAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (_eventQueue.TryDequeue(out var e))
                {
                    try
                    {
                        // Filter deep events out
                        string relative = e.FullPath.Substring(_desktopPath.Length).TrimStart(Path.DirectorySeparatorChar);
                        string[] parts = relative.Split(Path.DirectorySeparatorChar);
                        
                        if (FolderMode == Core.Configuration.FolderMode.MirrorDesktop)
                        {
                            if (parts.Length > 2) continue; // Ignore deep folder changes
                        }
                        else
                        {
                            if (parts.Length > 1) continue; // Category mode only cares about desktop root
                        }

                        if (e.ChangeType == WatcherChangeTypes.Created || e.ChangeType == WatcherChangeTypes.Changed)
                        {
                            string category = GetCategoryForFile(e.FullPath);
                            _ = Application.Current.Dispatcher.InvokeAsync(() => OnFileAdded?.Invoke(e.FullPath, category));
                        }
                        else if (e.ChangeType == WatcherChangeTypes.Deleted)
                        {
                            _ = Application.Current.Dispatcher.InvokeAsync(() => OnFileRemoved?.Invoke(e.FullPath));
                        }
                        else if (e.ChangeType == WatcherChangeTypes.Renamed && e is RenamedEventArgs re)
                        {
                            _ = Application.Current.Dispatcher.InvokeAsync(() => OnFileRemoved?.Invoke(re.OldFullPath));
                            string category = GetCategoryForFile(re.FullPath);
                            _ = Application.Current.Dispatcher.InvokeAsync(() => OnFileAdded?.Invoke(re.FullPath, category));
                        }
                        else if (e.ChangeType == WatcherChangeTypes.Created && Directory.Exists(e.FullPath) && FolderMode == Core.Configuration.FolderMode.MirrorDesktop)
                        {
                            // A new folder was created on the desktop directly
                            string relFolder = e.FullPath.Substring(_desktopPath.Length).TrimStart(Path.DirectorySeparatorChar);
                            string[] relParts = relFolder.Split(Path.DirectorySeparatorChar);
                            if (relParts.Length == 1)
                            {
                                _ = Application.Current.Dispatcher.InvokeAsync(() => OnCategoryAdded?.Invoke(relParts[0]));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error processing file event: {ex.Message}");
                    }
                }
                else
                {
                    await Task.Delay(10, token); // Brief pause when queue is empty to save CPU
                }
            }
        }

        private async Task InitialScanAsync(string directory, int currentDepth, CancellationToken token)
        {
            await ScanDirectoryRecursiveAsync(directory, currentDepth, token);
        }

        private async Task ScanDirectoryRecursiveAsync(string directory, int currentDepth, CancellationToken token)
        {
            if (currentDepth > MaxDepth || token.IsCancellationRequested) return;

            try
            {
                var files = Directory.GetFiles(directory);
                var batch = new List<(string FilePath, string Category)>();

                foreach (var file in files)
                {
                    if (token.IsCancellationRequested) break;
                    
                    if (FolderMode == Core.Configuration.FolderMode.MirrorDesktop && directory.Equals(_desktopPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string category = GetCategoryForFile(file, directory);
                    batch.Add((file, category));

                    if (batch.Count >= 50)
                    {
                        var localBatch = new List<(string FilePath, string Category)>(batch);
                        _ = Application.Current.Dispatcher.InvokeAsync(() => OnFilesBatchAdded?.Invoke(localBatch));
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                {
                    var localBatch = new List<(string FilePath, string Category)>(batch);
                    _ = Application.Current.Dispatcher.InvokeAsync(() => OnFilesBatchAdded?.Invoke(localBatch));
                }

                if (currentDepth < MaxDepth || FolderMode == Core.Configuration.FolderMode.MirrorDesktop)
                {
                    var dirs = Directory.GetDirectories(directory);
                    var dirBatch = new List<(string FilePath, string Category)>();

                    foreach (var dir in dirs)
                    {
                        if (FolderMode == Core.Configuration.FolderMode.CategoryBased)
                        {
                            dirBatch.Add((dir, "Folders"));
                        }
                        else if (FolderMode == Core.Configuration.FolderMode.MirrorDesktop && currentDepth == 0)
                        {
                            string folderName = Path.GetFileName(dir);
                            _ = Application.Current.Dispatcher.InvokeAsync(() => OnCategoryAdded?.Invoke(folderName));
                        }
                        else if (FolderMode == Core.Configuration.FolderMode.MirrorDesktop && currentDepth > 0)
                        {
                            string category = GetCategoryForFile(dir, directory);
                            dirBatch.Add((dir, category));
                        }
                        
                        await ScanDirectoryRecursiveAsync(dir, currentDepth + 1, token);
                    }

                    if (dirBatch.Count > 0)
                    {
                        _ = Application.Current.Dispatcher.InvokeAsync(() => OnFilesBatchAdded?.Invoke(dirBatch));
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning directory {directory}: {ex.Message}");
            }
        }

        private string GetCategoryForFile(string filePath, string parentDirectory = null)
        {
            if (FolderMode == Core.Configuration.FolderMode.MirrorDesktop)
            {
                string relative = filePath.Substring(_desktopPath.Length).TrimStart(Path.DirectorySeparatorChar);
                string[] parts = relative.Split(Path.DirectorySeparatorChar);
                if (parts.Length == 1) return "Desktop";
                return parts[0]; // Top level folder is the category
            }

            if (Directory.Exists(filePath))
                return "Folders"; // If it's a directory, put in Folders panel

            string extension = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(extension)) return "Others";

            if (ExtensionToCategoryMap.TryGetValue(extension, out string category))
            {
                return category;
            }

            return "Others";
        }

        public void Dispose()
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _cancellationTokenSource.Cancel();
            _processorTask?.Wait(500); // Wait briefly for graceful shutdown
            _cancellationTokenSource.Dispose();
        }
    }
}
