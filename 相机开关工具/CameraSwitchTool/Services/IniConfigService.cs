using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CameraSwitchTool.Services
{
    /// <summary>
    /// setup.ini 读写服务。
    /// P/Invoke 声明与主程序 CommonLib/IniAPI.cs 完全一致（CharSet.Auto），
    /// 保证读写行为与主程序相同，不会破坏文件中其他节点。
    /// </summary>
    public static class IniConfigService
    {
        public const string Section = "system";

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint GetPrivateProfileString(string lpAppName, string lpKeyName, string lpDefault,
            char[] lpReturnedString, uint nSize, string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WritePrivateProfileString(string lpAppName, string lpKeyName, string lpString,
            string lpFileName);

        /// <summary>
        /// 定位 setup.ini：优先工具所在目录（部署时与 VisionMeasure.exe 同目录），
        /// 其次取正在运行的主程序目录。
        /// </summary>
        public static string LocateIniPath()
        {
            string own = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "setup.ini");
            if (File.Exists(own)) return own;

            try
            {
                foreach (var p in Process.GetProcessesByName(MainProcessController.MainProcessName))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(p.MainModule.FileName);
                        string candidate = Path.Combine(dir, "setup.ini");
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { /* 进程可能刚退出或无权访问 */ }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 读取布尔键。键不存在或值无法解析时返回 null，由调用方决定默认值（默认 True，与主程序一致）。
        /// </summary>
        public static bool? ReadBool(string iniPath, string key)
        {
            char[] buffer = new char[256];
            uint n = GetPrivateProfileString(Section, key, "", buffer, (uint)buffer.Length, iniPath);
            if (n == 0) return null;
            string value = new string(buffer, 0, (int)n);
            bool parsed;
            if (bool.TryParse(value.Trim(), out parsed)) return parsed;
            return null;
        }

        public static void WriteBool(string iniPath, string key, bool value)
        {
            bool ok = WritePrivateProfileString(Section, key, value ? "True" : "False", iniPath);
            if (!ok)
            {
                throw new InvalidOperationException(string.Format("写入 setup.ini 失败：{0}（可能是文件被占用或只读）", key));
            }
        }

        /// <summary>
        /// 保存前滚动备份（每次覆盖同名备份文件，位于 setup.ini 同目录）。
        /// </summary>
        public static void BackupIni(string iniPath)
        {
            string backup = iniPath + ".camswitch.bak";
            File.Copy(iniPath, backup, true);
        }
    }
}
