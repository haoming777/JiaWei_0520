using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HslCommunication;
using HslCommunication.ModBus;
using static CommonLib.Class_Config;
using XL.Tool;
using System.Text.RegularExpressions;
using System.Diagnostics;
using HslCommunication.Profinet.Inovance;

namespace PLC调试.Class
{
	public class HCModbusClass
	{

		Thread doKeepAlive;        // 心跳

		Thread doState;

		Thread doReadT1;

		Thread doReadCount;

		Stopwatch timeOut;

		private volatile bool _disposed = false;

	public HCModbusClass()
		{
			try
			{
				timeOut = new Stopwatch();

				doState = new Thread(new ThreadStart(DoStateMethod));
				doState.IsBackground = true;

				doKeepAlive = new Thread(new ThreadStart(WriteKeepAlive));
				doKeepAlive.IsBackground = true;

				doReadCount = new Thread(new ThreadStart(DoReadCount));
				doReadCount.IsBackground = true;

				doKeepAlive.Start();
				doState.Start();
				toolClass.SaveLog("doReadCount.Start");
				doReadCount.Start();
				toolClass.SaveLog("doReadCount.Start完成");
				toolClass.SaveLog("Modbus初始化完成");

				_plcSendStatistics.Start();
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"Modbus初始化错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}

		}

		private readonly SendIntervalStatistics _plcSendStatistics = new SendIntervalStatistics();

		XLToolClass toolClass = new XLToolClass();

		public InovanceTcpNet modbusTcp = new InovanceTcpNet();
		//OperateResult connectState;
		public bool modbusState = false;

		public event PlcConnectStateHandler EventConnectState;

		public event PlcCountHandler EventCount;

		/// <summary>
		/// 相机一触发信号
		/// </summary>
		public delegate void DelegateTriggerGet();
		public event DelegateTriggerGet EventTriggerGet;
		public bool ConnectModbus()
		{
			try
			{
				modbusTcp?.ConnectClose();
				modbusTcp = new InovanceTcpNet(InovanceSeries.AM, _Config.ModbusIP, _Config.ModbusPort, 1);
				modbusTcp.AddressStartWithZero = true;
				modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
				modbusTcp.IsStringReverse = true;
				modbusTcp.ConnectTimeOut = 2000; // P1: reduced from 5000

				OperateResult connectState = modbusTcp.ConnectServer();
				modbusState = connectState.IsSuccess;
				if (connectState.IsSuccess)
				{

					timeOut.Restart();
					errorCount = 0;
					EventConnectState(true, "Modbus连接成功");
				try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug("Modbus连接成功 IP=" + _Config.ModbusIP + ":" + _Config.ModbusPort + ""); } catch { }
					return true;
				}
				else
				{
					EventConnectState(false, "Modbus连接失败");
				try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug("Modbus连接失败 IP=" + _Config.ModbusIP + ":" + _Config.ModbusPort + ""); } catch { }
					return false;
				}

			}
			catch (Exception ex)
			{
				modbusState = false;
				EventConnectState(false, $"连接Modbus错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return false;
			}
		}

	public void Dispose()
		{
		if (_disposed) return;
		_disposed = true;
		try { modbusTcp?.ConnectClose(); } catch { }
		modbusState = false;
		_plcSendStatistics.Stop();
		}

	public void CloseModbus()
		{
			try
			{
				_plcSendStatistics.Stop();
				_disposed = true;
				modbusTcp.ConnectClose();
				modbusState = false;
				toolClass.SaveLog($"关闭Modbus连接...");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"关闭Modbus时错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}


		private void DoStateMethod()
		{
			timeOut.Start();
			uint oldVal = 0;
			try
			{
				while (!_disposed)
				{
					Thread.Sleep(50);
					if (modbusState)
					{
						uint newVal = modbusTcp.ReadUInt16($"{_Config.keepAlive}").Content;
						if (oldVal != newVal)
						{
							oldVal = newVal;
														timeOut.Restart();

						}

						//
						if (timeOut.ElapsedMilliseconds > 10000)
						{
							modbusState = false;
							EventConnectState(false, $"心跳状态超十秒未更新，判定为通讯断开状态，最后一次为[{newVal}]");
							try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug("Modbus心跳超时, 最后值=" + newVal + ", 超时ms=" + timeOut.ElapsedMilliseconds + ""); } catch { }
						}
					}
				}
			}
			catch (Exception ex)
			{
				modbusState = false;
				EventConnectState(false, $"向Modbus写心跳时发生错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}

		}



		private void WriteKeepAlive()
		{
			try
			{
				while (!_disposed)
				{
					Thread.Sleep(500);
					//toolClass.SaveLog($"modbusState: {modbusState}");
					if (modbusState)
					{
						//toolClass.SaveLog($"进来了: {modbusState}");
						//心跳
						var hbWr = modbusTcp.Write(_Config.keepAlive.ToString(), (short)1);
						if (!hbWr.IsSuccess)
							try { CommonLib.FastLogger.Instance.Warn("HC心跳写失败: " + hbWr.Message); } catch { }

					}
				}
			}
			catch (Exception ex)
			{
				modbusState = false;
				EventConnectState(false, $"向Modbus写心跳时发生错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}
		
		public void ClearCount()
		{
			try
			{
				if (modbusState)
				{
					modbusTcp.Write("MB10016", (short)1);

				}
			}
			catch (Exception ex)
			{
				modbusState = false;
				EventConnectState(false, $"ClearCount发生错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		public void RuningMethod()
		{
			try
			{
				try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Info("RuningMethod 开始发送运行信号..."); } catch { }
				// 等待 Modbus 连接就绪（最多重试 5 次，每次等 1 秒）
				const int MAX_RETRY = 5;
				for (int retry = 0; retry < MAX_RETRY; retry++)
				{
					if (modbusState) break;
					if (retry == 0)
						try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Warn(string.Format("RuningMethod: modbusState=false, 等待重连...({0}/{1})", retry + 1, MAX_RETRY)); } catch { }
					System.Threading.Thread.Sleep(1000);
				}
				if (!modbusState)
				{
					try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Error("RuningMethod: 等待超时, modbusState 仍为 false, 未发送运行信号"); } catch { }
					return;
				}

				try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Info("Modbus → MX7080.0=True"); } catch { }
				modbusTcp.Write("MX7080.0", true);
				// 回读验证
				try
				{
					bool rb = modbusTcp.ReadBool("MX7080.0").Content;
					try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Info(string.Format("Modbus 回读 → MX7080.0={0}", rb)); } catch { }
					if (!rb)
						try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Warn("Modbus RuningMethod 回读不一致! 期望:True"); } catch { }
				}
				catch { }
				try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Info("RuningMethod 运行信号发送完成"); } catch { }
			}
			catch (Exception ex)
			{
				modbusState = false;
				EventConnectState(false, string.Format("RuningMethod发生错误...\r\n {0} \r\n {1}", ex.Message, ex.StackTrace));
			}
		}

		int errorCount = 0;
		private void DoReadCount()
		{
			try
			{
				uint count1 = 0;
				uint count2 = 0;
				uint count3 = 0;
				uint count4 = 0;
				uint count5 = 0;
				toolClass.SaveLog($"读PLC计数开始");

				toolClass.SaveLog($"modbusState为{modbusState}");
				toolClass.SaveLog($"EventCount为{(EventCount == null ? "null" : "正常")}");

				while (!_disposed)
				{
					Thread.Sleep(100);
					if (modbusState)
					{
						count1 = modbusTcp.ReadUInt32($"MB10100").Content;
						count2 = modbusTcp.ReadUInt32($"MB10104").Content;
						count3 = modbusTcp.ReadUInt32($"MB10108").Content;
						count4 = modbusTcp.ReadUInt32($"MB10112").Content;
						count5 = modbusTcp.ReadUInt32($"MB10116").Content;

						if (EventCount != null)
						{
							EventCount(count1, count2, count3, count4, count5);
							//toolClass.SaveLog($"\r\ncount1:{count1},\r\ncount2:{count2},\r\ncount3:{count3},\r\ncount4:{count4},\r\ncount5:{count5}");
						}
						else
						{
							toolClass.SaveLog("EventCount为null");
						}
					}
					else
					{
						errorCount++;
						if (errorCount == 0)
						{
							toolClass.SaveLog($"modbusState为{modbusState}");

						}
					}
				}
			}
			catch (Exception ex)
			{
				modbusState = false;
				EventConnectState(false, $"向Modbus写心跳时发生错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		public bool WriteResult(bool result1, bool result2, bool result3)
		{
			try
			{
				short ok = 1;
				short ng = 2;
				short v1 = result1 ? ok : ng;
				short v2 = result2 ? ok : ng;
				short v3 = result3 ? ok : ng;
				try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug($"Modbus → MB10008={v1} MB10010={v2} MB10012={v3} MB10014={ok}"); } catch { }

				Stopwatch sw = new Stopwatch();

				if (!modbusState)
				{
					toolClass.SaveLog($"WriteResult写入结果时，modbusState状态为：{modbusState}");
					return false;
				}

				sw.Restart();
				// 4路信号并行写入，以 Write 返回的 IsSuccess（Modbus TCP 协议级应答）确认写入成功。
				// 【已移除回读验证】PLC 收到 1 后立即处理并置 0，以太网往返延迟下回读经常读到
				// 已被 PLC 置 0 的值，造成大量"回读不一致"误报；且每次回读多一次网络往返徒增耗时
				System.Threading.Tasks.Parallel.Invoke(
					() =>
					{
						try
						{
							var wr = modbusTcp.Write("MB10008", v1);
							if (!wr.IsSuccess)
								try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Warn($"Modbus MB10008 写入失败! {wr.Message}"); } catch { }
						}
						catch (Exception ex)
						{
							try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Error($"Modbus MB10008 写入异常: {ex.Message}"); } catch { }
						}
					},
					() =>
					{
						try
						{
							var wr = modbusTcp.Write("MB10010", v2);
							if (!wr.IsSuccess)
								try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Warn($"Modbus MB10010 写入失败! {wr.Message}"); } catch { }
						}
						catch (Exception ex)
						{
							try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Error($"Modbus MB10010 写入异常: {ex.Message}"); } catch { }
						}
					},
					() =>
					{
						try
						{
							var wr = modbusTcp.Write("MB10012", v3);
							if (!wr.IsSuccess)
								try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Warn($"Modbus MB10012 写入失败! {wr.Message}"); } catch { }
						}
						catch (Exception ex)
						{
							try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Error($"Modbus MB10012 写入异常: {ex.Message}"); } catch { }
						}
					},
					() =>
					{
						try
						{
							var wr = modbusTcp.Write("MB10014", ok);
							if (!wr.IsSuccess)
								try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Warn($"Modbus MB10014 写入失败! {wr.Message}"); } catch { }
						}
						catch (Exception ex)
						{
							try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Error($"Modbus MB10014 写入异常: {ex.Message}"); } catch { }
						}
					}
				);

				// 记录发送间隔
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
						"写入结果完成，耗时：{0}ms，MB10008={1} MB10010={2} MB10012={3} MB10014={4}",
						sw.ElapsedMilliseconds, v1, v2, v3, ok));
			}
			catch { }
				if (sw.ElapsedMilliseconds > 50) { try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug("Modbus写入耗时偏高: " + sw.ElapsedMilliseconds + "ms"); } catch { } }
				return true;
			}
			catch (Exception ex)
			{
				modbusState = false;
				EventConnectState(false, string.Format("向Modbus写入结果时发生异常...\r\n {0} \r\n {1}", ex.Message, ex.StackTrace));
				return false;
			}
		}




		bool bRunning = false;
		int ReconnectCount = 0;

		public void Reconnect()
		{
			if (bRunning) return;
			toolClass.SaveLog("尝试重新连接Modbus");
			Task.Run(() =>
			{
				bRunning = true;
				while (!modbusState)
				{
					ReconnectCount++;
					toolClass.SaveLog($"正在尝试第 {ReconnectCount} 次重连");
					ConnectModbus();
					Thread.Sleep(1000);
				}
				bRunning = false;
				toolClass.SaveLog($"在第 {ReconnectCount} 次时重连成功");
				try { if (CommonLib.FastLogger.IsInitialized) CommonLib.FastLogger.Instance.Debug("Modbus重连成功, 尝试" + ReconnectCount + "次"); } catch { }
				ReconnectCount = 0;
			});
		}

	}

	/// <summary>
	/// 发送间隔统计器
	/// </summary>
	public class SendIntervalStatistics
	{
		private readonly Stopwatch _stopwatch;
		private readonly List<long> _intervals;
		private readonly object _lockObj;

		/// <summary>
		/// 上一次发送的时间戳
		/// </summary>
		private long _lastSendTime;

		/// <summary>
		/// 是否已开始计时
		/// </summary>
		private bool _isStarted;

		/// <summary>
		/// 异常间隔阈值（5秒），超过此值不记录
		/// </summary>
		private const long ABNORMAL_INTERVAL_THRESHOLD = 5000; // 5秒，单位毫秒

		/// <summary>
		/// 构造函数
		/// </summary>
		public SendIntervalStatistics()
		{
			_stopwatch = new Stopwatch();
			_intervals = new List<long>();
			_lockObj = new object();
			_lastSendTime = 0;
			_isStarted = false;
		}

		/// <summary>
		/// 开始计时（在程序启动或开始新一轮统计时调用）
		/// </summary>
		public void Start()
		{
			lock (_lockObj)
			{
				_stopwatch.Restart();
				_intervals.Clear();
				_lastSendTime = 0;
				_isStarted = true;
			}
		}

		/// <summary>
		/// 记录一次发送
		/// </summary>
		/// <returns>本次间隔时间（毫秒），如果超过阈值返回-1</returns>
		public long RecordSend()
		{
			if (!_isStarted)
			{
				Start();
				return 0;
			}

			lock (_lockObj)
			{
				long currentTime = _stopwatch.ElapsedMilliseconds;

				// 如果是第一次发送，只记录时间点
				if (_lastSendTime == 0)
				{
					_lastSendTime = currentTime;
					return 0;
				}

				// 计算本次间隔
				long interval = currentTime - _lastSendTime;

				// 更新上一次发送时间
				_lastSendTime = currentTime;

				// 检查是否超过阈值（5秒）
				if (interval > ABNORMAL_INTERVAL_THRESHOLD)
				{
					// 超过5秒不记录，但可以记录日志
					// 例如：Log.Warning($"发送间隔异常：{interval}ms，超过阈值{ABNORMAL_INTERVAL_THRESHOLD}ms");
					return -1;
				}

				// 记录正常间隔
				_intervals.Add(interval);
				return interval;
			}
		}

		/// <summary>
		/// 停止计时
		/// </summary>
		public void Stop()
		{
			lock (_lockObj)
			{
				_stopwatch.Stop();
				_isStarted = false;
			}
		}

		/// <summary>
		/// 获取统计结果
		/// </summary>
		/// <returns>统计结果对象</returns>
		public StatisticsResult GetStatistics()
		{
			lock (_lockObj)
			{
				if (_intervals.Count == 0)
				{
					return new StatisticsResult
					{
						CurrentInterval = 0,
						MaxInterval = 0,
						MinInterval = 0,
						AverageInterval = 0,
						TotalCount = 0,
						ValidCount = 0
					};
				}

				long currentInterval = _intervals.Count > 0 ? _intervals.Last() : 0;
				long maxInterval = _intervals.Max();
				long minInterval = _intervals.Min();
				double averageInterval = _intervals.Average();

				return new StatisticsResult
				{
					CurrentInterval = currentInterval,
					MaxInterval = maxInterval,
					MinInterval = minInterval,
					AverageInterval = averageInterval,
					TotalCount = _intervals.Count,
					ValidCount = _intervals.Count
				};
			}
		}

		/// <summary>
		/// 重置统计
		/// </summary>
		public void Reset()
		{
			lock (_lockObj)
			{
				_intervals.Clear();
				_lastSendTime = 0;
				_isStarted = false;
				_stopwatch.Reset();
			}
		}

		/// <summary>
		/// 获取所有记录的间隔（用于调试）
		/// </summary>
		/// <returns>间隔列表</returns>
		public List<long> GetAllIntervals()
		{
			lock (_lockObj)
			{
				return new List<long>(_intervals);
			}
		}
	}

	/// <summary>
	/// 统计结果类
	/// </summary>
	public class StatisticsResult
	{
		/// <summary>
		/// 本次间隔（毫秒）
		/// </summary>
		public long CurrentInterval { get; set; }

		/// <summary>
		/// 最大间隔（毫秒）
		/// </summary>
		public long MaxInterval { get; set; }

		/// <summary>
		/// 最小间隔（毫秒）
		/// </summary>
		public long MinInterval { get; set; }

		/// <summary>
		/// 平均间隔（毫秒）
		/// </summary>
		public double AverageInterval { get; set; }

		/// <summary>
		/// 总记录次数
		/// </summary>
		public int TotalCount { get; set; }

		/// <summary>
		/// 有效记录次数（小于5秒的记录）
		/// </summary>
		public int ValidCount { get; set; }

		/// <summary>
		/// 转换为字符串表示
		/// </summary>
		/// <returns>格式化字符串</returns>
		public override string ToString()
		{
			return $"当前间隔: {CurrentInterval}ms, " +
				   $"最大间隔: {MaxInterval}ms, " +
				   $"最小间隔: {MinInterval}ms, " +
				   $"平均间隔: {AverageInterval:F2}ms, " +
				   $"有效记录: {ValidCount}次";
		}
	}

	/// <summary>
	/// 示例使用方式（在MainFrm中的集成）
	/// </summary>
	public class MainFrmExample
	{
		// 在类中声明统计器
		private readonly SendIntervalStatistics _sendStatistics = new SendIntervalStatistics();

		// 在初始化方法中启动统计
		public void Initialize()
		{
			// ... 其他初始化代码

			// 启动间隔统计
			_sendStatistics.Start();
		}

		// 在发送方法中记录
		public void SendToPLC()
		{
			try
			{
				// ... 发送PLC的代码

				// 记录本次发送
				long interval = _sendStatistics.RecordSend();

				if (interval > 0)
				{
					// 正常间隔，可以记录到日志
					// Log.Info($"发送PLC成功，本次间隔: {interval}ms");

					// 可选：实时输出统计信息
					var stats = _sendStatistics.GetStatistics();
					// Log.Info($"发送间隔统计: {stats}");
				}
				else if (interval == -1)
				{
					// 间隔超过5秒
					// Log.Warning("发送间隔超过5秒，不记录统计");
				}
			}
			catch (Exception ex)
			{
				// Log.Error($"发送PLC失败: {ex.Message}");
			}
		}

		// 在程序结束时输出最终统计
		public void OnApplicationExit()
		{
			var finalStats = _sendStatistics.GetStatistics();


			// 记录到日志文件
			// Log.Info($"发送间隔最终统计: {finalStats}");

			_sendStatistics.Stop();
		}

		// 添加一个方法用于实时查看统计
		public void DisplayCurrentStatistics()
		{
			var stats = _sendStatistics.GetStatistics();
		}
	}
}
