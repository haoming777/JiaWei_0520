using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CommonLib
{
    /// <summary>
    /// 崩溃转储工具 —— 解决"程序突然消失、无任何日志"问题的最后防线。
    /// 三层兜底：
    ///   1. WriteDump()      —— 全局异常处理器中调用，主动转储进程（含原生内存/线程栈），供 WinDbg/VS 事后分析
    ///   2. TryEnableLocalDumps() —— 启动时尝试配置 WER LocalDumps 注册表；原生代码直接把进程杀死
    ///                               （连托管异常处理器都不执行）时，由 Windows 自动落 dump
    ///   3. IsFatalException()    —— 判断是否为"进程内存已不可信、必须记录后立即退出"的原生级异常
    /// </summary>
    public static class CrashDump
    {
        #region MiniDumpWriteDump P/Invoke (dbghelp.dll)
        [DllImport("dbghelp.dll", SetLastError = true)]
        private static extern bool MiniDumpWriteDump(
            IntPtr hProcess, uint processId, SafeHandle hFile, uint dumpType,
            IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);

        private const uint MiniDumpNormal = 0x00000000;
        private const uint MiniDumpWithDataSegs = 0x00000001;
        private const uint MiniDumpWithHandleData = 0x00000004;
        private const uint MiniDumpWithUnloadedModules = 0x00000020;
        private const uint MiniDumpWithIndirectlyReferencedMemory = 0x00000040;
        private const uint MiniDumpWithThreadInfo = 0x00001000;
        #endregion

        /// <summary>
        /// 转储当前进程到 Logs\Dumps\crash_*.dmp，返回文件路径；失败返回 null（绝不抛异常）。
        /// 注意：必须在崩溃现场调用（全局异常处理器内），此时线程上下文才完整。
        /// </summary>
        public static string WriteDump()
        {
            try
            {
                string dumpDir = GetDumpDir();
                string dumpPath = Path.Combine(dumpDir, "crash_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".dmp");
                using (var fs = new FileStream(dumpPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    // 中等规模转储：数据段+句柄+线程信息+未加载模块+间接引用内存。
                    // 足以定位访问违例/堆损坏/死锁，且不会像全内存转储那样产生数 GB 文件。
                    uint dumpType = MiniDumpNormal | MiniDumpWithDataSegs | MiniDumpWithHandleData
                                  | MiniDumpWithUnloadedModules | MiniDumpWithIndirectlyReferencedMemory
                                  | MiniDumpWithThreadInfo;
                    var proc = Process.GetCurrentProcess();
                    if (MiniDumpWriteDump(proc.Handle, (uint)proc.Id, fs.SafeFileHandle,
                            dumpType, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
                        return dumpPath;
                }
                try { File.Delete(dumpPath); } catch { }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 尝试在注册表启用 WER LocalDumps（需管理员权限）：
        /// 即使原生代码崩溃、托管异常处理器完全没机会执行，Windows 也会自动写 dump 到 Logs\Dumps。
        /// 返回是否启用成功；失败时调用方应记日志提示。
        /// </summary>
        public static bool TryEnableLocalDumps(string exeName)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\" + exeName))
                {
                    if (key == null) return false;
                    key.SetValue("DumpFolder", GetDumpDir(), Microsoft.Win32.RegistryValueKind.ExpandString);
                    // DumpType=2 全内存转储（原生崩溃往往需要看完整内存/GPU上下文）；
                    // 若磁盘空间紧张可改为 1（mini dump）
                    key.SetValue("DumpType", 2, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("DumpCount", 10, Microsoft.Win32.RegistryValueKind.DWord);
                }
                return true;
            }
            catch
            {
                return false; // 非管理员权限时失败
            }
        }

        /// <summary>
        /// 原生级致命异常：进程内存已不可信，catch 后必须"记录 → 重新抛出 → 全局处理器转储退出"，
        /// 绝不能吞掉继续运行（继续跑会把坏数据写进 PLC/数据库）。
        /// </summary>
        public static bool IsFatalException(Exception ex)
        {
            if (ex == null) return false;
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (e is AccessViolationException ||
                    e is SEHException ||
                    e is OutOfMemoryException ||
                    e is StackOverflowException)
                    return true;
            }
            return false;
        }

        /// <summary>dump 统一存放目录：程序目录\Logs\Dumps</summary>
        private static string GetDumpDir()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Dumps");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
