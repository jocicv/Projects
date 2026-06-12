namespace AirQualityServer.Logging
{

    public sealed class ThreadSafeLogger : IDisposable
    {
        private readonly Queue<string> _logQueue = new();
        private readonly object _queueLock = new();
        private readonly Thread _writerThread;
        private readonly string? _logFilePath;
        private bool _disposed;
        private bool _running = true;

        private static ThreadSafeLogger? _instance;
        private static readonly object _instanceLock = new();

        public static ThreadSafeLogger Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        _instance ??= new ThreadSafeLogger("server.log");
                    }
                }
                return _instance;
            }
        }

        private ThreadSafeLogger(string? logFilePath = null)
        {
            _logFilePath = logFilePath;

            _writerThread = new Thread(ProcessLogQueue)
            {
                Name         = "LogWriter",
                IsBackground = true,
                Priority     = ThreadPriority.BelowNormal
            };
            _writerThread.Start();
        }

        private void ProcessLogQueue()
        {
            while (_running || HasPendingLogs())
            {
                string? entry = null;

                lock (_queueLock)
                {
                    while (_logQueue.Count == 0 && _running)
                        Monitor.Wait(_queueLock, 200);

                    if (_logQueue.Count > 0)
                        entry = _logQueue.Dequeue();
                }

                if (entry != null)
                {
                    try
                    {
                        Console.WriteLine(entry);
                        if (_logFilePath != null)
                            File.AppendAllText(_logFilePath, entry + Environment.NewLine);
                    }
                    catch { }
                }
            }
        }

        private bool HasPendingLogs()
        {
            lock (_queueLock)
                return _logQueue.Count > 0;
        }

        public void Log(LogLevel level, string message, string? requestId = null)
        {
            if (_disposed) return;

            var prefix    = requestId != null ? $"[REQ:{requestId}]" : "[SYSTEM]";
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var levelStr  = level switch
            {
                LogLevel.Info    => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error   => "ERROR",
                LogLevel.Debug   => "DEBUG",
                _                => "INFO "
            };

            var entry = $"{timestamp} [{levelStr}] {prefix} {message}";

            lock (_queueLock)
            {
                _logQueue.Enqueue(entry);
                Monitor.Pulse(_queueLock);
            }
        }

        public void Info(string message, string? requestId = null)    => Log(LogLevel.Info, message, requestId);
        public void Warning(string message, string? requestId = null) => Log(LogLevel.Warning, message, requestId);
        public void Error(string message, string? requestId = null)   => Log(LogLevel.Error, message, requestId);
        public void Debug(string message, string? requestId = null)   => Log(LogLevel.Debug, message, requestId);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running  = false;

            lock (_queueLock)
                Monitor.PulseAll(_queueLock);

            _writerThread.Join(3000);
        }
    }

    public enum LogLevel { Debug, Info, Warning, Error }
}
