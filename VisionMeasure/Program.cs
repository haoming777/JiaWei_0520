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
			using (Mutex mutex = new Mutex(true, Application.ProductName, out isAppRunning))
			{
				if (!isAppRunning)
				{
					MessageBox.Show("系统已经启动！", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					Environment.Exit(1);
				}
			}

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
				FastLogger.Emergency(ex, "未处理异常(AppDomain)");
				string msg = ex?.Message ?? e.ExceptionObject?.ToString();
				MessageBox.Show($"程序发生严重异常，即将退出。\n{msg}", "严重错误",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				Environment.Exit(1);
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
