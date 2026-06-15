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
	public class S7_1200Class
	{
		Thread doKeepAlive;        // 心跳

		Thread doState;

		Thread doReadT1;

		Stopwatch timeOut;
		Stopwatch triggerWatch = new Stopwatch();   // 触发间隔统计

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
			toolClass.SaveLog("doReadCount.Start完成");
			toolClass.SaveLog("PLC初始化完成");

			_plcSendStatistics.Start();
		}

		public delegate void DelegateConnectState(bool state, string error);
		public event DelegateConnectState EventConnectState;

		public delegate void DelegateTriggerGet();
		public event DelegateTriggerGet EventTriggerGet;

		public delegate void DelegateCount(uint count1, uint count2, uint count3, uint count4, uint count5);
		public event DelegateCount EventCount;

		SiemensS7Net plc = new SiemensS7Net(SiemensPLCS.S1200);

		XLToolClass toolClass = new XLToolClass();
		bool plcState = false;
		public bool modbusState => plcState;

		/// <summary>兼容 HCModbusClass 的 modbusTcp.Write 调用</summary>
		public void WriteRegister(string address, short value)
		{
			plc.Write(address, value);
		}

		public void WriteRegister(string address, bool value)
		{
			plc.Write(address, value);
		}

		private readonly SendIntervalStatistics _plcSendStatistics = new SendIntervalStatistics();

		Thread doReadCount;

		public bool ConnectModbus()
		{
			try
			{
				plc.IpAddress = _Config.PlcIP;
				plc.Port = _Config.PlcPort;

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
			try
			{
				_plcSendStatistics.Stop();
				plc.ConnectClose();
				plcState = false;
				toolClass.SaveLog($"关闭PLC连接...");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"关闭PLC时错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void ReadGetTrigger()
		{
			try
			{

				string path = "DB1000.DBW0";
				short val = 0;
				//string path = _Config.gt_DataValid.ToString();
				//toolClass.SaveLog($"触发地址：{path}");
				while (true)
				{

					Thread.Sleep(50);
					//toolClass.SaveLog(plcState + "状态");
					if (!plcState) continue;

					short test = Convert.ToInt16(plc.ReadInt16("DB1000.DBW0").Content);
						
					if (test == 1)
					{
						if (triggerWatch.IsRunning) { long trigMs = triggerWatch.ElapsedMilliseconds; if (trigMs > 500) toolClass.SaveLog("触发间隔偏长: " + trigMs + "ms"); triggerWatch.Restart(); } else triggerWatch.Start();
						EventTriggerGet();
						Thread.Sleep(50);
						plc.Write("DB1000.DBW0", val);
						toolClass.SaveLog($"写零后读取{plc.ReadInt16(path).Content}");
					}
				}
			}
			catch (Exception ex)
			{
				plcState = false;
				EventConnectState(false, $"读触发信号时出现异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}


		private void WriteKeepAlive()
		{
			try
			{
				short val = 1;
				while (true)
				{
					Thread.Sleep(500);

					if (plcState)
					{
						plc.Write("DB1000.DBW78", val);
					}
				}

			}
			catch (Exception ex)
			{
				plcState = false;
				EventConnectState(false, $"向PLC写心跳时发生错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void DoStateMethod()
		{
			timeOut.Start();
			short oldVal = 0;
			try
			{
				while (true)
				{
					Thread.Sleep(50);
					if (plcState)
					{
						short newVal = plc.ReadInt16("DB1000.DBW78").Content;
						if (oldVal != newVal)
						{
							Console.WriteLine($"状态变了 之前{oldVal} 现在{newVal}");
							oldVal = newVal;
							//Console.WriteLine(timeOut.ElapsedMilliseconds);
							timeOut.Restart();

							Console.WriteLine($"状态更新后 时间清空了{timeOut.ElapsedMilliseconds}");
						}

						if (timeOut.ElapsedMilliseconds > 10000)
						{
							Console.WriteLine($"超出十秒状态没有更新了 时间：{timeOut.ElapsedMilliseconds}ms");
							plcState = false;
							EventConnectState(false, $"心跳状态超十秒未更新，判定为通讯断开状态，最后一次为[{newVal}]");
							try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug("S7-1200心跳超时, 最后值=" + newVal + ""); } catch { }
						}
					}
				}
			}
			catch (Exception ex)
			{
				plcState = false;
				EventConnectState(false, $"向PLC写心跳时发生错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
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
				if (plcState)
				{
					short ok = 1;
					plc.Write("DB1000.DBX72.4", true);
					plc.Write("DB1000. DBW88", ok);

				}
			}
			catch (Exception ex)
			{
				plcState = false;
				EventConnectState(false, $"RuningMethod发生错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
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

				short ok = 1;
				short ng = 2;
				var sw = Stopwatch.StartNew();

				plc.Write("DB1000.DBW80", result1 ? ok : ng);
				plc.Write("DB1000.DBW82", result2 ? ok : ng);
				plc.Write("DB1000.DBW84", result3 ? ok : ng);
				plc.Write("DB1000.DBW86", ok);

				long interval = _plcSendStatistics.RecordSend();
				if (interval > 0)
				{
					toolClass.SaveLog($"每组结果发送间隔: {interval}ms");
					if (_plcSendStatistics.GetStatistics().ValidCount % 10 == 0)
					{
						var stats = _plcSendStatistics.GetStatistics();
						toolClass.SaveLog($"发送间隔统计（每10次）: {stats}");
					}
				}
				toolClass.SaveLog($"写入结果完成，耗时：{sw.ElapsedMilliseconds}ms，结果：r1={(result1?ok:ng)}、r2={(result2?ok:ng)}、r3={(result3?ok:ng)}");
					if (sw.ElapsedMilliseconds > 50) { try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug("S7-1200写入耗时偏高: " + sw.ElapsedMilliseconds + "ms"); } catch { } }
				return true;
			}
			catch (Exception ex)
			{
				plcState = false;
				EventConnectState(false, $"向PLC写入结果时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return false;
			}
		}

		private void DoReadCount()
		{
			try
			{
				uint count1 = 0, count2 = 0, count3 = 0, count4 = 0, count5 = 0;
				toolClass.SaveLog("读PLC计数开始");

				while (true)
				{
					Thread.Sleep(10);
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
			}
			catch (Exception ex)
			{
				plcState = false;
				EventConnectState(false, $"读PLC计数错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
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
				while (!plcState)
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
