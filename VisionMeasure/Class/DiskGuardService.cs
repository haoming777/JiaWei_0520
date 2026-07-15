using CommonLib;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VisionMeasure.Class
{
	/// <summary>
	/// 【P2】磁盘空间监控服务：每60秒检查存图盘符可用空间，
	/// 低于阈值自动暂停存图并记录告警日志。
	/// 独立后台Task，不阻塞主业务流程。
	/// </summary>
	internal class DiskGuardService : IDisposable
	{
		private readonly string _monitorPath;
		private readonly double _warnPercent;    // Warn阈值（默认20%）
		private readonly double _criticalPercent; // Critical阈值（默认10%）
		private readonly int _checkIntervalSec;
		private volatile bool _disposed;
		private CancellationTokenSource _cts;
		private Task _guardTask;

		public DiskGuardService(string monitorPath = null, double warnPercent = 20, double criticalPercent = 10, int checkIntervalSec = 60)
		{
			_monitorPath = monitorPath ?? Class_Config._Config.ImagePath;
			_warnPercent = warnPercent;
			_criticalPercent = criticalPercent;
			_checkIntervalSec = checkIntervalSec;
		}

		public void Start()
		{
			if (_guardTask != null) return;
			_cts = new CancellationTokenSource();
			_guardTask = Task.Run(() => GuardLoop(_cts.Token));
			try { FastLogger.Instance.Info(string.Format("[DiskGuard] Started, path={0} warn={1}% critical={2}% interval={3}s", _monitorPath, _warnPercent, _criticalPercent, _checkIntervalSec)); } catch { }
		}

		private async Task GuardLoop(CancellationToken ct)
		{
			while (!ct.IsCancellationRequested && !_disposed)
			{
				try
				{
					await Task.Delay(_checkIntervalSec * 1000, ct);
					if (ct.IsCancellationRequested || _disposed) break;

					string root = Path.GetPathRoot(_monitorPath);
					if (string.IsNullOrEmpty(root)) continue;

					DriveInfo drive = new DriveInfo(root);
					if (!drive.IsReady) continue;

					double freePercent = (double)drive.AvailableFreeSpace / drive.TotalSize * 100;
					double freeGB = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);

					if (freePercent < _criticalPercent)
					{
						// 紧急：暂停存图
						try { FastLogger.Instance.Error(string.Format("[DiskGuard] CRITICAL: {0} free={1:F1}GB ({2:F1}%). Pausing image save.", root, freeGB, freePercent)); } catch { }
						try { Class_Config._Config.IsSaveOkImage = false; } catch { }
						try { Class_Config._Config.IsSaveNgImage = false; } catch { }
						try { Class_Config._Config.IsSaveOkRawImage = false; } catch { }
						try { Class_Config._Config.IsSaveNgRawImage = false; } catch { }
					}
					else if (freePercent < _warnPercent)
					{
						try { FastLogger.Instance.Warn(string.Format("[DiskGuard] WARN: {0} free={1:F1}GB ({2:F1}%).", root, freeGB, freePercent)); } catch { }
					}
				}
				catch (OperationCanceledException) { break; }
				catch (Exception ex)
				{
					try { FastLogger.Instance.Error("[DiskGuard] " + ex.Message); } catch { }
				}
			}
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			try { _cts?.Cancel(); } catch { }
			try { _cts?.Dispose(); } catch { }
			_guardTask = null;
		}
	}
}
