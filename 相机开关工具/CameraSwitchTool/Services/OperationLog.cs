using System;
using System.IO;
using System.Text;

namespace CameraSwitchTool.Services
{
    /// <summary>
    /// 操作日志：记录保存/放弃/重启等关键操作，供客户现场追溯。
    /// 日志文件位于主程序日志同目录 Logs\CameraSwitchTool.log，
    /// 与主程序日志（Logs\app_*.log、Logs\crash_*.log）区分，不会撞名。
    /// UTF-8（带 BOM）编码，便于记事本直接查看中文。
    /// </summary>
    public static class OperationLog
    {
        private static readonly object LockObj = new object();
        private static string _logPath;

        /// <summary>
        /// 初始化日志文件路径（调用方传入 setup.ini / 主程序所在目录）。
        /// </summary>
        public static void Init(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;
            try
            {
                _logPath = Path.Combine(directory, "Logs", "CameraSwitchTool.log");
                if (!File.Exists(_logPath))
                {
                    // Logs 目录不存在时先创建；首次创建日志时写入 BOM，后续追加不再写 BOM
                    Directory.CreateDirectory(Path.GetDirectoryName(_logPath));
                    File.WriteAllText(_logPath, "﻿", new UTF8Encoding(false));
                }
            }
            catch { }
        }

        public static void Info(string message) { Write("INFO", message); }

        public static void Warn(string message) { Write("WARN", message); }

        public static void Error(string message) { Write("ERROR", message); }

        private static void Write(string level, string message)
        {
            if (string.IsNullOrEmpty(_logPath)) return;
            try
            {
                lock (LockObj)
                {
                    string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}\r\n", DateTime.Now, level, message);
                    File.AppendAllText(_logPath, line, new UTF8Encoding(false));
                }
            }
            catch { /* 日志失败不影响主流程 */ }
        }
    }
}
