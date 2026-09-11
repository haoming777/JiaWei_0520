using System;
using System.Diagnostics;
using System.IO;

namespace CameraSwitchTool.Services
{
    /// <summary>
    /// 主程序（VisionMeasure.exe）进程控制：状态查询、启动、优雅停止、重启。
    /// 注意：启动时必须指定 WorkingDirectory 为主程序目录，
    /// 主程序通过 Directory.GetCurrentDirectory() 定位 setup.ini/vpp/data。
    /// </summary>
    public static class MainProcessController
    {
        public const string MainProcessName = "VisionMeasure";
        public const string MainExeFile = "VisionMeasure.exe";

        public static bool IsRunning()
        {
            return Process.GetProcessesByName(MainProcessName).Length > 0;
        }

        public static string FindMainExePath()
        {
            foreach (var p in Process.GetProcessesByName(MainProcessName))
            {
                try
                {
                    string file = p.MainModule.FileName;
                    if (File.Exists(file)) return file;
                }
                catch { /* 进程可能刚退出或无权访问 */ }
            }

            string candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MainExeFile);
            return File.Exists(candidate) ? candidate : null;
        }

        /// <summary>
        /// 优雅关闭主程序：先发关闭消息（触发其 FormClosing 清理相机/线程），
        /// 超时后强制结束。主程序有单实例互斥，进程退出后互斥自动释放。
        /// </summary>
        public static void StopMainProcess()
        {
            foreach (var p in Process.GetProcessesByName(MainProcessName))
            {
                try { p.CloseMainWindow(); } catch { /* 主窗口可能已关闭 */ }

                if (!p.WaitForExit(10000))
                {
                    try { p.Kill(); }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("无法结束主程序进程（可能权限不足）：" + ex.Message);
                    }
                    if (!p.WaitForExit(5000))
                        throw new InvalidOperationException("主程序进程未能在规定时间内退出");
                }
            }
        }

        public static Process StartMainProcess()
        {
            string exe = FindMainExePath();
            if (exe == null)
                throw new FileNotFoundException("未找到主程序 VisionMeasure.exe，请将本工具复制到主程序目录下运行");

            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false
            };
            return Process.Start(psi);
        }

        public static void RestartMainProcess()
        {
            string exe = FindMainExePath();
            if (exe == null)
                throw new FileNotFoundException("未找到主程序 VisionMeasure.exe，请将本工具复制到主程序目录下运行");

            StopMainProcess();
            StartMainProcess();
        }
    }
}
