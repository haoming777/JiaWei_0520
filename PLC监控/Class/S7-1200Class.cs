using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Windows.Forms.AxHost;
using static CommonLib.Class_Config;
using XL.Tool;
using HslCommunication.Profinet.Siemens;
using HslCommunication;
using System.Xml.Serialization;
using HslCommunication.Profinet.Siemens.S7PlusHelper;

namespace PLC调试.Class
{
	public class S7_1200Class : IPlcCommunication
	{
		Thread doKeepAlive;        // 心跳

		Thread doState;

		Thread doReadT1;

		Stopwatch timeOut;
		Stopwatch triggerWatch = new Stopwatch();   // 触发间隔统计

		private volatile bool _disposed = false;

		public S7_1200Class()
		{
			timeOut = new Stopwatch();

			doReadT1 = new Thread(new ThreadStart(ReadGetTrigger));
			doReadT1.IsBackground = true;


			doState = new Thread(new ThreadStart(DoStateMethod));
			doState.IsBackground = true;

			doKeepAlive = new Thread(new ThreadStart(WriteKeepAlive));
			doKeepAlive.IsBackground = true;

			doReadT1.Start();
			doKeepAlive.Start();
			doState.Start();

			doReadCount = new Thread(new ThreadStart(DoReadCount));
			doReadCount.IsBackground = true;
			toolClass.SaveLog("doReadCount.Start");
			doReadCount.Start();

			doReadStatus = new Thread(new ThreadStart(DoReadDeviceStatus));
			doReadStatus.IsBackground = true;
			doReadStatus.Start();
			toolClass.SaveLog("doReadCount.Start完成");
			toolClass.SaveLog("PLC初始化完成");

			_plcSendStatistics.Start();
		}

		/// <summary>
		/// 释放资源，安全停止所有后台线程，防止程序关闭时卡死
		/// </summary>
		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;

			// 先断开PLC连接，让所有while循环的plcState检查失效
			try { plc.ConnectClose(); } catch { }
			plcState = false;

			// 等待所有线程退出（最多等3秒）
			var threads = new Thread[] { doReadT1, doKeepAlive, doState, doReadCount, doReadStatus };
			foreach (var t in threads)
			{
				if (t != null && t.IsAlive)
				{
					try { t.Join(3000); } catch { }
				}
			}

			_plcSendStatistics.Stop();
			toolClass.SaveLog("PLC资源已释放");
		}

		public event PlcConnectStateHandler EventConnectState;

		public delegate void DelegateTriggerGet();
		public event DelegateTriggerGet EventTriggerGet;

		public event PlcCountHandler EventCount;

		/// <summary>设备运行模式变化事件（DB1000.DBW323，4=自动 其他=手动）</summary>
		public event PlcDeviceModeHandler EventDeviceMode;

		/// <summary>气缸禁用状态变化事件（DB1000.DBX234.0，TRUE=禁用 FALSE=启用）</summary>
		public event PlcCylinderStateHandler EventCylinderState;

		SiemensS7Net plc = new SiemensS7Net(SiemensPLCS.S1200);

		XLToolClass toolClass = new XLToolClass();
		bool plcState = false;
		public bool modbusState => plcState;

		/// <summary>设备是否处于自动模式（DB1000.DBW323==4）。首次成功读取前返回 false（按手动处理）</summary>
		public bool IsAutoMode => _statusInited && _lastModeRaw == 4;

		/// <summary>兼容 HCModbusClass 的 modbusTcp.Write 调用</summary>
		public void WriteRegister(string address, short value)
		{
			plc.Write(address, value);
		}

		public void WriteRegister(string address, bool value)
		{
			plc.Write(address, value);
		}

		/// <summary>
		/// 读设备模式 DB1000.DBW323（4=自动）与气缸状态 DB1000.DBX234.0（TRUE=禁用），周期 100ms。
		/// 【关键安全设计】读失败不置 plcState=false、不触发 EventConnectState——
		/// 这两个是 PLC 程序新增地址，现场 PLC 未同步更新时会持续读失败；
		/// 若按既有线程模式判断线，会导致 WriteResult 停止、整机停摆。
		/// 改为节流 Warn 日志暴露问题：互锁自然失效，但不影响生产主流程。
		/// </summary>
		private void DoReadDeviceStatus()
		{
			int failCount = 0;
			while (!_disposed)
			{
				try
				{
					Thread.Sleep(100);
					if (!plcState) continue;

					short modeRaw = plc.ReadInt16("DB1000.DBW323").Content;
					bool cylDisabled = plc.ReadBool("DB1000.DBX234.0").Content;
					failCount = 0;

					if (!_statusInited)
					{
						_statusInited = true;
						_lastModeRaw = modeRaw;
						_cylinderDisabled = cylDisabled;
						EventDeviceMode?.Invoke(modeRaw == 4, modeRaw);   // 首读触发事件，记录初始状态
						EventCylinderState?.Invoke(cylDisabled);
						// 【气缸状态】日志：首读记录初始状态（含来源地址；日志搜索关键词：【气缸状态】）
						try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Info(string.Format("【气缸状态】[S7-1200] 初始状态: {0} 来源:DB1000.DBX234.0", cylDisabled ? "禁用(TRUE)" : "启用(FALSE)")); } catch { }
					}
					else
					{
						if (_lastModeRaw != modeRaw) { _lastModeRaw = modeRaw; EventDeviceMode?.Invoke(modeRaw == 4, modeRaw); }
						if (_cylinderDisabled != cylDisabled)
						{
							bool prevDisabled = _cylinderDisabled;
							_cylinderDisabled = cylDisabled;
							EventCylinderState?.Invoke(cylDisabled);
							// 【气缸状态】日志：状态变化记录旧→新方向与来源地址（日志搜索关键词：【气缸状态】）
							try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Info(string.Format("【气缸状态】[S7-1200] 状态变化: {0}→{1} 来源:DB1000.DBX234.0", prevDisabled ? "禁用(TRUE)" : "启用(FALSE)", cylDisabled ? "禁用(TRUE)" : "启用(FALSE)")); } catch { }
						}
					}
				}
				catch (Exception ex)
				{
					failCount++;
					if (failCount == 1 || failCount % 600 == 0) // 首次 + 每约60秒一次节流
						try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Warn(string.Format("【设备状态】读DB1000.DBW323/DBX234.0失败(第{0}次): {1}（不影响主流程；持续出现请检查PLC程序是否已包含该地址）", failCount, ex.Message)); } catch { }
					Thread.Sleep(1000);
				}
			}
		}

		private readonly SendIntervalStatistics _plcSendStatistics = new SendIntervalStatistics();

		Thread doReadCount;

		Thread doReadStatus;                     // 设备状态/气缸状态轮询线程
		volatile short _lastModeRaw = 0;         // DBW323 上次值
		volatile bool _cylinderDisabled = false; // DBX234.0 上次值
		volatile bool _statusInited = false;     // 首读成功标志

		public bool ConnectModbus()
		{
			try
			{
				plc.IpAddress = _Config.PlcIP;
				plc.Port = _Config.PlcPort;
				plc.ConnectTimeOut = 2000; // P1: 2s timeout prevents PLC blocking from stalling threads

				plc?.ConnectClose();
				OperateResult connectState = plc.ConnectServer();
				plcState = connectState.IsSuccess;

				if (connectState.IsSuccess)
				{

					timeOut.Restart();

					EventConnectState(true, "PLC连接成功");
				try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug("S7-1200连接成功 IP=" + _Config.PlcIP + ":" + _Config.PlcPort + ""); } catch { }
					return true;
				}
				else
				{
					EventConnectState(false, "PLC连接失败");
				try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug("S7-1200连接失败 IP=" + _Config.PlcIP + ":" + _Config.PlcPort + ""); } catch { }
					return false;
				}
			}
			catch (Exception ex)
			{
				plcState = false;
				EventConnectState(false, $"连接PLC错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return false;
			}

		}

		public void CloseModbus()
		{
			Dispose();
		}

		private void ReadGetTrigger()
		{
			string path = "DB1000.DBW0";
			short val = 0;
			while (!_disposed)
			{
				try
				{
					Thread.Sleep(50);
					if (!plcState) continue;

					short test = Convert.ToInt16(plc.ReadInt16("DB1000.DBW0").Content);
					if (test == 1)
					{
						if (triggerWatch.IsRunning)
						{
							long trigMs = triggerWatch.ElapsedMilliseconds;
							if (trigMs > 500)
							{
								try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Warn("触发间隔偏长: " + trigMs + "ms"); } catch { }
							}
							triggerWatch.Restart();
						}
						else triggerWatch.Start();
						EventTriggerGet?.Invoke();
						Thread.Sleep(50);
						plc.Write("DB1000.DBW0", val);
						toolClass.SaveLog($"写零后读取{plc.ReadInt16(path).Content}");
					}
				}
				catch (Exception ex)
				{
					plcState = false;
					EventConnectState(false, $"读触发信号时出现异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
					Thread.Sleep(1000);
				}
			}
		}


		private void WriteKeepAlive()
		{
			short val = 1;
			while (!_disposed)
			{
				try
				{
					Thread.Sleep(500);
					if (plcState)
						plc.Write("DB1000.DBW78", val);
				}
				catch (Exception ex)
				{
					plcState = false;
					EventConnectState(false, $"向PLC写心跳时发生错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
					Thread.Sleep(1000);
				}
			}
		}

		private void DoStateMethod()
		{
			timeOut.Start();
			short oldVal = 0;
			while (!_disposed)
			{
				try
				{
					Thread.Sleep(50);
					if (plcState)
					{
						short newVal = plc.ReadInt16("DB1000.DBW78").Content;
						if (oldVal != newVal)
						{
							oldVal = newVal;
							timeOut.Restart();
						}

						if (timeOut.ElapsedMilliseconds > 10000)
						{
							plcState = false;
							EventConnectState(false, $"心跳状态超十秒未更新，判定为通讯断开状态，最后一次为[{newVal}]");
							try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug("S7-1200心跳超时, 最后值=" + newVal); } catch { }
						}
					}
				}
				catch (Exception ex)
				{
					plcState = false;
					EventConnectState(false, $"DoState异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
					Thread.Sleep(1000);
				}
			}
		}


		public void ClearCount()
		{
			try
			{
				if (plcState)
					plc.Write("DB1000.DBX208.1", true);
			}
			catch (Exception ex)
			{
				plcState = false;
				EventConnectState(false, $"ClearCount发生错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		public void RuningMethod()
		{
			try
			{
				try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Info("RuningMethod 开始发送运行信号..."); } catch { }
				// 等待 PLC 连接就绪（最多重试 5 次，每次等 1 秒）
				const int MAX_RETRY = 5;
				for (int retry = 0; retry < MAX_RETRY; retry++)
				{
					if (plcState) break;
					if (retry == 0)
						try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Warn(string.Format("RuningMethod: plcState=false, 等待重连...({0}/{1})", retry + 1, MAX_RETRY)); } catch { }
					System.Threading.Thread.Sleep(1000);
				}
				if (!plcState)
				{
					try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Error("RuningMethod: 等待超时, plcState 仍为 false, 未发送运行信号"); } catch { }
					return;
				}

				short ok = 1;
				try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Info("S7-1200 → DBX72.4=True"); } catch { }
				plc.Write("DB1000.DBX72.4", true);
				try
				{
					bool rb1 = plc.ReadBool("DB1000.DBX72.4").Content;
					try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Info(string.Format("S7-1200 回读 → DBX72.4={0}", rb1)); } catch { }
					if (!rb1)
						try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Warn("S7-1200 DBX72.4 回读不一致!"); } catch { }
				}
				catch { }

				try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Info("S7-1200 → DBW88=" + ok); } catch { }
				plc.Write("DB1000.DBW88", ok);
				try
				{
					short rw1 = plc.ReadInt16("DB1000.DBW88").Content;
					try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Info(string.Format("S7-1200 回读 → DBW88={0}", rw1)); } catch { }
					if (rw1 != ok)
						try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Warn(string.Format("S7-1200 DBW88 回读不一致! 期望:{0} 实际:{1}", ok, rw1)); } catch { }
				}
				catch { }

				try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Info("RuningMethod 运行信号发送完成"); } catch { }
			}
			catch (Exception ex)
			{
				plcState = false;
				EventConnectState(false, string.Format("RuningMethod发生错误...\r\n {0} \r\n {1}", ex.Message, ex.StackTrace));
			}
		}

		public bool WriteResult(bool result1, bool result2, bool result3)
		{
			try
			{
				if (!plcState)
				{
					toolClass.SaveLog($"WriteResult写入结果时，PLC状态为：{plcState}");
					return false;
				}

				ushort ok = 1;
				ushort ng = 2;
				ushort v1 = result1 ? ok : ng;
				ushort v2 = result2 ? ok : ng;
				ushort v3 = result3 ? ok : ng;
				try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug($"S7-1200 → DBW80={v1} DBW82={v2} DBW84={v3} DBW86={ok}"); } catch { }
				var sw = Stopwatch.StartNew();

				int writeErrors = 0;
				var wr1 = plc.Write("DB1000.DBW80", new ushort[] { v1, v2, v3 });
				if (!wr1.IsSuccess)
				{
					writeErrors++;
					try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Warn($"S7-1200 批量写入 DBW80-84 失败! {wr1.Message}"); } catch { }
				}

				var wr2 = plc.Write("DB1000.DBW86", ok);
				if (!wr2.IsSuccess)
				{
					writeErrors++;
					try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Warn($"S7-1200 DBW86 写入失败! {wr2.Message}"); } catch { }
				}

				long interval = _plcSendStatistics.RecordSend();
				if (interval > 0)
				{
					try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug($"每组结果发送间隔: {interval}ms"); } catch { }
					if (_plcSendStatistics.GetStatistics().ValidCount % 10 == 0)
					{
						var stats = _plcSendStatistics.GetStatistics();
						try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug($"发送间隔统计（每10次）: {stats}"); } catch { }
					}
				}
				try
				{
					if (CommonLib.FastLogger.IsInitialized)
						CommonLib.FastLogger.Instance.Debug(string.Format(
							"写入结果完成，耗时：{0}ms，DBW80={1} DBW82={2} DBW84={3} DBW86={4} 失败{5}路",
							sw.ElapsedMilliseconds, v1, v2, v3, ok, writeErrors));
				}
				catch { }
				if (sw.ElapsedMilliseconds > 50)
					try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug("S7-1200写入耗时偏高: " + sw.ElapsedMilliseconds + "ms"); } catch { }
				return writeErrors == 0;
			}
			catch (Exception ex)
			{
				plcState = false;
				EventConnectState(false, string.Format("向PLC写入结果时发生异常...\r\n {0} \r\n {1}", ex.Message, ex.StackTrace));
				return false;
			}
		}

		private void DoReadCount()
		{
			uint count1 = 0, count2 = 0, count3 = 0, count4 = 0, count5 = 0;
			toolClass.SaveLog("读PLC计数开始");

			while (!_disposed)
			{
				try
				{
					Thread.Sleep(100);
					if (plcState)
					{
						count1 = plc.ReadUInt32("DB1000.DBD100").Content;
						count2 = plc.ReadUInt32("DB1000.DBD104").Content;
						count3 = plc.ReadUInt32("DB1000.DBD108").Content;
						count4 = plc.ReadUInt32("DB1000.DBD112").Content;
						count5 = plc.ReadUInt32("DB1000.DBD116").Content;

						EventCount?.Invoke(count1, count2, count3, count4, count5);
					}
					else
					{
						errorCount++;
					}
				}
				catch (Exception ex)
				{
					plcState = false;
					EventConnectState(false, $"读PLC计数错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
					Thread.Sleep(1000);
				}
			}
		}

		int errorCount = 0;
		bool bRunning = false;
		int ReconnectCount = 0;

		public void Reconnect()
		{
			if (bRunning) return;
			toolClass.SaveLog("尝试重新连接PLC");
			Task.Run(() =>
			{
				bRunning = true;
				while (!plcState && !_disposed)
				{
					ReconnectCount++;
					toolClass.SaveLog($"正在尝试第 {ReconnectCount} 次重连");
					ConnectModbus();
					Thread.Sleep(1000);
				}
				bRunning = false;
				toolClass.SaveLog($"在第 {ReconnectCount} 次时重连成功");
				try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug("S7-1200重连成功, 尝试" + ReconnectCount + "次"); } catch { }
				ReconnectCount = 0;
			});
		}
	}
}
