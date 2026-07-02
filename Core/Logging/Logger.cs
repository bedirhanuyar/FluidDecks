using System;
using System.IO;

namespace FluidDecks.Core.Logging
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _logFilePath;

        public static bool IsEnabled { get; set; } = true;

        static Logger()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string logDir = Path.Combine(appData, "FluidDecks", "Logs");
                Directory.CreateDirectory(logDir);
                _logFilePath = Path.Combine(logDir, "fluiddecks_log.txt");
            }
            catch
            {
                // Fallback to local directory if AppData is inaccessible
                _logFilePath = "fluiddecks_log.txt";
            }
        }

        public static void Log(string message, string level = "INFO", Exception ex = null)
        {
            if (!IsEnabled) return;

            lock (_lock)
            {
                try
                {
                    using (StreamWriter sw = new StreamWriter(_logFilePath, true))
                    {
                        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                        sw.WriteLine($"[{timestamp}] [{level}] {message}");
                        
                        if (ex != null)
                        {
                            sw.WriteLine($"EXCEPTION: {ex.Message}");
                            sw.WriteLine($"STACKTRACE: {ex.StackTrace}");
                            if (ex.InnerException != null)
                            {
                                sw.WriteLine($"INNER EXCEPTION: {ex.InnerException.Message}");
                                sw.WriteLine($"INNER STACKTRACE: {ex.InnerException.StackTrace}");
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore logger failures so it doesn't crash the app
                }
            }
        }
    }
}
