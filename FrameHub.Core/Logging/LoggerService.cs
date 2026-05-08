using System;
using System.IO;
using System.Text;
using FrameHub.Core.Services;

namespace FrameHub.Core.Logging
{
    public class LoggerConfiguration
    {
        public bool IsEnabled { get; set; } = true;
        public ILogLevel LogLevel { get; set; } = FrameHub.Core.Logging.LogLevel.Info;
        public string LogFilePath { get; set; } = AppPaths.GetUserDataFilePath("FrameHub.log");
        public bool EnableConsoleOutput { get; set; } = false;
        public string DateFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";
        public string SourceName { get; set; } = "FrameHub";
    }

    public class ConsoleWriter : IDisposable
    {
        private readonly LoggerConfiguration _config;
        private bool _disposed;

        public ConsoleWriter(LoggerConfiguration config) => _config = config;

        public void Write(ILogLevel level, string message, Exception? ex = null)
        {
            if (_disposed || !_config.EnableConsoleOutput) return;

            var timestamp = DateTime.Now.ToString(_config.DateFormat);
            var formattedMessage = $"{timestamp} - {_config.SourceName} [{level.Name}] - {message}";
            Console.WriteLine(ex == null ? formattedMessage : $"{formattedMessage}\n{ex}");
        }

        public void Dispose() => _disposed = true;
    }

    public class FileWriter : IDisposable
    {
        private const long MaxLogFileBytes = 5 * 1024 * 1024;
        private const int MaxRolledFiles = 3;

        private readonly LoggerConfiguration _config;
        private StreamWriter? _streamWriter;
        private bool _disposed;

        public FileWriter(LoggerConfiguration config)
        {
            _config = config;
            OpenFile();
        }

        private void OpenFile()
        {
            var directory = Path.GetDirectoryName(_config.LogFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            RollIfNeeded();

            _streamWriter = new StreamWriter(_config.LogFilePath, append: true, Encoding.UTF8)
            {
                AutoFlush = true
            };
        }

        private void RollIfNeeded()
        {
            try
            {
                if (!File.Exists(_config.LogFilePath)) return;
                var info = new FileInfo(_config.LogFilePath);
                if (info.Length < MaxLogFileBytes) return;

                for (int i = MaxRolledFiles - 1; i >= 1; i--)
                {
                    string source = $"{_config.LogFilePath}.{i}";
                    string target = $"{_config.LogFilePath}.{i + 1}";
                    if (File.Exists(source))
                    {
                        File.Copy(source, target, overwrite: true);
                    }
                }

                File.Copy(_config.LogFilePath, $"{_config.LogFilePath}.1", overwrite: true);
                File.WriteAllText(_config.LogFilePath, string.Empty, Encoding.UTF8);
            }
            catch
            {
                // Never let log rotation break the application.
            }
        }

        public void Write(ILogLevel level, string message, Exception? ex = null)
        {
            if (_disposed || _streamWriter == null) return;

            var timestamp = DateTime.Now.ToString(_config.DateFormat);
            var formattedMessage = $"{timestamp} - {_config.SourceName} [{level.Name}] - {message}";

            try
            {
                _streamWriter.WriteLine(formattedMessage);
                if (ex != null)
                {
                    _streamWriter.WriteLine(ex);
                }
            }
            catch (IOException ioEx)
            {
                Console.WriteLine($"[FATAL] Failed to write log file: {ioEx.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _streamWriter?.Dispose();
            _streamWriter = null;
        }
    }

    public class LoggerService : ILogger
    {
        private static readonly Lazy<LoggerService> _instance = new(() => new LoggerService());

        private readonly object _sync = new();
        private readonly ConsoleWriter _consoleWriter;
        private FileWriter _fileWriter;
        private readonly LoggerConfiguration _config;
        private ILogLevel _currentLogLevel;
        private bool _disposed;

        public static LoggerService Shared => _instance.Value;
        public static ILogger Instance => Shared;

        public LoggerConfiguration Configuration => _config;

        public ILogLevel LogLevel
        {
            get => _currentLogLevel;
            set
            {
                _currentLogLevel = value ?? FrameHub.Core.Logging.LogLevel.Info;
                _config.LogLevel = _currentLogLevel;
            }
        }

        private LoggerService()
        {
            _config = new LoggerConfiguration();
            _currentLogLevel = _config.LogLevel;
            _consoleWriter = new ConsoleWriter(_config);
            _fileWriter = new FileWriter(_config);
        }

        public void Configure(bool enabled, ILogLevel logLevel, string logFilePath, bool enableConsoleOutput, string sourceName)
        {
            lock (_sync)
            {
                _config.IsEnabled = enabled;
                _config.LogLevel = logLevel ?? FrameHub.Core.Logging.LogLevel.Info;
                _currentLogLevel = _config.LogLevel;
                _config.EnableConsoleOutput = enableConsoleOutput;
                _config.SourceName = string.IsNullOrWhiteSpace(sourceName) ? "FrameHub" : sourceName.Trim();

                string resolvedPath = AppPaths.ResolveUserLogFilePath(logFilePath);
                if (!string.Equals(_config.LogFilePath, resolvedPath, StringComparison.OrdinalIgnoreCase))
                {
                    _fileWriter.Dispose();
                    _config.LogFilePath = resolvedPath;
                    _fileWriter = new FileWriter(_config);
                }
            }
        }

        public void Debug(string message) => Log(FrameHub.Core.Logging.LogLevel.Debug, message);
        public void Info(string message) => Log(FrameHub.Core.Logging.LogLevel.Info, message);
        public void Warn(string message) => Log(FrameHub.Core.Logging.LogLevel.Warn, message);
        public void Error(string message, Exception? ex = null) => Log(FrameHub.Core.Logging.LogLevel.Error, message, ex);
        public void Fatal(string message, Exception? ex = null) => Log(FrameHub.Core.Logging.LogLevel.Fatal, message, ex);

        public void Log(ILogLevel level, string message, Exception? ex = null)
        {
            if (_disposed || !_config.IsEnabled || !_currentLogLevel.IsEnabled(level)) return;

            lock (_sync)
            {
                _consoleWriter.Write(level, message, ex);
                _fileWriter.Write(level, message, ex);
            }
        }

        public void LogException(Exception ex) => Error($"Uncaught exception: {ex.Message}", ex);

        public void Dispose()
        {
            if (_disposed) return;
            _consoleWriter.Dispose();
            _fileWriter.Dispose();
            _disposed = true;
        }
    }
}
