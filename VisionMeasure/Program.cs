using CommonLib;
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace VisionMeasure
{
	internal static class Program
	{
		/// <summary>
		/// 应用程序的主入口点。
		/// </summary>
		[STAThread]
		static void Main()
		{
			bool isAppRunning = false;
			Mutex appMutex = new Mutex(true, Application.ProductName, out isAppRunning);
			if (!isAppRunning)
			{
				MessageBox.Show("系统已经启动！", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				Environment.Exit(1);
			}
			// appMutex 必须持有到进程退出，不可 using/dispose 否则互斥失效

			// ──── 初始化关键日志系统（最早调用）────
			string logDir = Path.Combine(Application.StartupPath, "Logs");
			try { FastLogger.Init(logDir); } catch { }
			FastLogger.Instance.Info("══════════════════════════════════════");
			FastLogger.Instance.Info("应用程序启动");
			FastLogger.Instance.Info("版本: " + Application.ProductVersion);
			FastLogger.Instance.Info("══════════════════════════════════════");

			// ──── 全局异常捕获（阻止闪退，记日志）────
			Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

			// UI 线程异常
			Application.ThreadException += (sender, e) =>
			{
				FastLogger.Emergency(e.Exception, "UI线程异常");
				MessageBox.Show($"程序发生异常，请查看日志。\n{e.Exception.Message}", "异常",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			};

			// 非 UI 线程异常（最后防线）
			AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
			{
				var ex = e.ExceptionObject as Exception;
				string msg = ex?.Message ?? e.ExceptionObject?.ToString();
				// 同步写 crash 日志（不走队列，确保落盘）
				try
				{
					string crashDir = Path.Combine(Application.StartupPath, "Logs");
					if (!Directory.Exists(crashDir)) Directory.CreateDirectory(crashDir);
					string crashFile = Path.Combine(crashDir, "crash_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
					string crashInfo = string.Format("[{0}] CRASH: {1}\n{2}",
						DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), msg,
						ex?.StackTrace ?? "(no stack)");
					File.AppendAllText(crashFile, crashInfo + Environment.NewLine);
				}
				catch { }
				try { FastLogger.Emergency(ex, "未处理异常(AppDomain)"); } catch { }
				MessageBox.Show(string.Format("程序发生严重异常，即将退出。\n{0}\n\n详细信息已写入 Logs\\crash_*.log", msg), "严重错误",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			};

			try
			{
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);

				FastLogger.Instance.Info("开始加载主窗体...");
				MainFrm mainFrm = new MainFrm();
				FastLogger.Instance.Info("主窗体已创建，进入消息循环");
				Application.Run(mainFrm);
				FastLogger.Instance.Info("消息循环结束，程序正常退出");
			}
			catch (Exception ex)
			{
				FastLogger.Emergency(ex, "Main函数异常");
				MessageBox.Show($"程序启动失败: {ex.Message}", "启动错误",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				FastLogger.Instance.Info("应用程序关闭");
				try { FastLogger.Instance.Flush(3000); FastLogger.Instance.Dispose(); } catch { }
			}
		}
	}
}
