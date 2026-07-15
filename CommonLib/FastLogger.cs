using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CommonLib
{
    /// <summary>
    /// 高性能异步日志类 —— 专用于关键生命周期事件（程序启动、关闭、崩溃）
    /// 设计目标：
    ///   1. 异步写入，不阻塞调用线程（BlockingCollection + 后台线程）
    ///   2. 崩溃场景兜底 —— Emergency() 同步直接写盘
    ///   3. 后续可扩展到高速检测项目中使用（队列容量大、无锁入队）
    /// 注意：正常运行时不使用，避免日志爆炸。
    /// </summary>
    public class FastLogger : IDisposable
    {
        #region 单例
        private static readonly object _initLock = new object();
        private static FastLogger _instance;
        private static volatile bool _initialized;

        /// <summary>获取单例（Init() 之后才可用）</summary>
        public static FastLogger Instance
        {
            get
            {
                if (!_initialized)
                    throw new InvalidOperationException("FastLogger 未初始化，请先调用 FastLogger.Init(logDir)");
                return _instance;
            }
        }

        public static bool IsInitialized => _initialized;

        public static void Init(string logDir)
        {
            lock (_initLock)
            {
                if (_initialized) return;
                _instance = new FastLogger(logDir);
                _initialized = true;
                _instance.Info("══════════════════════════════════════");
                _instance.Info("FastLogger 初始化完成");
                _instance.Info("日志目录: " + logDir);
                _instance.Info("══════════════════════════════════════");
            }
        }
        #endregion

        /// <summary>调试日志开关。True=输出详细调试信息，False=仅输出关键事件</summary>
        public static bool DebugEnabled { get; set; } = false;

        private readonly BlockingCollection<LogEntry> _queue;
        private readonly Thread _worker;
        private readonly string _logDir;
        private volatile bool _disposed;
        private int _droppedCount;

        // 【性能优化】持久 StreamWriter，避免每次 File.AppendAllText 的开销（打开/关闭文件）
        private StreamWriter _writer;
        private string _currentLogDate;
        private readonly object _writeLock = new object();
        private int _entriesSinceFlush;
        private const int FLUSH_INTERVAL = 50; // 每50条 flush 一次

        private FastLogger(string logDir)
        {
            _logDir = logDir ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(_logDir))
                Directory.CreateDirectory(_logDir);

            // 【P2】启动时清理超过30天的日志文件
            CleanOldLogs(_logDir, 30);

            _queue = new BlockingCollection<LogEntry>(new ConcurrentQueue<LogEntry>(), 10000);

            _worker = new Thread(ProcessQueue)
            {
                Name = "FastLogger",
                IsBackground = true,
                Priority = ThreadPriority.Lowest
            };
            _worker.Start();
        }

        public void Info(string message) => Enqueue(LogLevel.INFO, message);
        public void Warn(string message) => Enqueue(LogLevel.WARN, message);
        public void Error(string message) => Enqueue(LogLevel.ERROR, message);

        /// <summary>调试日志（受 DebugEnabled 控制，关闭后不记录）</summary>
        public void Debug(string message)
        {
            if (DebugEnabled) Enqueue(LogLevel.INFO, "[DBG] " + message);
        }

        public void Error(string message, Exception ex)
        {
            if (ex == null)
                Error(message);
            else
                Error(message + " | 异常: " + ex.GetType().Name + " | " + ex.Message + " | " + (ex.StackTrace ?? ""));
        }

        private void Enqueue(LogLevel level, string message)
        {
            if (_disposed) return;
            try
            {
                var entry = new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = level,
                    Message = message ?? "(null)",
                    ThreadId = Thread.CurrentThread.ManagedThreadId
                };
                if (!_queue.TryAdd(entry, 0))
                {
                    int dropped = Interlocked.Increment(ref _droppedCount);
                    if (dropped % 100 == 0)
                    {
                        var warnEntry = new LogEntry
                        {
                            Timestamp = DateTime.Now,
                            Level = LogLevel.WARN,
                            Message = "[FastLogger] 队列已满，已累计丢弃 " + dropped + " 条日志",
                            ThreadId = Thread.CurrentThread.ManagedThreadId
                        };
                        _queue.TryAdd(warnEntry, 10);
                    }
                }
            }
            catch { }
        }

        /// <summary>崩溃专用：同步写入，不经过队列</summary>
        public static void Emergency(string message)
        {
            try
            {
                if (_instance != null && !_instance._disposed)
                {
                    try { _instance.Error("[EMERGENCY] " + message); } catch { }
                    _instance.Flush(2000);
                }
            }
            catch { }

            try
            {
                string crashDir = _instance?._logDir ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                if (!Directory.Exists(crashDir)) Directory.CreateDirectory(crashDir);
                string crashFile = Path.Combine(crashDir, "crash_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] [CRASH] [T" + Thread.CurrentThread.ManagedThreadId + "] " + message;
                File.AppendAllText(crashFile, line + Environment.NewLine);
            }
            catch { }
        }

        public static void Emergency(Exception ex, string context)
        {
            if (ex == null) return;
            string msg = string.IsNullOrEmpty(context)
                ? "异常: " + ex.GetType().Name + " | " + ex.Message + "\r\n堆栈: " + (ex.StackTrace ?? "")
                : "[" + context + "] 异常: " + ex.GetType().Name + " | " + ex.Message + "\r\n堆栈: " + (ex.StackTrace ?? "");

            if (ex is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                    msg += "\r\n  内部异常: " + inner.GetType().Name + " | " + inner.Message;
            }
            else if (ex.InnerException != null)
            {
                msg += "\r\n  内部异常: " + ex.InnerException.GetType().Name + " | " + ex.InnerException.Message;
            }

            Emergency(msg);
        }

        public void Flush(int timeoutMs = 5000)
        {
            if (_disposed) return;
            int waited = 0;
            while (_queue.Count > 0 && waited < timeoutMs)
            {
                Thread.Sleep(20);
                waited += 20;
            }
        }

        public int PendingCount => _queue?.Count ?? 0;
        public int DroppedCount => _droppedCount;

        private void ProcessQueue()
        {
            try
            {
                foreach (var entry in _queue.GetConsumingEnumerable())
                {
                    if (_disposed) break;
                    try { WriteEntry(entry); } catch { }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private void WriteEntry(LogEntry entry)
        {
            try
            {
                string date = entry.Timestamp.ToString("yyyyMMdd");
                string line = "[" + entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] [" + entry.Level + "] [T" + entry.ThreadId.ToString("D2") + "] " + entry.Message;

                lock (_writeLock)
                {
                    // 日期变更 → 关闭旧 writer，开新文件
                    if (_writer == null || _currentLogDate != date)
                    {
                        try { _writer?.Flush(); } catch { }
                        try { _writer?.Dispose(); } catch { }
                        string filePath = Path.Combine(_logDir, "app_" + date + ".log");
                        _writer = new StreamWriter(filePath, true, System.Text.Encoding.UTF8, 4096);
                        _currentLogDate = date;
                        _entriesSinceFlush = 0;
                    }

                    _writer.WriteLine(line);
                    _entriesSinceFlush++;

                    // 定期 flush，平衡性能与数据安全
                    if (_entriesSinceFlush >= FLUSH_INTERVAL)
                    {
                        try { _writer.Flush(); } catch { }
                        _entriesSinceFlush = 0;
                    }
                }
            }
            catch { }
        }

        /// <summary>【P2】清理超过 retentionDays 天的旧日志文件</summary>
        private static void CleanOldLogs(string logDir, int retentionDays)
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-retentionDays);
                foreach (var file in Directory.GetFiles(logDir, "app_*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                            File.Delete(file);
                    }
                    catch { }
                }
                foreach (var file in Directory.GetFiles(logDir, "crash_*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                            File.Delete(file);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                Info("FastLogger 正在关闭...");
                Flush(3000);
                _queue.CompleteAdding();
                if (_worker != null && _worker.IsAlive) _worker.Join(2000);
                _queue.Dispose();
            }
            catch { }
            finally
            {
                // 关闭 StreamWriter
                lock (_writeLock)
                {
                    try { _writer?.Flush(); } catch { }
                    try { _writer?.Dispose(); } catch { }
                    _writer = null;
                }
            }
        }

        private enum LogLevel { INFO, WARN, ERROR }

        private struct LogEntry
        {
            public DateTime Timestamp;
            public LogLevel Level;
            public string Message;
            public int ThreadId;
        }
    }
}
