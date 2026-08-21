using CommonLib;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VisionMeasure
{
	internal static class Program
	{
		/// <summary>
		/// 应用程序的主入口点。
		/// </summary>
		[STAThread]
		static void Main()		{
			// ──── 1. 最先注册全局异常捕获（必须在日志系统初始化之前，任何崩溃都要能留痕）────
			Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

			// UI 线程异常（进程不会退出，记日志并提示）
			Application.ThreadException += (sender, e) =>
			{
				FastLogger.Emergency(e.Exception, "UI线程异常");
				MessageBox.Show($"程序发生异常，请查看日志。\n{e.Exception.Message}", "异常",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			};

			// 非 UI 线程异常（最后防线；处理器执行完后进程仍会退出）
			AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
			{
				var ex = e.ExceptionObject as Exception;
				string msg = ex?.Message ?? e.ExceptionObject?.ToString();
				// 1) 进程转储（含原生内存与全部线程栈，供 WinDbg/VS 事后分析）
				string dumpPath = null;
				try { dumpPath = CrashDump.WriteDump(); } catch { }
				// 2) 同步写 crash 日志（不走队列，确保落盘）
				try
				{
					string crashDir = Path.Combine(Application.StartupPath, "Logs");
					if (!Directory.Exists(crashDir)) Directory.CreateDirectory(crashDir);
					string crashFile = Path.Combine(crashDir, "crash_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
					var sb = new StringBuilder();
					sb.AppendFormat("[{0}] CRASH: {1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), msg).AppendLine();
					sb.AppendLine("堆栈: " + (ex?.StackTrace ?? "(no stack)"));
					sb.AppendLine("转储文件: " + (dumpPath ?? "(转储失败)"));
					try
					{
						var proc = Process.GetCurrentProcess();
						sb.AppendLine("运行时长: " + (DateTime.Now - proc.StartTime).ToString(@"dd\.hh\:mm\:ss"));
						sb.AppendLine("内存(工作集): " + (proc.WorkingSet64 / 1024 / 1024) + " MB");
						sb.AppendLine("线程数: " + proc.Threads.Count);
					}
					catch { }
					File.AppendAllText(crashFile, sb.ToString());
				}
				catch { }
				try { FastLogger.Emergency(ex, "未处理异常(AppDomain)"); } catch { }
				MessageBox.Show(string.Format("程序发生严重异常，即将退出。\n{0}\n\n详细信息已写入 Logs\\crash_*.log 与 Logs\\Dumps\\*.dmp", msg), "严重错误",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			};

			// Task 内异常无人观察（fire-and-forget 任务故障留痕，不杀进程）
			TaskScheduler.UnobservedTaskException += (sender, e) =>
			{
				try { FastLogger.Emergency(e.Exception, "未观察的任务异常(Task)"); } catch { }
				e.SetObserved();
			};

			bool isAppRunning = false;
			Mutex appMutex = new Mutex(true, Application.ProductName, out isAppRunning);
			if (!isAppRunning)
			{
				MessageBox.Show("系统已经启动！", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				Environment.Exit(1);
			}
			// appMutex 必须持有到进程退出，不可 using/dispose 否则互斥失效

			// ──── 2. 初始化关键日志系统 ────
			string logDir = Path.Combine(Application.StartupPath, "Logs");
			try { FastLogger.Init(logDir); } catch { }

			// ──── 3. WER LocalDumps：原生代码直接杀死进程（连本文件两个处理器都不执行）时由 Windows 自动落 dump ────
			try
			{
				string exeName = Process.GetCurrentProcess().ProcessName + ".exe";
				if (FastLogger.IsInitialized)
				{
					if (CrashDump.TryEnableLocalDumps(exeName))
						FastLogger.Instance.Info("WER LocalDumps 已启用，原生崩溃将自动转储到 Logs\\Dumps");
					else
						FastLogger.Instance.Warn("WER LocalDumps 启用失败(需管理员权限)，原生崩溃将无法自动转储");
				}
			}
			catch { }

			// ──── 版本标识（每次改动后手动递增 BUILD_TAG，编译时间自动取 exe 时间戳）────
			const string BUILD_TAG = "2026-08-20-v7"; // ← 改代码后记得改这个（v7: PLC状态互锁+气缸日志+实时取像回报位）
			string buildTime = "未知";
			try { buildTime = System.IO.File.GetLastWriteTime(typeof(Program).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss"); } catch { }
			try
			{
				FastLogger.Instance.Info("══════════════════════════════════════");
				FastLogger.Instance.Info("应用程序启动");
				FastLogger.Instance.Info("版本: " + Application.ProductVersion + " | 构建标识: " + BUILD_TAG + " | 编译时间: " + buildTime);
				FastLogger.Instance.Info("══════════════════════════════════════");
			}
			catch { }

			// ──── 4. 会话标记：检测上次是否正常退出（崩溃/强杀/断电 事后可从日志确认），并启动心跳 ────
			SessionMarker.CheckPreviousAndCreate();
			SessionMarker.StartHeartbeat();

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

	/// <summary>
	/// 会话生命周期标记：启动时落一个 session.lock（含启动时间与PID），正常关闭流程末尾删除。
	/// 下次启动若发现文件仍在，说明上次进程未走完正常关闭（崩溃/任务管理器强杀/断电），
	/// 输出 Warn 日志供事后排查“到底是不是人为关闭”。
	/// </summary>
	internal static class SessionMarker
	{
		private static readonly string MarkerPath = Path.Combine(Application.StartupPath, "session.lock");
		private static string _startInfo = "";
		private static Thread _heartbeatThread;
		private static volatile bool _heartbeatStop;

		/// <summary>启动时调用：检查上次会话残留标记并写入本次标记</summary>
		public static void CheckPreviousAndCreate()
		{
			try
			{
				if (File.Exists(MarkerPath))
				{
					string lastInfo = "";
					try { lastInfo = File.ReadAllText(MarkerPath).Trim(); } catch { }
					FastLogger.Instance.Warn(string.Format(
						"[会话] 上次会话未正常退出(疑似崩溃/强杀/断电)! 上次启动信息: {0}", lastInfo));
				}
				else
				{
					FastLogger.Instance.Info("[会话] 上次会话正常退出");
				}
				_startInfo = string.Format("启动时间={0} PID={1}",
					DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
					System.Diagnostics.Process.GetCurrentProcess().Id);
				File.WriteAllText(MarkerPath, _startInfo);
			}
			catch (Exception ex)
			{
				try { FastLogger.Instance.Error("[会话] 标记文件处理异常: " + ex.Message); } catch { }
			}
		}

		/// <summary>
		/// 启动 30 秒心跳：持续刷新 session.lock 的"最后心跳"时间。
		/// 进程一旦崩溃，该文件时间戳会停在死亡前 30 秒内——下次启动看日志即可精确推算死亡时刻，
		/// 与 PLC/相机日志对照就能定位死亡前程序在干什么。
		/// </summary>
		public static void StartHeartbeat()
		{
			try
			{
				if (_heartbeatThread != null) return;
				_heartbeatStop = false;
				_heartbeatThread = new Thread(() =>
				{
					while (!_heartbeatStop)
					{
						Thread.Sleep(30000);
						if (_heartbeatStop) break;
						try
						{
							File.WriteAllText(MarkerPath, _startInfo + " 最后心跳=" +
								DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
						}
						catch { } // 磁盘故障时心跳失败不致命，继续下一轮
					}
				})
				{ IsBackground = true, Name = "SessionHeartbeat" };
				_heartbeatThread.Start();
			}
			catch { }
		}

		/// <summary>正常关闭流程完成后调用：删除标记，表示本次为干净退出</summary>
		public static void MarkCleanExit()
		{
			try { _heartbeatStop = true; } catch { }
			try { if (File.Exists(MarkerPath)) File.Delete(MarkerPath); } catch { }
		}
	}
}
