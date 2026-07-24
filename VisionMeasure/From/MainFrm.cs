using AIsdk;
using Cognex.VisionPro.ToolBlock;
using CommonLib;
using Dahua.LConv;
using HslCommunication;
using HslCommunication.Profinet.Siemens.S7PlusHelper;
using HZH_Controls;
using MT.Camera.SDK;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using OpenCvSharp.Extensions;
using PLCMonitor;
using PLC调试.Class;
using RoundnessDetection1;
using RoundnessDetectionV3;
using SmartMore.ViMo;
using Sunny.UI;
using Sunny.UI.Win32;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using VisionMeasure.Class;
using VisionMeasure.From;
using XL.Controls;
using XL.UsbDog;
using static CommonLib.Class_Config;
using static CommonLib.takephotoVm;
using static cszmcaux.zmcaux;
using static HZH_Controls.MouseHook;
using static MT.Camera.SDK.Common;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

namespace VisionMeasure
{
	// ==================== 主窗体类123 ====================

	public partial class MainFrm : Form, ICamera
	{
		// 非托管 API — 锁定进程工作集防 OS 换出
		[DllImport("kernel32.dll")]
		private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, int minSize, int maxSize);

		// 线程安全关闭标志
		private volatile bool _isClosing = false;
		// 相机重连去抖：同一相机只允许一个重连 Task 在跑
		private volatile bool _cam1Reconnecting, _cam2Reconnecting, _cam3Reconnecting, _cam4Reconnecting, _cam5Reconnecting;
		private readonly ManualResetEventSlim _closeWaitHandle = new ManualResetEventSlim(false);
		private readonly object _closeLock = new object();

		// 工位启用状态（程序启动时读一次 INI，运行期不变）
		private bool[] _cameraEnabled; // [0]=Cam1, [1]=Cam2, ...

		public Vision vision = new Vision();
		public IPlcCommunication modbusClass; // 运行时根据配置选择 S7-1200 或 HCModbus
		XLUsbDogClass UsbDogClass = new XLUsbDogClass();

		private AsyncDatabaseRecorder _dbRecorder;
		private DiskGuardService _diskGuard;
		internal static volatile bool SaveOkAndRawPaused;
		internal static volatile bool SaveNgResultPaused;

		/// <summary>运行时日志开关（热路径诊断用，默认关闭。设为true可输出每帧详细日志）</summary>
		public static volatile bool RunLogEnabled = false;

		public DaHuaSDK camera1SDK, camera2SDK, camera3SDK, camera4SDK, camera5SDK;
		private ProgressBar progressBar1;
		public static string _path = Directory.GetCurrentDirectory();

		// 相机模型路径
		string modelpath_cam1 = _path + "\\AI\\Cam1\\model_trt_fp16.vimosln";
		bool UseGpu_cam1 = true;
		int deviceid_cam1 = 0;
		string modelId_char_cam1 = "3";

		string modelpath_cam2 = _path + "\\AI\\Cam2\\model_trt_fp16.vimosln";
		bool UseGpu_cam2 = true;
		int deviceid_cam2 = 0;
		string modelId_class_cam2 = "5";

		string modelpath_cam4 = _path + "\\AI\\Cam4\\model_trt_fp16.vimosln";
		bool UseGpu_cam4 = true;
		int deviceid_cam4 = 0;
		string modelId_segmentation_cam4 = "2";
		string modelId_char_cam4 = "3";

		string modelpath_cam5 = _path + "\\AI\\Cam5\\model_trt_fp16.vimosln";
		bool UseGpu_cam5 = true;
		int deviceid_cam5 = 0;
		string modelId_char_PCode_cam5 = "6";
		string modelId_char_cam5 = "5";
		string modelId_flaw_cam5 = "4";
		string modelId_color_cam5 = "3";
		string modelId_rests_cam5 = "3";
		string modelId_segmentation_cam5 = "2";

		// 静态资源定义


		private static readonly Font Font_Main = new Font("Microsoft YaHei", 30, FontStyle.Bold);
		private static readonly Font Font_Sub = new Font("Microsoft YaHei", 24, FontStyle.Bold);
		private ImageProcessor _processor1;
		private ImageProcessor _processor2;
		private ImageProcessor _processor3;
		private ImageProcessor _processor4;
		private ImageProcessor _processor5;
		private ResultMatcher _resultMatcher;

		private ImageBufferPool _bufferPool1;
		private ImageBufferPool _bufferPool2;
		private ImageBufferPool _bufferPool3;
		private ImageBufferPool _bufferPool4;
		private ImageBufferPool _bufferPool5;

		private DateTime _lastShiftCheckTime = DateTime.Now;
		private string _currentShift = "";
		private string _currentShiftDate = "";

		// SKU管理
		private string _savedSku = "";  // 已保存的SKU
		private bool _skuModified = false;  // SKU是否已修改但未保存


		// 性能监控
		private System.Timers.Timer _performanceTimer;
		private Dictionary<string, PerformanceStats> _performanceHistory;

		// 【性能诊断】PLC发送耗时统计
		private long _plcPerfSendCount;
		private long _plcPerfTotalMs;
		private long _plcPerfMaxMs;
		private long _plcPerfIntervalMaxMs; private Stopwatch _plcPerfIntervalWatch = new Stopwatch();
		private int _perfBriefCounter;   // 轻量报告周期计数器

		// 配置常量
		private const int MAX_QUEUE_SIZE = 500;
		private const int PERFORMANCE_UPDATE_INTERVAL = 5000;
		private const int IMAGE_JPEG_QUALITY = 85;

		// 原有字段...
		int temp_result1 = 0;
		int temp_result2 = 0;
		int temp_result = 0;
		bool result_char_top = false;
		bool result_char_btm = false;

		int inputCount1 = 0;
		int inputCount2 = 0;
		int inputCount3 = 0;
		int inputCount4 = 0;
		int inputCount5 = 0;

		int outputCount1 = 0;
		int outputCount2 = 0;
		int outputCount3 = 0;
		int outputCount4 = 0;
		int outputCount5 = 0;

		int resultCount1 = 0;
		int resultCount2 = 0;
		int resultCount3 = 0;
		int resultCount4 = 0;
		int resultCount5 = 0;

		int offset_cam1 = 0;
		int offset_cam2 = 0;
		int offset_cam3 = 0;
		int offset_cam4 = 0;
		int offset_cam5 = 0;
		int offset_send = 0;

		int cameraModel = 0;
		int writeNGCount = 0;
		bool IFSaveLog = false;

		double k = 0;
		double offset = 0;
		double astrict = 0;

		Vimo Model_Segmentation_Cam1 = new Vimo();
		Vimo Model_Class_Cam2 = new Vimo();
		Vimo Model_Segmentation_Cam4 = new Vimo();
		Vimo Model_Char_Cam4 = new Vimo();
		Vimo Model_Char_Cam5 = new Vimo();
		Vimo Model_Char_PCode_Cam5 = new Vimo();
		Vimo Model_Color_Cam5 = new Vimo();
		Vimo Model_Rests_Cam5 = new Vimo();
		Vimo Model_Segmentation_Cam5 = new Vimo();

		// 使用 CancellationTokenSource 管理线程
		private CancellationTokenSource _cts;
		private Thread updateThread;
		private Thread ReadIO1Thread;
		private Thread WriteResultThread;

		List<QueueResultItem> SendResultList = new List<QueueResultItem>();

		int minArea = 500;
		takephotoVm myZmcaux = new takephotoVm();
		public IntPtr g_handle = (IntPtr)0;

		public delegate void DelegatTriggerSignal(string timeStr);
		public event DelegatTriggerSignal EventTriggerSignal1;
		public event DelegatTriggerSignal EventTriggerSignal2;

		private HighSpeedImageSaver _highSpeedSaver1;
		private HighSpeedImageSaver _highSpeedSaver2;
		private HighSpeedImageSaver _highSpeedSaver3;
		private HighSpeedImageSaver _highSpeedSaver4;
		private HighSpeedImageSaver _highSpeedSaver5;

		private Stopwatch _saveTimer;
		private int _totalSaveCount;
		private long _totalSaveTimeMs;

		bool IFSaveOKImage = false;
		bool IFSaveNGImage = false;
		bool IFSaveNGRawImage = false;
		bool IFSaveOKRawImage = false;

		#region 输入输出
		int input_cam1 = -1;
		int input_cam2 = -1;
		int input_cam3 = -1;
		int input_cam4 = -1;
		int input_cam5 = -1;

		int output_cam1 = -1;
		int output_cam2 = -1;
		int output_cam3 = -1;
		int output_cam4 = -1;
		int output_cam5 = -1;

		int output_delay_cam1 = -1;
		int output_delay_cam2 = -1;
		int output_delay_cam3 = -1;
		int output_delay_cam4 = -1;
		int output_delay_cam5 = -1;
		#endregion

		#region 图像缓存（用于按缺陷类型存图）
		private ConcurrentDictionary<long, Dictionary<string, Mat>> _imageCache = new ConcurrentDictionary<long, Dictionary<string, Mat>>();
		private ConcurrentDictionary<long, Dictionary<string, Mat>> _resultImageCache = new ConcurrentDictionary<long, Dictionary<string, Mat>>();
		private ConcurrentDictionary<long, QueueResultItem[]> _pendingImageSaves = new ConcurrentDictionary<long, QueueResultItem[]>();

		// Task数组池化，避免每帧 new Task[]
		private readonly Task[] _cam4Tasks = new Task[2];
		private readonly Task[] _cam5Tasks = new Task[5];
		#endregion

		#region 光电信号
		uint IFCam1 = 100;
		uint IFCam2 = 100;
		uint IFCam3 = 100;
		uint IFCam4 = 100;
		uint IFCam5 = 100;
		#endregion

		#region 运控轴状态
		uint IFAuto = 0;
		int IFInit = 0;
		#endregion

		long camera1Count = 0, _imgRcvd1 = 0;
		long camera2Count = 0, _imgRcvd2 = 0;
		long camera3Count = 0, _imgRcvd3 = 0;
		long camera4Count = 0, _imgRcvd4 = 0;
		long camera5Count = 0, _imgRcvd5 = 0;
		long _cam4CallbackCount = 0, _cam5CallbackCount = 0;
		int _consecutiveC5NG = 0; // 同步爆管计数器
		int _localTotal = 0; // 本地计数器

		// 调试日志周期计数器（避免热路径每帧写日志）
		private long _debugMatchCount = 0;     // 结果匹配周期计数
		private long _debugPlcSendCount = 0;   // PLC发送周期计数

		// 【AI可靠性】各相机 AI 模型返回 null 的连续次数计数器（检测模型偶发异常）
		private long _aiNullCount_Cam1, _aiNullCount_Cam2, _aiNullCount_Cam4_Seg, _aiNullCount_Cam4_Ocr;
		private long _aiNullCount_Cam5_Seg, _aiNullCount_Cam5_Ocr, _aiNullCount_Cam5_Color, _aiNullCount_Cam5_PCode, _aiNullCount_Cam5_Rests;
		private const int AI_NULL_ALERT_THRESHOLD = 5; // 连续 null 超阈值记 Error 告警

		/// <summary>AI模型返回null时的计数与告警（保持当前判OK不变，仅追加监控）</summary>
		private void TrackAiNull(ref long counter, string camera, string modelName)
		{
			long c = Interlocked.Increment(ref counter);
			if (c <= 1 || c % AI_NULL_ALERT_THRESHOLD == 0)
				try { FastLogger.Instance.Error(string.Format("[AI-Null] {0} {1} 连续{2}次返回null", camera, modelName, c)); } catch { }
		}
		private void ResetAiNullCounter(ref long counter) { Interlocked.Exchange(ref counter, 0); }
		private const int DEBUG_LOG_INTERVAL = 100; // 每N次输出一次调试日志

		#region 构造函数和初始化
		public MainFrm()
		{
			try { FastLogger.Instance.Info("MainFrm 构造函数开始"); } catch { }

			InitializeComponent();

			// 【工位配置】启动时读一次，后续不再读 INI（防呆：有人改 INI 也不生效，必须重启）
			_cameraEnabled = new bool[5];
			_cameraEnabled[0] = _Config.ActiveCam1;
			_cameraEnabled[1] = _Config.ActiveCam2;
			_cameraEnabled[2] = _Config.ActiveCam3;
			_cameraEnabled[3] = _Config.ActiveCam4;
			_cameraEnabled[4] = _Config.ActiveCam5;
			// 防呆：至少一个工位启用
			int enabledCount = _cameraEnabled.Count(x => x);
			if (enabledCount == 0)
			{
				try { FastLogger.Instance.Warn("所有工位均已禁用！将使用默认配置（全部启用）"); } catch { }
				for (int i = 0; i < 5; i++) _cameraEnabled[i] = true;
			}
			try { FastLogger.Instance.Info("工位启用状态: " + string.Join(" ", _cameraEnabled.Select((b, i) => "Cam" + (i + 1) + "=" + b)) + " (" + _cameraEnabled.Count(e => e) + "/5 启用)"); } catch { }
			if (IFSaveLog) FastLogger.Instance.Info("[CamCfg] 工位启用状态: Cam1=" + _cameraEnabled[0] + " Cam2=" + _cameraEnabled[1] + " Cam3=" + _cameraEnabled[2] + " Cam4=" + _cameraEnabled[3] + " Cam5=" + _cameraEnabled[4] + " (启用" + _cameraEnabled.Count(e => e) + "路)");

			// 【内存】Server GC (每核独立堆并行回收) + 后台并发 GC 已在 App.config 中通过
			// <gcServer enabled="true"/> + <gcConcurrent enabled="true"/> 配置。
			// 运行时不再干预 GC 策略——曾设过 LatencyMode=Interactive + CompactOnce，但该组合
			// 在 .NET Framework 4.7.2 Server GC 下导致托管堆被压到 ~3MB 并触发每秒 6+ 次
			// 全代回收死循环（Gen0≈Gen1≈Gen2≈2400/8分钟）。不干预时 Server GC 会自动选择
			// 最佳的代预算和回收频率。
			try { FastLogger.Instance.Info("[内存] ServerGC=" + System.Runtime.GCSettings.IsServerGC + " (由App.config控制,运行时不再干预GC策略)"); } catch { }
			// 【新增优化】提升当前进程在操作系统中的优先级，确保多线程调度更稳定
			Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
			// 注意：SetProcessWorkingSetSize(h, -1, -1) 是裁剪工作集而非锁定，已移除
			System.Threading.ThreadPool.SetMinThreads(30, 30);

			_cts = new CancellationTokenSource();
			InitializePerformanceMonitoring();
			InitializeImageProcessors();
			InitializeHighSpeedSavers();
			InitializeMemoryPools();
			// 30秒定时刷新待写入数据库的记录
			_batchFlushTimer = new System.Timers.Timer(30000);
			_batchFlushTimer.Elapsed += (s, e) => { FlushPendingRecords(); _dbRecorder?.RefreshCurrentSummary(); };
			_batchFlushTimer.AutoReset = true;
			_batchFlushTimer.Start();

			InitializeDatabaseRecorder();

			// 【P2】磁盘监控服务：每60秒检查存图盘可用空间
			try { _diskGuard = new DiskGuardService(); _diskGuard.Start(); } catch { }

			// 绑定导出按钮事件
			if (exportBtn != null) exportBtn.Click += ExportBtn_Click;

			// 加载保存的SKU
			InitializeSavedSku();

			try { FastLogger.Instance.Info("MainFrm 构造函数完成"); } catch { }
		}

		/// <summary>
		/// 导出按钮点击事件
		/// </summary>
		private void ExportBtn_Click(object sender, EventArgs e)
		{
			ManualSaveCurrentShiftReport();
		}

		/// <summary>
		/// 初始化保存的SKU
		/// </summary>
		private void InitializeSavedSku()
		{
			try
			{
				string savedSku = _Config.LastSku;
				if (!string.IsNullOrEmpty(savedSku))
				{
					_savedSku = savedSku?.Trim() ?? "";
					_skuModified = false;
					if (SKU_Txt != null)
					{
						SKU_Txt.Text = _savedSku;
						SKU_Txt.Style = UIStyle.Green;
					}
					FastLogger.Instance.Debug($"已加载保存的SKU: {savedSku}");
				}
				else
				{
					// 本地没有SKU，显示红色提示用户需要输入
					if (SKU_Txt != null)
					{
						SKU_Txt.Style = UIStyle.Red;
					}
				}
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"初始化SKU异常: {ex.Message}");
			}
		}

		private void InitializeMemoryPools()
		{
			try
			{
				int width12 = 1440;
				int height12 = 1080;
				int width3 = 1280;
				int height3 = 1024;
				int width45 = 1624;
				int height45 = 1240;

				_bufferPool1 = _cameraEnabled[0] ? new ImageBufferPool(width12, height12, PixelFormat.Format8bppIndexed, 3, 10, 50 * 1024 * 1024) { PoolName = "Camera1_Pool" } : null;
				_bufferPool2 = _cameraEnabled[1] ? new ImageBufferPool(width12, height12, PixelFormat.Format8bppIndexed, 3, 10, 50 * 1024 * 1024) { PoolName = "Camera2_Pool" } : null;
				_bufferPool3 = _cameraEnabled[2] ? new ImageBufferPool(width3, height3, PixelFormat.Format8bppIndexed, 3, 10, 50 * 1024 * 1024) { PoolName = "Camera3_Pool" } : null;
				_bufferPool4 = _cameraEnabled[3] ? new ImageBufferPool(width45, height45, PixelFormat.Format8bppIndexed, 3, 10, 50 * 1024 * 1024) { PoolName = "Camera4_Pool" } : null;
				_bufferPool5 = _cameraEnabled[4] ? new ImageBufferPool(width45, height45, PixelFormat.Format8bppIndexed, 3, 10, 50 * 1024 * 1024) { PoolName = "Camera5_Pool" } : null;

				FastLogger.Instance.Debug("内存池初始化完成");
				try { FastLogger.Instance.Info("内存池初始化完成"); } catch { }
			}
			catch (Exception ex)
			{
				try { FastLogger.Instance.Error("内存池初始化失败", ex); } catch { }
				FastLogger.Instance.Error($"初始化内存池失败: {ex.Message}");
			}
		}

		private ImageBufferPool GetBufferPool(CameraSelect camera)
		{
			switch (camera)
			{
				case CameraSelect.Camera1: return _bufferPool1;
				case CameraSelect.Camera2: return _bufferPool2;
				case CameraSelect.Camera3: return _bufferPool3;
				case CameraSelect.Camera4: return _bufferPool4;
				case CameraSelect.Camera5: return _bufferPool5;
				default: return _bufferPool1;
			}
		}

		private void InitializeDatabaseRecorder()
		{
			try
			{
				_dbRecorder = new AsyncDatabaseRecorder();
				// 设置获取当前SKU的委托
				_dbRecorder.GetCurrentSku = () => GetCurrentSkuValue();
				_dbRecorder.OnRecordCommitted = (unifiedId) => OnDbRecordCommitted(unifiedId);
				_dbRecorder.OnBurstExcluded = () =>
				{
					// 同步计数已由ResultCountMethod处理，此处不再累加
					this.BeginInvoke(new Action(() =>
						{
							if (BaoGuanNGTxt != null) BaoGuanNGTxt.Text = _Config.burstExcludeCount.ToString();
							if (yieldTxt != null && _Config.total > 0)
							{
								double eff = _Config.total - _Config.burstExcludeCount;
								double yr = eff > 0 ? Math.Min(100.0, Math.Max(0.0, (_Config.ok * 100.0) / eff)) : 0;
								yieldTxt.Text = yr.ToString("F2") + "%";
								yieldTxt.ForeColor = yr > 95 ? Color.Green : yr > 90 ? Color.Orange : Color.Red;
							}
						}));
				};
				_dbRecorder.OnSummaryRefreshed = (total, ok, exclude) => { };
				FastLogger.Instance.Debug("异步数据库记录器初始化完成");
				try { FastLogger.Instance.Info("异步数据库记录器初始化完成"); } catch { }
			}
			catch (Exception ex)
			{
				try { FastLogger.Instance.Error("数据库记录器初始化失败", ex); } catch { }
				FastLogger.Instance.Error($"初始化数据库记录器失败: {ex.Message}");
			}
		}

		private string GetCurrentSkuValue()
		{
			// 【P1】热路径优化：直接返回缓存值，不再同步 Invoke UI 线程
			// _savedSku 是 Enter 确认后保存的权威值，运行期间 SKU 极少变化
			if (!string.IsNullOrEmpty(_savedSku))
				return _savedSku;
			// 兜底：SKU 未初始化时从控件读取（仅启动时）
			try { string t = SKU_Txt?.Text?.Trim(); if (!string.IsNullOrEmpty(t)) return t; } catch { }
			return "";
		}

		/// <summary>
		/// SKU输入框Enter键处理 - 保存SKU并清空统计数据
		/// </summary>
		private void SKU_Txt_Enter(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Enter)
			{
				try
				{
					// 【修复】必须读控件当前文本，不能用 GetCurrentSkuValue()（它优先返回 _savedSku 缓存，
					// 导致 _savedSku != currentSku 永远不成立，SKU 更新和统计清零功能失效）
					string currentSku = SKU_Txt?.Text?.Trim() ?? "";

					if (currentSku.Length < 6 || currentSku.Length > 10)
					{
						if (!string.IsNullOrEmpty(_savedSku) && SKU_Txt != null)
							SKU_Txt.Text = _savedSku;
						SetSkuTextBoxBorderColor(UIStyle.Red);
						MessageBox.Show($"SKU长度必须为6~10位！当前为{currentSku.Length}位，请检查！\r\n已恢复为上次保存的SKU。");
						return;
					}


					// 检查SKU是否发生变化
					if (_savedSku != currentSku)
					{
						// 防抖：SKU没真正变化不重置
						if (string.IsNullOrEmpty(_savedSku) && string.IsNullOrEmpty(currentSku))
						{
							e.SuppressKeyPress = true;
							return;
						}

						// SKU切换：先自动保存上一个SKU的班次报表（仅汇总表）
						if (!string.IsNullOrEmpty(_savedSku) && _dbRecorder != null)
						{
							_dbRecorder.ExportFullShiftReport(_currentShiftDate, _currentShift, skipDetailExport: true);
							FastLogger.Instance.Debug($"SKU切换: {_savedSku} -> {currentSku}，已自动保存{_currentShift}班次汇总报表");
						}

						// SKU发生变化，清空统计数据
						ClearStatisticsDisplay();
						_savedSku = currentSku;
						_skuModified = false;

						// 保存到配置文件
						_Config.LastSku = currentSku;

						// 设置边框为绿色
						SetSkuTextBoxBorderColor(UIStyle.Green);

						FastLogger.Instance.Info($"SKU已更新: {currentSku}，统计数据已清空");
					}
				}
				catch (Exception ex)
				{
					FastLogger.Instance.Error($"SKU保存异常: {ex.Message}");
				}
				e.SuppressKeyPress = true;
			}
		}

		/// <summary>
		/// SKU输入框文本变化处理 - 设置边框颜色
		/// 规则：
		/// - 本地没有SKU时显示红色
		/// - 输入框内容与本地SKU不同时显示黄色
		/// - 输入框内容与本地SKU相同时显示绿色
		/// </summary>
		private void SKU_Txt_TextChanged(object sender, EventArgs e)
		{
			try
			{
				// 【修复】同 SKU_Txt_Enter：读控件当前文本做比较，用缓存值会导致永远"相同"、边框颜色提示失效
				string currentSku = SKU_Txt?.Text?.Trim() ?? "";

				// 本地没有SKU时显示红色
				if (string.IsNullOrEmpty(_savedSku))
				{
					_skuModified = true;
					SetSkuTextBoxBorderColor(UIStyle.Red);
				}
				// 输入框内容与本地SKU不同时显示黄色
				else if (currentSku != _savedSku)
				{
					_skuModified = true;
					SetSkuTextBoxBorderColor(UIStyle.Orange);
				}
				else
				{
					// 相同则显示绿色
					_skuModified = false;
					SetSkuTextBoxBorderColor(UIStyle.Green);
				}
			}
			catch { }
		}

		/// <summary>
		/// 设置SKU输入框边框颜色
		/// </summary>
		private void SetSkuTextBoxBorderColor(UIStyle color)
		{
			try
			{
				if (SKU_Txt != null)
				{
					SKU_Txt.Style = color;
				}
			}
			catch { }
		}
		private void InitializeHighSpeedSavers()
		{
			try
			{
				_highSpeedSaver1 = _cameraEnabled[0] ? new HighSpeedImageSaver("Camera1", 1, 500) : null;
				_highSpeedSaver2 = _cameraEnabled[1] ? new HighSpeedImageSaver("Camera2", 1, 500) : null;
				_highSpeedSaver3 = _cameraEnabled[2] ? new HighSpeedImageSaver("Camera3", 1, 500) : null;
				_highSpeedSaver4 = _cameraEnabled[3] ? new HighSpeedImageSaver("Camera4", 1, 500) : null;
				_highSpeedSaver5 = _cameraEnabled[4] ? new HighSpeedImageSaver("Camera5", 1, 500) : null;

				_saveTimer = Stopwatch.StartNew();
				_totalSaveCount = 0;
				_totalSaveTimeMs = 0;

				FastLogger.Instance.Debug("高性能图像保存器初始化完成");
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"初始化高性能保存器失败: {ex.Message}");
			}
		}

		private const int MAX_IMAGE_CACHE_SIZE = 50; // 最多同时缓存50组图像（约600MB），超限时强制清理旧条目
		private const long MEMORY_WARN_THRESHOLD = 4000; // Server GC每核独立堆天然占更多内存，旧值2500MB持续触发误驱逐→GC风暴
		private const long MEMORY_CRITICAL_THRESHOLD = 4500; // 旧值2800MB→Server GC下正常运行就2600-2700，一直触发紧急清空→存图竞态
		private long _cacheEvictCount = 0;
		private int _memCheckCounter = 0;  // 内存检查计数器（替代 DateTime.Now.Second % 30 的不精确判断）

		// 系统资源仪表（每1分钟随耗时报告输出）
		private long _lastSysCpuTicks;      // 上次采样时的 DateTime.UtcNow.Ticks
		private long _lastSysCpuTimeTicks;  // 上次采样时的进程 CPU 时间 Ticks
		private long _lastGpuQueryTicks;    // 上次 GPU 查询时刻（60秒缓存）
		private long _gpuMemoryMb;          // GPU 显存占用量 MB（0=不可用/未查询到）
		private long _gpuUtilPct;           // GPU 利用率 %（0=不可用/未查询到）
		private int _lastGc2Count;           // 上次报告时的 GC Gen2 累计次数（用于计算本分钟增量）
		private MinuteSnapshot _prevMetrics; // 上一分钟的指标快照（趋势对比用）
		private bool _wasActive;              // 上一分钟是否有生产活动（切换时记一笔）
		private bool _hasEverReported;        // 是否已输出过至少一份报告（首份闲置不拦截以确认存活）
		private int _hourlyCounter;            // 报告计数(每60份≈每小时), 输出累计快照
		private long _startupWorkingSet;       // 首次报告的 WorkingSet 基线
		private int _startupThreadCount;       // 首次报告的线程数基线
		private int _startupHandleCount;       // 首次报告的句柄数基线

		private void InitializePerformanceMonitoring()
		{
			_performanceHistory = new Dictionary<string, PerformanceStats>();
			_performanceTimer = new System.Timers.Timer(PERFORMANCE_UPDATE_INTERVAL);
			_performanceTimer.Elapsed += OnPerformanceTimerElapsed;
			_performanceTimer.AutoReset = true;
			_performanceTimer.Start();
		}

		private void OnPerformanceTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
		{
			if (_isClosing) return;
			try
			{
				// 每1分钟输出耗时汇总（Timer间隔5秒 → 12次）
				// 【重构】原"5分钟重置+5分钟详细报告"两个计数器与本计数器同拍触发，
				// 重置总是先于详细报告执行，导致详细报告读到刚清零的空数据（全天空报告）。
				// 现统一为每分钟一份多行完整报告（含各阶段耗时），输出后立即清零，批间独立统计
				if (++_perfBriefCounter >= 12)
				{
					_perfBriefCounter = 0;
					ReportPerformanceBrief();
				}

				// 每30秒检查内存压力（Timer间隔5秒 → 6次）
				if (++_memCheckCounter >= 6)
				{
					_memCheckCounter = 0;
					CheckMemoryPressure();
				}
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"性能监控异常: {ex.Message}");
			}
		}

		/// <summary>
		/// 每分钟耗时汇总（多行格式）：各相机总耗时 + 全部阶段 Min/Max/Avg + PLC发送 + 内存。
		/// 输出后立即清零全部统计，保证每一批 Min/Max/Avg 都是本分钟内的独立数据（批间互不影响）。
		/// 全部相机无帧（待机）时压缩为单行简报，避免刷屏。
		/// </summary>
		private void ReportPerformanceBrief()
		{
			try
			{
				string csvTs = DateTime.Now.ToString("HH:mm:ss");
				var csvRows = new List<string>();
				var processors = new[] { _processor1, _processor2, _processor3, _processor4, _processor5 };

				long plcCnt = Interlocked.Read(ref _plcPerfSendCount);
				string plcAvg = plcCnt > 0 ? (Interlocked.Read(ref _plcPerfTotalMs) / plcCnt).ToString() : "-";
				long plcMax = Interlocked.Read(ref _plcPerfMaxMs);
				long plcGap = Interlocked.Read(ref _plcPerfIntervalMaxMs);

				bool anyFrames = false;
				foreach (var proc in processors)
				{ if (proc != null && proc.Performance.ProcessCount > 0) { anyFrames = true; break; } }

				// ──── 闲置门控：产线停工时跳过报告，只在"运行→待机"切换时记一笔 ────
				if (!anyFrames)
				{
					if (_wasActive) // 刚停：记一笔，然后放行这一份闲置报告（以后就静默了）
					{
						_wasActive = false;
						try { FastLogger.Instance.Info($"[{DateTime.Now:HH:mm}] 生产线停止，进入待机"); } catch { }
					}
					else if (_hasEverReported) // 持续闲置且已出过至少一份报告 → 完全静默
					{
						ResetPerformanceStats();
						Interlocked.Exchange(ref _plcPerfSendCount, 0);
						Interlocked.Exchange(ref _plcPerfTotalMs, 0);
						Interlocked.Exchange(ref _plcPerfMaxMs, 0);
						Interlocked.Exchange(ref _plcPerfIntervalMaxMs, 0);
						_prevMetrics = null;
						return;
					}
				}
				else { _wasActive = true; }
				_hasEverReported = true;

				// ──── 提前采集一次系统资源指标（两边分支共用，避免重复计算导致 CPU%=0）────
				long sysTicks = DateTime.UtcNow.Ticks;
				long sysCpuTicks = Process.GetCurrentProcess().TotalProcessorTime.Ticks;
				double cpuPct = 0;
				if (_lastSysCpuTicks > 0 && sysTicks > _lastSysCpuTicks)
					cpuPct = (double)(sysCpuTicks - _lastSysCpuTimeTicks) / (sysTicks - _lastSysCpuTicks) * 100.0 / Environment.ProcessorCount;
				_lastSysCpuTicks = sysTicks; _lastSysCpuTimeTicks = sysCpuTicks;
				QueryGpuInfo();
				long gMem = Interlocked.Read(ref _gpuMemoryMb);
				long gUtil = Interlocked.Read(ref _gpuUtilPct);
				int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
				var sb = new StringBuilder();
				using (var sysProc = Process.GetCurrentProcess())
				{
					long ws = sysProc.WorkingSet64 / 1024 / 1024;
					long pm = sysProc.PrivateMemorySize64 / 1024 / 1024;
					long gcHeap = GC.GetTotalMemory(false) / 1024 / 1024;
					int thrCnt = sysProc.Threads.Count;
					int hdlCnt = sysProc.HandleCount;
					string gpuPart = (gMem > 0 || gUtil > 0) ? string.Format("GPU显存:{0}MB 利用率:{1}% | ", gMem, gUtil) : "";
					string sysLine = string.Format("[系统资源] CPU:{0:F1}% | {1}WorkingSet:{2}MB Private:{3}MB | GC:0={4} 1={5} 2={6} Heap:{7}MB | 线程:{8} 句柄:{9}",
						cpuPct, gpuPart, ws, pm, gc0, gc1, gc2, gcHeap, thrCnt, hdlCnt);

					if (!anyFrames)
					{
						// 待机状态：单行简报
						sb.Append(string.Format("[{0}] 全部相机无帧 PLC写Avg={1}ms 队列={2} | {3}",
							DateTime.Now.ToString("HH:mm"), plcAvg, SendResultList.Count, sysLine));
					}
					else
					{
						sb.AppendLine(string.Format("══════ 耗时汇总 [{0}] (每分钟一批, 批间独立统计) ══════", csvTs));
						double maxCamAvg = 0; string slowestCam = ""; long maxAiAvg = 0; string heaviestAiCam = "";
						foreach (var proc in processors)
						{
							if (proc == null) continue;
							var perf = proc.Performance;
							if (perf.ProcessCount == 0) { sb.AppendLine(string.Format("[{0}] 无帧", proc.CameraName)); continue; }

							sb.AppendLine(perf.GetStageReport(proc.CameraName));

							// 收集诊断线索
							if (perf.AverageTimeMs > maxCamAvg) { maxCamAvg = perf.AverageTimeMs; slowestCam = proc.CameraName; }
							long aiT = perf.StageTimes != null && perf.StageTimes.ContainsKey("AI模型推理") ? perf.StageTimes["AI模型推理"] : 0;
							long aiAvgMs = aiT / perf.ProcessCount;
							if (aiAvgMs > maxAiAvg) { maxAiAvg = aiAvgMs; heaviestAiCam = proc.CameraName; }

							long aiM = perf.StageMax != null && perf.StageMax.ContainsKey("AI模型推理") ? perf.StageMax["AI模型推理"] : 0;
							long aiMin = perf.StageMin != null && perf.StageMin.ContainsKey("AI模型推理") ? perf.StageMin["AI模型推理"] : 0;
							string aiAvg = aiT > 0 ? ((double)aiT / perf.ProcessCount).ToString("F0") : "-";
							string aiMinStr = aiT > 0 ? aiMin.ToString() : "-";
							csvRows.Add(string.Format("{0},{1},{2},{3:F0},{4},{5},{6},{7},{8},,,,",
								csvTs, proc.CameraName, perf.ProcessCount, perf.AverageTimeMs, perf.MinTimeMs, perf.MaxTimeMs, aiAvg, aiMinStr, aiM));
						}

						sb.AppendLine(plcCnt > 0
							? string.Format("[PLC发送] 次数:{0} 写入Avg:{1}ms Max:{2}ms 间隔Max:{3}ms 队列:{4}", plcCnt, plcAvg, plcMax, plcGap, SendResultList.Count)
							: string.Format("[PLC发送] 本周期无发送 队列:{0}", SendResultList.Count));
						sb.AppendLine(sysLine);

						// ──── 流水线健康快照：队列积压 + 缓存水位 + 线程池 ────
						try
						{
							int pq1 = _processor1?.ImageQueueCount ?? 0, rq1 = _processor1?.ResultQueueCount ?? 0;
							int pq2 = _processor2?.ImageQueueCount ?? 0, rq2 = _processor2?.ResultQueueCount ?? 0;
							int pq3 = _processor3?.ImageQueueCount ?? 0, rq3 = _processor3?.ResultQueueCount ?? 0;
							int pq4 = _processor4?.ImageQueueCount ?? 0, rq4 = _processor4?.ResultQueueCount ?? 0;
							int pq5 = _processor5?.ImageQueueCount ?? 0, rq5 = _processor5?.ResultQueueCount ?? 0;
							int sq1 = _highSpeedSaver1?.QueueCount ?? 0, sq2 = _highSpeedSaver2?.QueueCount ?? 0;
							int sq3 = _highSpeedSaver3?.QueueCount ?? 0, sq4 = _highSpeedSaver4?.QueueCount ?? 0;
							int sq5 = _highSpeedSaver5?.QueueCount ?? 0;
							int imgN = _imageCache.Count, rstN = _resultImageCache.Count, pendN = _pendingImageSaves.Count;
							ThreadPool.GetAvailableThreads(out int wkAvail, out int ioAvail);
							ThreadPool.GetMaxThreads(out int wkMax, out int ioMax);

							// 队列压缩:全0时显示"空闲"；逐相机明细只显示非零值
							var qParts = new List<string>();
							if (pq1 + pq2 + pq3 + pq4 + pq5 + rq1 + rq2 + rq3 + rq4 + rq5 == 0)
								qParts.Add("处理队列:空闲");
							else
							{
								var procQ = new List<string>();
								if (pq1 > 0 || rq1 > 0) procQ.Add($"Cam1:{pq1}/{rq1}");
								if (pq2 > 0 || rq2 > 0) procQ.Add($"Cam2:{pq2}/{rq2}");
								if (pq3 > 0 || rq3 > 0) procQ.Add($"Cam3:{pq3}/{rq3}");
								if (pq4 > 0 || rq4 > 0) procQ.Add($"Cam4:{pq4}/{rq4}");
								if (pq5 > 0 || rq5 > 0) procQ.Add($"Cam5:{pq5}/{rq5}");
								qParts.Add("处理队列(图/结果):" + (procQ.Count > 0 ? string.Join(" ", procQ) : "全部0"));
							}
							if (sq1 + sq2 + sq3 + sq4 + sq5 == 0)
								qParts.Add("存图队列:空闲");
							else
							{
								var saveQ = new List<string>();
								if (sq1 > 0) saveQ.Add($"Cam1:{sq1}");
								if (sq2 > 0) saveQ.Add($"Cam2:{sq2}");
								if (sq3 > 0) saveQ.Add($"Cam3:{sq3}");
								if (sq4 > 0) saveQ.Add($"Cam4:{sq4}");
								if (sq5 > 0) saveQ.Add($"Cam5:{sq5}");
								qParts.Add("存图队列:" + string.Join(" ", saveQ));
							}
							qParts.Add($"缓存:Img={imgN} Rst={rstN} Pend={pendN} 驱逐累计={_cacheEvictCount}");
							qParts.Add($"线程池:工作忙={wkMax - wkAvail}/{wkMax} IO忙={ioMax - ioAvail}/{ioMax}");
							int logPend = 0, logDrop = 0;
							try { logPend = FastLogger.Instance.PendingCount; logDrop = FastLogger.Instance.DroppedCount; } catch { }
							int dbPend = _pendingRecords?.Count ?? 0;
							qParts.Add($"日志积压:{logPend}(丢弃:{logDrop}) DB待写入:{dbPend}");
							sb.AppendLine("[流水线] " + string.Join(" | ", qParts));

							// ──── 趋势对比 vs 上一分钟（不猜阈值，用跑出来的数据自己比）────
							var trends = new List<string>();
							int gc2Delta = gc2 - _lastGc2Count;
							_lastGc2Count = gc2;

							if (_prevMetrics != null)
							{
								var pr = _prevMetrics;
								foreach (var proc in processors)
								{
									if (proc == null) continue;
									var perf = proc.Performance;
									if (perf.ProcessCount == 0) continue;
									string cam = proc.CameraName;
									if (pr.CamAvgMs.TryGetValue(cam, out double prevAvg) && prevAvg > 0)
									{
										double delta = perf.AverageTimeMs - prevAvg;
										double pct = delta / prevAvg * 100;
										if (Math.Abs(pct) > 10)
											trends.Add($"{cam}:{perf.AverageTimeMs:F0}ms(Δ{delta:+0;-0}ms/{pct:+0;-0}%)");
									}
								}
								if (pr.CpuPct > 0 && Math.Abs(cpuPct - pr.CpuPct) > 5)
									trends.Add($"CPU:{cpuPct:F1}%(Δ{cpuPct - pr.CpuPct:+0.0;-0.0}%)");
								if (pm > 0 && pr.PrivateMB > 0 && Math.Abs(pm - pr.PrivateMB) > 50)
									trends.Add($"Private:{pm}MB(Δ{pm - pr.PrivateMB:+0;-0})");
								if (gc2Delta > 0) trends.Add($"GC Gen2:+{gc2Delta}(累计{gc2})");
								if (gUtil > 0 && pr.GpuUtil > 0 && Math.Abs(gUtil - pr.GpuUtil) > 10)
									trends.Add($"GPU:{gUtil}%(Δ{gUtil - pr.GpuUtil:+0;-0}%)");
								if (gMem > 0 && pr.GpuMemMB > 0 && Math.Abs(gMem - pr.GpuMemMB) > 200)
									trends.Add($"GPU显存:{gMem}MB(Δ{gMem - pr.GpuMemMB:+0;-0})");
								if (logDrop > 0) trends.Add($"日志丢弃+{logDrop}");
							}
							else { trends.Add("(首份,下分钟起有对比)"); }

							// 仅保留普适的绝对危险告警
							var alerts = new List<string>();
							if (cpuPct > 95) alerts.Add("⚠CPU>95%");
							if (logDrop > 0) alerts.Add("⚠日志丢弃");
							if (gc2Delta >= 5) alerts.Add("⚠GC暴增");

							// 保存快照
							_prevMetrics = new MinuteSnapshot
							{
								CamAvgMs = processors.Where(p => p != null).ToDictionary(p => p.CameraName, p => p.Performance.AverageTimeMs),
								CpuPct = cpuPct,
								GpuUtil = gUtil,
								GpuMemMB = gMem,
								PrivateMB = pm,
								Gc2Count = gc2
							};

							// ──── 整点累计快照（每60份报告≈1小时），追踪长周期趋势 ────
							if (++_hourlyCounter >= 60)
							{
								_hourlyCounter = 0;
								if (_startupWorkingSet == 0) { _startupWorkingSet = ws; _startupThreadCount = thrCnt; _startupHandleCount = hdlCnt; }
								long wsDelta = ws - _startupWorkingSet;
								int thrDelta = thrCnt - _startupThreadCount;
								int hdlDelta = hdlCnt - _startupHandleCount;
								// 汇总本小时各相机帧数
								var camFrames = processors.Where(p => p != null)
									.Select(p => $"{p.CameraName}:{GetTotalFrames(p.CameraName)}").ToArray();
								sb.AppendLine($"══ 整点累计快照(距启动约{_hourlyCounter*60 + (_startupWorkingSet > 0 ? 60 : 0)}分钟) ══");
								sb.AppendLine($"  累计帧数: {string.Join(" ", camFrames)}");
								sb.AppendLine($"  资源变化(自首次报告): WS{wsDelta:+0;-0}MB 线程{thrDelta:+0;-0} 句柄{hdlDelta:+0;-0} | 当前: WS={ws}MB 线程={thrCnt} 句柄={hdlCnt}");
								sb.AppendLine($"  GC累计: 0代={gc0} 1代={gc1} 2代={gc2} | 缓存驱逐累计={_cacheEvictCount}");
							}

							string trendLine = trends.Count > 0 ? "══ 趋势(vs上一分钟): " + string.Join(" | ", trends) : "";
							if (trendLine.Length > 0) sb.AppendLine(trendLine + (alerts.Count > 0 ? " " + string.Join(" ", alerts) : ""));
							sb.Append("══════════════════════════════════");
						}
						catch { }
					}
				} // using sysProc

				csvRows.Add(string.Format("{0},PLC,{1},,,,,,,{2},{3},{4},{5}",
					csvTs, plcCnt, plcCnt > 0 ? plcAvg : "", plcMax, plcGap, SendResultList.Count));

				string report = sb.ToString();
				try { if (FastLogger.IsInitialized) FastLogger.Instance.Info(report); } catch { }
				WritePerfCsv(csvRows);

				// 【批间独立】输出完成后统一清零，下一分钟从零开始统计
				ResetPerformanceStats();
				Interlocked.Exchange(ref _plcPerfSendCount, 0);
				Interlocked.Exchange(ref _plcPerfTotalMs, 0);
				Interlocked.Exchange(ref _plcPerfMaxMs, 0);
				Interlocked.Exchange(ref _plcPerfIntervalMaxMs, 0);
			}
			catch (Exception ex)
			{
				try { if (FastLogger.IsInitialized) FastLogger.Instance.Error("[Brief] " + ex.Message); } catch { }
			}
		}

		/// <summary>获取相机累计处理帧数（整点快照用）</summary>
		private long GetTotalFrames(string cameraName)
		{
			switch (cameraName)
			{
				case "Camera1": return Interlocked.CompareExchange(ref resultCount1, 0, 0);
				case "Camera2": return Interlocked.CompareExchange(ref resultCount2, 0, 0);
				case "Camera3": return Interlocked.CompareExchange(ref resultCount3, 0, 0);
				case "Camera4": return Interlocked.CompareExchange(ref resultCount4, 0, 0);
				case "Camera5": return Interlocked.CompareExchange(ref resultCount5, 0, 0);
				default: return 0;
			}
		}

		/// <summary>
		/// 耗时数据落盘为 CSV 表格：Logs/PerfStats/yyyyMMdd.csv（每1分钟随耗时汇总追加一批行）
		/// 统计区间说明：每一行都是该分钟内的独立统计（输出后清零，批间互不累计）
		/// </summary>
		private void WritePerfCsv(List<string> rows)
		{
			try
			{
				if (rows == null || rows.Count == 0) return;
				string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "PerfStats");
				if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
				string file = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd") + ".csv");
				if (!File.Exists(file))
					File.WriteAllText(file, "时间,对象,帧数/发送次数,总Avg(ms),总Min(ms),总Max(ms),AIavg(ms),AIMin(ms),AIMax(ms),PLC写Avg(ms),PLC写Max(ms),PLC间隔Max(ms),发送队列\r\n", new UTF8Encoding(true));
				File.AppendAllText(file, string.Join("\r\n", rows) + "\r\n", Encoding.UTF8);
			}
			catch { }
		}
		/// <summary>
		/// 内存压力检查：当缓存过大或总内存超过阈值时强制清理
		/// </summary>
		private void CheckMemoryPressure()
		{
			try
			{
				int cacheCount = _imageCache.Count;
				if (cacheCount > 0 || _resultImageCache.Count > 0)
				{
					try { FastLogger.Instance.Debug($"[内存] ImageCache={_imageCache.Count} ResultCache={_resultImageCache.Count} PendingSaves={_pendingImageSaves.Count} 已驱逐={_cacheEvictCount}"); } catch { }
				}

				// 缓存超过上限 → 清理最旧的50%
				if (_imageCache.Count > MAX_IMAGE_CACHE_SIZE)
				{
					int toRemove = _imageCache.Count - MAX_IMAGE_CACHE_SIZE / 2;
					EvictOldestCacheEntries(toRemove);
				}

				// 检查进程内存
				using (var proc = Process.GetCurrentProcess())
				{
					long memMB = proc.WorkingSet64 / 1024 / 1024;
					if (memMB > MEMORY_WARN_THRESHOLD)
					{
						try { FastLogger.Instance.Warn($"[内存] 内存偏高: {memMB}MB, 仅驱逐缓存"); } catch { }
						EvictOldestCacheEntries(MAX_IMAGE_CACHE_SIZE / 2);
					}
					if (memMB > MEMORY_CRITICAL_THRESHOLD)
					{
						try { FastLogger.Instance.Error($"[内存] 内存严重不足: {memMB}MB, 紧急清理缓存"); } catch { }
						// 清空所有图像缓存
						ForceClearAllImageCache();
					}
				}
			}
			catch { }
		}

		/// <summary>
		/// 驱逐最旧的N个缓存条目（按key排序，删除最小的key=最旧的ID）
		/// </summary>
		private void EvictOldestCacheEntries(int count)
		{
			if (count <= 0) return;
			try
			{
				var keys = _imageCache.Keys.OrderBy(k => k).Take(count).ToList();
				foreach (var key in keys)
				{
					ClearImageCache(key);
					Interlocked.Increment(ref _cacheEvictCount);
				}
				try { FastLogger.Instance.Warn($"[内存] 驱逐{keys.Count}个旧缓存条目, 剩余{_imageCache.Count}, 累计驱逐{_cacheEvictCount}"); } catch { }
			}
			catch { }
		}

		/// <summary>
		/// 紧急清空所有图像缓存（内存严重不足时）
		/// </summary>
		private void ForceClearAllImageCache()
		{
			try
			{
				var keys = _imageCache.Keys.ToArray();
				foreach (var key in keys)
				{
					ClearImageCache(key);
					Interlocked.Increment(ref _cacheEvictCount);
				}
				// 同时清理滞留的存图待处理队列，防止对象池泄漏
				{
					var pendingKeys = _pendingImageSaves.Keys.ToArray();
					foreach (var key in pendingKeys)
					{
						if (_pendingImageSaves.TryRemove(key, out var items))
						{
							foreach (var item in items)
								try { QueueResultItem.Return(item); } catch { }
						}
					}
				}
				// P1: single GC only
				try { FastLogger.Instance.Error($"[内存] 紧急清理完成，清除{keys.Length}组缓存"); } catch { }
			}
			catch { }
		}

		/// <summary>
		/// 查询 NVIDIA GPU 显存占用与利用率（nvidia-smi），60秒缓存，超时或不可用时返回 0
		/// </summary>
		private void QueryGpuInfo()
		{
			long nowTicks = DateTime.UtcNow.Ticks;
			if (nowTicks - Interlocked.Read(ref _lastGpuQueryTicks) < 60L * TimeSpan.TicksPerSecond)
				return;
			Interlocked.Exchange(ref _lastGpuQueryTicks, nowTicks);
			try
			{
				using (var procGpu = new Process())
				{
					procGpu.StartInfo = new ProcessStartInfo
					{
						FileName = "nvidia-smi",
						Arguments = "--query-gpu=memory.used,utilization.gpu --format=csv,noheader,nounits",
						RedirectStandardOutput = true,
						UseShellExecute = false,
						CreateNoWindow = true
					};
					procGpu.Start();
					string output = procGpu.StandardOutput.ReadToEnd();
					if (!procGpu.WaitForExit(3000)) { try { procGpu.Kill(); } catch { } return; }
					if (procGpu.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
					{
						var parts = output.Trim().Split(',');
						if (parts.Length >= 2 && long.TryParse(parts[0].Trim(), out long mem) && long.TryParse(parts[1].Trim(), out long util))
						{
							Interlocked.Exchange(ref _gpuMemoryMb, mem);
							Interlocked.Exchange(ref _gpuUtilPct, util);
						}
					}
				}
			}
			catch { /* nvidia-smi 不可用属于正常情况（无N卡或未装驱动），静默跳过 */ }
		}

		private void StartMethod(CameraSelect camera) { }
		private void EndMethod(CameraSelect camera) { }

		private void InitializeImageProcessors()
		{
			try
			{
				// 仅启用工位创建处理器，禁用工位置 null
				_processor1 = _cameraEnabled[0] ? new ImageProcessor("Camera1", ProcessCamera1Image, MAX_QUEUE_SIZE) : null;
				_processor2 = _cameraEnabled[1] ? new ImageProcessor("Camera2", ProcessCamera2Image, MAX_QUEUE_SIZE) : null;
				_processor3 = _cameraEnabled[2] ? new ImageProcessor("Camera3", ProcessCamera3Image, MAX_QUEUE_SIZE) : null;
				_processor4 = _cameraEnabled[3] ? new ImageProcessor("Camera4", ProcessCamera4Image, MAX_QUEUE_SIZE) : null;
				_processor5 = _cameraEnabled[4] ? new ImageProcessor("Camera5", ProcessCamera5Image, MAX_QUEUE_SIZE) : null;

				var processors = new[] { _processor1, _processor2, _processor3, _processor4, _processor5 };
				// ResultMatcher 传入可能含 null 的数组（内部跳过 null）
				_resultMatcher = new ResultMatcher(processors, OnResultsMatched);

				int activeCount = processors.Count(p => p != null);
				FastLogger.Instance.Info($"图像处理器初始化完成（{activeCount}/5 启用）");
				try { FastLogger.Instance.Info($"图像处理器初始化完成（{activeCount}/5 启用）"); } catch { }
			}
			catch (Exception ex)
			{
				try { FastLogger.Instance.Error("图像处理器初始化失败", ex); } catch { }
				FastLogger.Instance.Error($"初始化图像处理器失败: {ex.Message}");
			}
		}

		private void UpdatePerformanceDisplay()
		{
			if (_isClosing) return;
			try
			{
				this.BeginInvoke(new Action(() =>
				{
					if (_isClosing) return;
					try
					{
						ImageQueueTxt1.Text = _processor1?.ImageQueueCount.ToString() ?? "0";
						ImageQueueTxt2.Text = _processor2?.ImageQueueCount.ToString() ?? "0";
						ImageQueueTxt3.Text = _processor3?.ImageQueueCount.ToString() ?? "0";
						ImageQueueTxt4.Text = _processor4?.ImageQueueCount.ToString() ?? "0";
						ImageQueueTxt5.Text = _processor5?.ImageQueueCount.ToString() ?? "0";

						ResultQueueTxt1.Text = _processor1?.ResultQueueCount.ToString() ?? "0";
						ResultQueueTxt2.Text = _processor2?.ResultQueueCount.ToString() ?? "0";
						ResultQueueTxt3.Text = _processor3?.ResultQueueCount.ToString() ?? "0";
						ResultQueueTxt4.Text = _processor4?.ResultQueueCount.ToString() ?? "0";
						ResultQueueTxt5.Text = _processor5?.ResultQueueCount.ToString() ?? "0";

						UpdateCameraPerformanceDisplay(1, _processor1?.Performance);
						UpdateCameraPerformanceDisplay(2, _processor2?.Performance);
						UpdateCameraPerformanceDisplay(3, _processor3?.Performance);
						UpdateCameraPerformanceDisplay(4, _processor4?.Performance);
						UpdateCameraPerformanceDisplay(5, _processor5?.Performance);
					}
					catch { }
				}));
			}
			catch { }
		}

		private void UpdateCameraPerformanceDisplay(int cameraIndex, PerformanceStats? stats)
		{
			if (!stats.HasValue || _isClosing) return;
			try
			{
				switch (cameraIndex)
				{
					case 1:
						MeanTimeTxt1.Text = stats.Value.AverageTimeMs.ToString("F1");
						SingleTimeTxt1.Text = stats.Value.ProcessCount > 0 ? $"[{stats.Value.MinTimeMs}-{stats.Value.MaxTimeMs}]" : "0";
						break;
					case 2:
						MeanTimeTxt2.Text = stats.Value.AverageTimeMs.ToString("F1");
						SingleTimeTxt2.Text = stats.Value.ProcessCount > 0 ? $"[{stats.Value.MinTimeMs}-{stats.Value.MaxTimeMs}]" : "0";
						break;
					case 3:
						MeanTimeTxt3.Text = stats.Value.AverageTimeMs.ToString("F1");
						SingleTimeTxt3.Text = stats.Value.ProcessCount > 0 ? $"[{stats.Value.MinTimeMs}-{stats.Value.MaxTimeMs}]" : "0";
						break;
					case 4:
						MeanTimeTxt4.Text = stats.Value.AverageTimeMs.ToString("F1");
						SingleTimeTxt4.Text = stats.Value.ProcessCount > 0 ? $"[{stats.Value.MinTimeMs}-{stats.Value.MaxTimeMs}]" : "0";
						break;
					case 5:
						MeanTimeTxt5.Text = stats.Value.AverageTimeMs.ToString("F1");
						SingleTimeTxt5.Text = stats.Value.ProcessCount > 0 ? $"[{stats.Value.MinTimeMs}-{stats.Value.MaxTimeMs}]" : "0";
						break;
				}
			}
			catch { }
		}

		private void ResetPerformanceStats()
		{
			_processor1?.ResetPerformanceStats();
			_processor2?.ResetPerformanceStats();
			_processor3?.ResetPerformanceStats();
			_processor4?.ResetPerformanceStats();
			_processor5?.ResetPerformanceStats();
		}
		#endregion

		private void MainFrm_Load(object sender, EventArgs e)
		{
			try
			{
				try { FastLogger.Instance.Info("MainFrm_Load 开始初始化"); } catch { }
				FastLogger.Instance.Info("系统开始初始化");
				Loading.ShowLoadingScreen();

				//if (!UsbDogClass.FindUsbDog())
				//{
				//	FastLogger.Instance.Info("初始化时，未找到加密狗");
				//	throw new Exception("初始化时，未找到加密狗");
				//}
				_Config.cameraDebug = 0;

				LoadConfiguration();
				InitData();
				InitializeAIModels();

				// 【PLC类型选择】根据配置或默认选择通讯协议（防呆：初始化失败回退 S7-1200）
				try
				{
					string plcTypeCfg = (_Config.PlcType ?? "").Trim().ToUpperInvariant();
					if (plcTypeCfg == "HCMODBUS" || plcTypeCfg == "HC")
					{
						modbusClass = new HCModbusAdapter();
						FastLogger.Instance.Info("[PLC] 使用 HCModbus 通讯协议");
						try { FastLogger.Instance.Info("PLC类型: HCModbus"); } catch { }
					}
					else
					{
						modbusClass = new S7_1200Class();
						FastLogger.Instance.Info("[PLC] 使用 S7-1200 通讯协议（配置值:" + (string.IsNullOrEmpty(plcTypeCfg) ? "默认" : plcTypeCfg) + "）");
						try { FastLogger.Instance.Info("PLC类型: S7-1200"); } catch { }
					}
				}
				catch (Exception plcInitEx)
				{
					// 兜底：任何异常都回退 S7-1200
					modbusClass = new S7_1200Class();
					FastLogger.Instance.Error($"[PLC] 类型选择异常，回退S7-1200: {plcInitEx.Message}");
				}

				if (modbusClass == null)
				{
					modbusClass = new S7_1200Class();
					FastLogger.Instance.Info("[PLC] modbusClass为null，强制使用S7-1200");
				}

				modbusClass.EventConnectState += ModbusConnectState;
				modbusClass.EventCount += PLCCountMethod;

				if (modbusClass.ConnectModbus())
				{
					WriteResultThread = new Thread(WriteResultMethod);
					WriteResultThread.IsBackground = true;
					WriteResultThread.Start();
					FastLogger.Instance.Info("Modbus连接完成");
				}

				if (_Config.IFInitCamera.ToBool())
				{
					InitCamera();
				}

				DeleteDaysAgoImage();

				Loading.CloseLoadingScreen(System.Windows.Forms.Application.OpenForms["Loading"]);
				Thread.Sleep(500);
				this.WindowState = FormWindowState.Maximized;

				StartIOThreads();

				uiMonitor1.Activte = true;
				uiMonitor2.Activte = true;

				timer1.Start();

				if (!VerifyMethod())
				{
					throw new Exception("初始化时，部分所需参数未定义");
				}

				uiLabel2_Click(null, null);

				modbusClass.RuningMethod();

				FastLogger.Instance.Info("系统初始化完成");
				try { FastLogger.Instance.Info("MainFrm_Load 初始化完成"); } catch { }
			}
			catch (Exception ex)
			{
				try { FastLogger.Instance.Error("MainFrm_Load 初始化失败", ex); } catch { }
				FastLogger.Instance.Error($"初始化时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				MessageBox.Show($"系统初始化失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void uiLabel2_Click(object sender, EventArgs e)
		{
			modbusClass.ClearCount();
			inputCount1 = 0;
			inputCount2 = 0;
			inputCount3 = 0;
			inputCount4 = 0;
			inputCount5 = 0;
			outputCount1 = 0;
			outputCount2 = 0;
			outputCount3 = 0;
			outputCount4 = 0;
			outputCount5 = 0;
			resultCount1 = 0;
			resultCount2 = 0;
			resultCount3 = 0;
			resultCount4 = 0;
			resultCount5 = 0;
		}

		public bool VerifyMethod()
		{
			try
			{
				input_cam1 = _Config.Input_Camera1;
				input_cam2 = _Config.Input_Camera2;
				input_cam3 = _Config.Input_Camera3;
				input_cam4 = _Config.Input_Camera4;
				input_cam5 = _Config.Input_Camera5;

				offset_cam1 = _Config.Offset_Camera1;
				offset_cam2 = _Config.Offset_Camera2;
				offset_cam3 = _Config.Offset_Camera3;
				offset_cam4 = _Config.Offset_Camera4;
				offset_cam5 = _Config.Offset_Camera5;
				offset_send = _Config.Offset_Send;

				return input_cam1 != -1 && input_cam2 != -1 && input_cam3 != -1 &&
					   input_cam4 != -1 && input_cam5 != -1 &&
					   offset_cam1 != -1 && offset_cam2 != -1 && offset_cam3 != -1 &&
					   offset_cam4 != -1 && offset_cam5 != -1 && offset_send != -1;
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"VerifyMethod出现异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return false;
			}
		}

		private void DeleteDaysAgoImage()
		{
			Task.Run(() =>
			{
				try
				{
					DeleteDir.DeleteMethod();
					FastLogger.Instance.Info("过期图像清理完成");
				}
				catch (Exception ex)
				{
					FastLogger.Instance.Error($"清理过期图像失败: {ex.Message}");
				}
			});
		}

		private void InitCamera()
		{
			try
			{
				try { FastLogger.Instance.Info("Camera Init: 开始"); } catch { }

				// Camera1
				if (_cameraEnabled[0])
				{
					try
					{
						camera1SDK = new DaHuaSDK();
						camera1SDK.SetCameraInterface(this);
						camera1SDK.OnImage += OnCamera1Image;
						camera1SDK.SetCameraByKey(_Config.Camera1SN);
						camera1SDK.Open();
						var sdkVer1 = camera1SDK.curCameraKey;
						camera1SDK.StopStreamGrabber();
						camera1SDK.SetAcquisitionMode(0);
						bool trigOk1 = camera1SDK.SetTriggerMode(1);
						bool srcOk1 = camera1SDK.setTriggerSource(1);
						try { FastLogger.Instance.Info($"Camera1: 触发配置- SetTriggerMode(1)={trigOk1} setTriggerSource(1)={srcOk1} Key={sdkVer1}"); } catch { }
						camera1SDK.StartStreamGrabber();
						GlobalVar.CameraSdk1 = camera1SDK;
						try { FastLogger.Instance.Info("Camera1: 连接成功 SN=" + _Config.Camera1SN); } catch { }
						camera1State.State = Sunny.UI.UILightState.On;
						try { FastLogger.Instance.Debug("Camera1: SDK连接成功 SN=" + _Config.Camera1SN); } catch { }
						Thread.Sleep(100);
					}
					catch (Exception ex)
					{
						try { FastLogger.Instance.Error("相机一加载失败", ex); } catch { }
						try { FastLogger.Instance.Error("Camera1: 加载失败 " + ex.Message + ", 异常: " + ex.GetType().Name); } catch { }
						if (IFSaveLog) FastLogger.Instance.Error("[CamCfg] Camera1 连接失败: " + ex.Message);
					}
				}
				else { }

				// Camera2
				if (_cameraEnabled[1])
				{
					try
					{
						camera2SDK = new DaHuaSDK();
						camera2SDK.SetCameraInterface(this);
						camera2SDK.OnImage += OnCamera2Image;
						camera2SDK.SetCameraByKey(_Config.Camera2SN);
						camera2SDK.Open();
						var sdkVer2 = camera2SDK.curCameraKey;
						camera2SDK.StopStreamGrabber();
						camera2SDK.SetAcquisitionMode(0);
						bool trigOk2 = camera2SDK.SetTriggerMode(1);
						bool srcOk2 = camera2SDK.setTriggerSource(1);
						try { FastLogger.Instance.Info($"Camera2: 触发配置- SetTriggerMode(1)={trigOk2} setTriggerSource(1)={srcOk2} Key={sdkVer2}"); } catch { }
						camera2SDK.StartStreamGrabber();
						GlobalVar.CameraSdk2 = camera2SDK;
						try { FastLogger.Instance.Info("Camera2: 连接成功 SN=" + _Config.Camera2SN); } catch { }
						camera2State.State = Sunny.UI.UILightState.On;
						try { FastLogger.Instance.Debug("Camera2: SDK连接成功 SN=" + _Config.Camera2SN); } catch { }
						Thread.Sleep(100);
					}
					catch (Exception ex)
					{
						try { FastLogger.Instance.Error("相机二加载失败", ex); } catch { }
						try { FastLogger.Instance.Error("Camera2: 加载失败 " + ex.Message + ", 异常: " + ex.GetType().Name); } catch { }
						if (IFSaveLog) FastLogger.Instance.Error("[CamCfg] Camera2 连接失败: " + ex.Message);
					}
				}
				else { }

				// Camera3
				if (_cameraEnabled[2])
				{
					try
					{
						camera3SDK = new DaHuaSDK();
						camera3SDK.SetCameraInterface(this);
						camera3SDK.OnImage += OnCamera3Image;
						camera3SDK.SetCameraByKey(_Config.Camera3SN);
						camera3SDK.Open();
						var sdkVer3 = camera3SDK.curCameraKey;
						camera3SDK.StopStreamGrabber();
						camera3SDK.SetAcquisitionMode(0);
						bool trigOk3 = camera3SDK.SetTriggerMode(1);
						bool srcOk3 = camera3SDK.setTriggerSource(1);
						try { FastLogger.Instance.Info($"Camera3: 触发配置- SetTriggerMode(1)={trigOk3} setTriggerSource(1)={srcOk3} Key={sdkVer3}"); } catch { }
						camera3SDK.StartStreamGrabber();
						GlobalVar.CameraSdk3 = camera3SDK;
						try { FastLogger.Instance.Info("Camera3: 连接成功 SN=" + _Config.Camera3SN); } catch { }
						camera3State.State = Sunny.UI.UILightState.On;
						try { FastLogger.Instance.Debug("Camera3: SDK连接成功 SN=" + _Config.Camera3SN); } catch { }
						Thread.Sleep(100);
					}
					catch (Exception ex)
					{
						try { FastLogger.Instance.Error("相机三加载失败", ex); } catch { }
						try { FastLogger.Instance.Error("Camera3: 加载失败 " + ex.Message + ", 异常: " + ex.GetType().Name); } catch { }
						if (IFSaveLog) FastLogger.Instance.Error("[CamCfg] Camera3 连接失败: " + ex.Message);
					}
				}
				else { }

				// Camera4
				if (_cameraEnabled[3])
				{
					try
					{
						camera4SDK = new DaHuaSDK();
						camera4SDK.SetCameraInterface(this);
						camera4SDK.OnImage += OnCamera4Image;
						camera4SDK.SetCameraByKey(_Config.Camera4SN);
						camera4SDK.Open();
						camera4SDK.StopStreamGrabber();
						camera4SDK.SetAcquisitionMode(0);
						camera4SDK.SetTriggerMode(1);
						camera4SDK.setTriggerSource(1);
						camera4SDK.StartStreamGrabber();
						GlobalVar.CameraSdk4 = camera4SDK;
						try { FastLogger.Instance.Info("Camera4: 连接成功 SN=" + _Config.Camera4SN); } catch { }
						camera4State.State = Sunny.UI.UILightState.On;
						try { FastLogger.Instance.Debug("Camera4: SDK连接成功 SN=" + _Config.Camera4SN); } catch { }
						Thread.Sleep(100);
					}
					catch (Exception ex)
					{
						try { FastLogger.Instance.Error("相机四加载失败", ex); } catch { }
						try { FastLogger.Instance.Error("Camera4: 加载失败 " + ex.Message + ", 异常: " + ex.GetType().Name); } catch { }

					}
				}
				else { }

				// Camera5
				if (_cameraEnabled[4])
				{
					try
					{
						camera5SDK = new DaHuaSDK();
						camera5SDK.SetCameraInterface(this);
						camera5SDK.OnImage += OnCamera5Image;
						camera5SDK.SetCameraByKey(_Config.Camera5SN);
						camera5SDK.Open();
						camera5SDK.StopStreamGrabber();
						camera5SDK.SetAcquisitionMode(0);
						camera5SDK.SetTriggerMode(1);
						camera5SDK.setTriggerSource(1);
						camera5SDK.StartStreamGrabber();
						GlobalVar.CameraSdk5 = camera5SDK;
						try { FastLogger.Instance.Info("Camera5: 连接成功 SN=" + _Config.Camera5SN); } catch { }
						camera5State.State = Sunny.UI.UILightState.On;
						try { FastLogger.Instance.Debug("Camera5: SDK连接成功 SN=" + _Config.Camera5SN); } catch { }
						Thread.Sleep(100);
					}
					catch (Exception ex)
					{
						try { FastLogger.Instance.Error("相机五加载失败", ex); } catch { }
						try { FastLogger.Instance.Error("Camera5: 加载失败 " + ex.Message + ", 异常: " + ex.GetType().Name); } catch { }

					}
				}
				else { }

				int connectedCount = (camera1SDK != null ? 1 : 0)
					+ (camera2SDK != null ? 1 : 0)
					+ (camera3SDK != null ? 1 : 0)
					+ (camera4SDK != null ? 1 : 0)
					+ (camera5SDK != null ? 1 : 0);
				FastLogger.Instance.Info("相机初始化完成");
				try { FastLogger.Instance.Info("Camera Init: 完成, " + connectedCount + "/5已连接"); } catch { }
			}
			catch (Exception ex)
			{
				try { FastLogger.Instance.Error("相机初始化失败", ex); } catch { }
				FastLogger.Instance.Error($"连接相机错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void Camera5SDK_dhEventInfo(string message)
		{
			FastLogger.Instance.Debug($"{message}");
		}

		private void PLCCountMethod(uint count1, uint count2, uint count3, uint count4, uint count5)
		{
			if (_isClosing) return;
			try
			{
				this.BeginInvoke(new Action(() =>
				{
					if (_isClosing) return;
					plcInput1Txt.Text = count1.ToString();
					plcInput2Txt.Text = count2.ToString();
					plcInput3Txt.Text = count3.ToString();
					plcInput4Txt.Text = count4.ToString();
					plcInput5Txt.Text = count5.ToString();
					camera1CountTxt.Text = camera1Count.ToString();
					camera2CountTxt.Text = camera2Count.ToString();
					camera3CountTxt.Text = camera3Count.ToString();
					camera4CountTxt.Text = camera4Count.ToString();
					camera5CountTxt.Text = camera5Count.ToString();
				}));
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"更新PLC计数异常: {ex.Message}");
			}
		}

		private void ModbusConnectState(bool state, string error)
		{
			FastLogger.Instance.Info($"ModbusConnectState: {error}");
			if (state)
			{
				PlcState.State = Sunny.UI.UILightState.On;
			}
			else
			{
				PlcState.State = Sunny.UI.UILightState.Off;
				modbusClass.Reconnect();
			}
		}

		private bool InitMotoCard(string ip)
		{
			try
			{
				FastLogger.Instance.Info("控制器IP：" + ip);
				try { FastLogger.Instance.Info("运动控制卡开始连接 IP=" + ip); } catch { }
				if (!myZmcaux.Connect(ref g_handle, ip))
				{
					try { FastLogger.Instance.Error("运动控制卡连接失败 IP=" + ip + " (Connect返回false);"); } catch { }
					return false;
				}

				MotionState.State = UILightState.On;
				myZmcaux.Init(g_handle);
				FastLogger.Instance.Info($"运控卡连接成功，句柄为：{g_handle}");
				try { FastLogger.Instance.Info("运动控制卡连接成功 Handle=" + g_handle.ToInt64() + " IP=" + ip); } catch { }
				return true;
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"连接运控卡出现异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				try { FastLogger.Instance.Error("运动控制卡连接异常 IP=" + ip, ex); } catch { }
				return false;
			}
		}

		private void LoadConfiguration()
		{
			try
			{
				output_delay_cam1 = _Config.DelayOutput_Camera1;
				output_delay_cam2 = _Config.DelayOutput_Camera2;
				output_delay_cam3 = _Config.DelayOutput_Camera3;
				output_delay_cam4 = _Config.DelayOutput_Camera4;
				output_delay_cam5 = _Config.DelayOutput_Camera5;

				input_cam1 = _Config.Input_Camera1;
				input_cam2 = _Config.Input_Camera2;
				input_cam3 = _Config.Input_Camera3;
				input_cam4 = _Config.Input_Camera4;
				input_cam5 = _Config.Input_Camera5;

				offset_cam1 = _Config.Offset_Camera1;
				offset_cam2 = _Config.Offset_Camera2;
				offset_cam3 = _Config.Offset_Camera3;
				offset_cam4 = _Config.Offset_Camera4;
				offset_cam5 = _Config.Offset_Camera5;
				offset_send = _Config.Offset_Send;

				IFSaveLog = _Config.IFSaveLog;

				//modelpath_cam1 = _Config.ModelPath_Cam1;
				UseGpu_cam1 = _Config.UseGpu_Cam1;
				deviceid_cam1 = _Config.DeviceId_Cam1;
				modelId_char_cam1 = _Config.ModelId_Segmentation_Cam1;

				//modelpath_cam2 = _Config.ModelPath_Cam2;
				UseGpu_cam2 = _Config.UseGpu_Cam2;
				deviceid_cam2 = _Config.DeviceId_Cam2;
				modelId_class_cam2 = _Config.ModelId_Class_Cam2;

				//modelpath_cam4 = _Config.ModelPath_Cam4;
				UseGpu_cam4 = _Config.UseGpu_Cam4;
				deviceid_cam4 = _Config.DeviceId_Cam4;
				modelId_char_cam4 = _Config.ModelId_Char_Cam4;
				modelId_segmentation_cam4 = _Config.ModelId_Segmentation_Cam4;

				//modelpath_cam5 = _Config.ModelPath_Cam5;
				UseGpu_cam5 = _Config.UseGpu_Cam5;
				deviceid_cam5 = _Config.DeviceId_Cam5;
				modelId_char_cam5 = _Config.ModelId_Char_Cam5;
				modelId_char_PCode_cam5 = _Config.ModelId_Char_PCode_Cam5;
				modelId_flaw_cam5 = _Config.ModelId_Class_Cam5;
				modelId_color_cam5 = _Config.ModelId_ColorSegmentation_Cam5;
				modelId_segmentation_cam5 = _Config.ModelId_Segmentation_Cam5;





				this.Invoke(new Action(() =>
				{
					try
					{
						if (totalTxt != null) totalTxt.Text = _Config.total.ToString();
						if (okTxt != null) okTxt.Text = _Config.ok.ToString();
						if (ngTxt != null) ngTxt.Text = (_Config.total - _Config.ok).ToString();
						if (BaoGuanNGTxt != null) BaoGuanNGTxt.Text = _Config.burstExcludeCount.ToString();

						if (yieldTxt != null && _Config.total > 0)
						{
							// 防呆：burstExcludeCount不能超过total NG（防止良率>100%）
							if (_Config.burstExcludeCount > _Config.total - _Config.ok)
								_Config.burstExcludeCount = _Config.total - _Config.ok;
							// 良率 = OK合格数 ÷（总检测数 - 被剔除的连续爆管异常数量）
							double effectiveCount = _Config.total - _Config.burstExcludeCount;
							double yieldRate = effectiveCount > 0 ? Math.Min(100.0, Math.Max(0.0, (_Config.ok * 100.0) / effectiveCount)) : 0;
							yieldTxt.Text = yieldRate.ToString("F2") + "%";
							yieldTxt.ForeColor = yieldRate > 95 ? Color.Green : yieldRate > 90 ? Color.Orange : Color.Red;
						}
					}
					catch { }
				}));

				FastLogger.Instance.Info($"配置加载完成: cam1偏移{offset_cam1}, cam2偏移{offset_cam2}, cam3偏移{offset_cam3}, cam4偏移{offset_cam4}, cam5偏移{offset_cam5}, 发送偏移{offset_send}");
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"加载配置失败: {ex.Message}");
			}
		}

		private void InitializeAIModels()
		{
			try
			{
				int loadedCount = 0;
				string[] status = new string[5];

				// Camera1 - 分割模型
				if (_cameraEnabled[0])
				{
					Model_Segmentation_Cam1.Init(modelpath_cam1, UseGpu_cam1, deviceid_cam1, modelId_char_cam1);
					status[0] = "已加载(分割)";
					loadedCount++;
				}
				else status[0] = "跳过(工位未启用)";

				// Camera2 - 分类模型
				if (_cameraEnabled[1])
				{
					Model_Class_Cam2.Init(modelpath_cam2, UseGpu_cam2, deviceid_cam2, modelId_class_cam2);
					status[1] = "已加载(分类)";
					loadedCount++;
				}
				else status[1] = "跳过(工位未启用)";

				// Camera3 - 无AI模型（传统圆度检测）
				status[2] = _cameraEnabled[2] ? "无需AI(圆度检测)" : "跳过(工位未启用)";
				if (_cameraEnabled[2]) loadedCount++;

				// Camera4 - 分割+OCR模型
				if (_cameraEnabled[3])
				{
					Model_Segmentation_Cam4.Init(modelpath_cam4, UseGpu_cam4, deviceid_cam4, modelId_segmentation_cam4);
					Model_Char_Cam4.Init(modelpath_cam4, UseGpu_cam4, deviceid_cam4, modelId_char_cam4);
					status[3] = "已加载(分割+OCR)";
					loadedCount++;
				}
				else status[3] = "跳过(工位未启用)";

				// Camera5 - 字符+PCode+色标+缺陷+分割模型
				if (_cameraEnabled[4])
				{
					Model_Char_Cam5.Init(modelpath_cam5, UseGpu_cam5, deviceid_cam5, modelId_char_cam5);
					Model_Char_PCode_Cam5.Init(modelpath_cam5, UseGpu_cam5, deviceid_cam5, modelId_char_PCode_cam5);
					Model_Color_Cam5.Init(modelpath_cam5, UseGpu_cam5, deviceid_cam5, modelId_color_cam5);
					Model_Rests_Cam5.Init(modelpath_cam5, UseGpu_cam5, deviceid_cam5, modelId_rests_cam5);
					Model_Segmentation_Cam5.Init(modelpath_cam5, UseGpu_cam5, deviceid_cam5, modelId_segmentation_cam5);
					status[4] = "已加载(OCR+PCode+色标+缺陷)";
					loadedCount++;
				}
				else status[4] = "跳过(工位未启用)";

				FastLogger.Instance.Info("AI模型初始化完成");
				// 汇总日志
				try { FastLogger.Instance.Info("AI模型初始化: Cam1=" + status[0] + " Cam2=" + status[1] + " Cam3=" + status[2] + " Cam4=" + status[3] + " Cam5=" + status[4] + " (" + loadedCount + "/5 已加载)"); } catch { }
				// 明细日志（调试模式）
				try { if (FastLogger.IsInitialized && FastLogger.DebugEnabled) for (int i = 0; i < 5; i++) FastLogger.Instance.Debug("AI模型 Cam" + (i + 1) + ": " + status[i]); } catch { }
			}
			catch (Exception ex)
			{
				try { FastLogger.Instance.Error("AI模型初始化失败", ex); } catch { }
				FastLogger.Instance.Error($"AI模型初始化失败: {ex.Message}");
			}
		}


		private void StartIOThreads()
		{
			try
			{
				if (InitMotoCard(_Config.ControlIP))
				{
					ReadIO1Thread = new Thread(ReadIO1);
					ReadIO1Thread.IsBackground = true;
					ReadIO1Thread.Start();
					try { FastLogger.Instance.Info("运动控制卡已就绪，IO读取线程已启动"); } catch { }
				}
				else
				{
					try { FastLogger.Instance.Warn("运动控制卡未连接，IO读取线程跳过 IP=" + _Config.ControlIP); } catch { }
				}

				updateThread = new Thread(UpdateMethod);
				updateThread.IsBackground = true;
				updateThread.Start();
				FastLogger.Instance.Info("IO线程已启动");
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"启动IO线程失败: {ex.Message}");
			}
		}

		private void ReadIO1()
		{
			try
			{
				while (!_isClosing && !_cts.Token.IsCancellationRequested)
				{
					Thread.Sleep(2);
					IFInit = myZmcaux.GetModbusValue(g_handle, 0);
				}
			}
			catch (Exception ex)
			{
				if (!_isClosing)
					FastLogger.Instance.Error($"读IO信号异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		#region 实现相机接口
		public void OnCameraClose(string cameraName, string cameraKey)
		{
			FastLogger.Instance.Info(string.Format("相机【{0}】关闭连接", cameraKey));
			if (FastLogger.IsInitialized) FastLogger.Instance.Debug("相机断连: " + cameraName + " key=" + cameraKey);
			if (camera1SDK != null && camera1SDK.curCameraKey.Equals(cameraKey))
				camera1State.State = Sunny.UI.UILightState.Off;
			else if (camera2SDK != null && camera2SDK.curCameraKey.Equals(cameraKey))
				camera2State.State = Sunny.UI.UILightState.Off;
			else if (camera3SDK != null && camera3SDK.curCameraKey.Equals(cameraKey))
				camera3State.State = Sunny.UI.UILightState.Off;
			else if (camera4SDK != null && camera4SDK.curCameraKey.Equals(cameraKey))
				camera4State.State = Sunny.UI.UILightState.Off;
			else if (camera5SDK != null && camera5SDK.curCameraKey.Equals(cameraKey))
				camera5State.State = Sunny.UI.UILightState.Off;
		}

		public void OnCameraOpen(string cameraName, string cameraKey)
		{
			FastLogger.Instance.Info(string.Format("相机【{0}】连接", cameraKey));
			if (camera1SDK != null && camera1SDK.curCameraKey.Equals(cameraKey))
				camera1State.State = Sunny.UI.UILightState.On;
			else if (camera2SDK != null && camera2SDK.curCameraKey.Equals(cameraKey))
				camera2State.State = Sunny.UI.UILightState.On;
			else if (camera3SDK != null && camera3SDK.curCameraKey.Equals(cameraKey))
				camera3State.State = Sunny.UI.UILightState.On;
			else if (camera4SDK != null && camera4SDK.curCameraKey.Equals(cameraKey))
				camera4State.State = Sunny.UI.UILightState.On;
			else if (camera5SDK != null && camera5SDK.curCameraKey.Equals(cameraKey))
				camera5State.State = Sunny.UI.UILightState.On;
		}

		public void OnCameraConnectLoss(string cameraName, string cameraKey)
		{
			FastLogger.Instance.Info(string.Format("相机【{0}】丢失连接", cameraKey));
			if (camera1SDK != null && camera1SDK.curCameraKey.Equals(cameraKey))
			{
				camera1State.State = Sunny.UI.UILightState.Off;
				if (_cam1Reconnecting) return;
				_cam1Reconnecting = true;
				Task.Factory.StartNew(() =>
				{
					try
					{
						camera1SDK.Close();
						while (!camera1SDK.IsOpen() && !_isClosing && !_cts.Token.IsCancellationRequested)
						{
							try
							{
								FastLogger.Instance.Info(string.Format("相机{0}【{1}】重连", 1, cameraKey));
								camera1SDK.SetCameraByKey(_Config.Camera1SN);
								camera1SDK.Open();
							}
							catch { }
							Thread.Sleep(1000);
						}
					}
					finally { _cam1Reconnecting = false; }
				}, TaskCreationOptions.LongRunning);
			}
			else if (camera2SDK != null && camera2SDK.curCameraKey.Equals(cameraKey))
			{
				camera2State.State = Sunny.UI.UILightState.Off;
				if (_cam2Reconnecting) return;
				_cam2Reconnecting = true;
				Task.Factory.StartNew(() =>
				{
					try
					{
						camera2SDK.Close();
						while (!camera2SDK.IsOpen() && !_isClosing && !_cts.Token.IsCancellationRequested)
						{
							try
							{
								FastLogger.Instance.Info(string.Format("相机{0}【{1}】重连", 2, cameraKey));
								camera2SDK.SetCameraByKey(_Config.Camera2SN);
								camera2SDK.Open();
							}
							catch { }
							Thread.Sleep(1000);
						}
					}
					finally { _cam2Reconnecting = false; }
				}, TaskCreationOptions.LongRunning);
			}
			else if (camera3SDK != null && camera3SDK.curCameraKey.Equals(cameraKey))
			{
				camera3State.State = Sunny.UI.UILightState.Off;
				if (_cam3Reconnecting) return;
				_cam3Reconnecting = true;
				Task.Factory.StartNew(() =>
				{
					try
					{
						camera3SDK.Close();
						while (!camera3SDK.IsOpen() && !_isClosing && !_cts.Token.IsCancellationRequested)
						{
							try
							{
								FastLogger.Instance.Info(string.Format("相机{0}【{1}】重连", 3, cameraKey));
								camera3SDK.SetCameraByKey(_Config.Camera3SN);
								camera3SDK.Open();
							}
							catch { }
							Thread.Sleep(1000);
						}
					}
					finally { _cam3Reconnecting = false; }
				}, TaskCreationOptions.LongRunning);
			}
			else if (camera4SDK != null && camera4SDK.curCameraKey.Equals(cameraKey))
			{
				camera4State.State = Sunny.UI.UILightState.Off;
				if (_cam4Reconnecting) return;
				_cam4Reconnecting = true;
				Task.Factory.StartNew(() =>
				{
					try
					{
						camera4SDK.Close();
						while (!camera4SDK.IsOpen() && !_isClosing && !_cts.Token.IsCancellationRequested)
						{
							try
							{
								FastLogger.Instance.Info(string.Format("相机{0}【{1}】重连", 4, cameraKey));
								camera4SDK.SetCameraByKey(_Config.Camera4SN);
								camera4SDK.Open();
							}
							catch { }
							Thread.Sleep(1000);
						}
					}
					finally { _cam4Reconnecting = false; }
				}, TaskCreationOptions.LongRunning);
			}
			else if (camera5SDK != null && camera5SDK.curCameraKey.Equals(cameraKey))
			{
				camera5State.State = Sunny.UI.UILightState.Off;
				if (_cam5Reconnecting) return;
				_cam5Reconnecting = true;
				Task.Factory.StartNew(() =>
				{
					try
					{
						camera5SDK.Close();
						while (!camera5SDK.IsOpen() && !_isClosing && !_cts.Token.IsCancellationRequested)
						{
							try
							{
								FastLogger.Instance.Info(string.Format("相机{0}【{1}】重连", 5, cameraKey));
								camera5SDK.SetCameraByKey(_Config.Camera5SN);
								camera5SDK.Open();
							}
							catch { }
							Thread.Sleep(1000);
						}
					}
					finally { _cam5Reconnecting = false; }
				}, TaskCreationOptions.LongRunning);
			}
		}
		#endregion

		#region 相机图像处理方法（优化版）
		private void OnCamera1Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			if (_isClosing || _Config.cameraDebug != 0 || bitmap == null) return;
			if (_processor1 == null) { bitmap.Dispose(); return; }  // 工位未启用，丢弃图像
			try
			{
				long sequenceId = Interlocked.Increment(ref camera1Count);
				_imgRcvd1++;
				_processor1.AddImage(sequenceId, bitmap, offset_cam1, CameraSelect.Camera1);
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"相机一图像处理失败: {ex.Message}");
			}
		}

		private void OnCamera2Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			if (_isClosing || _Config.cameraDebug != 0 || bitmap == null) return;
			if (_processor2 == null) { bitmap.Dispose(); return; }
			try
			{
				long sequenceId = Interlocked.Increment(ref camera2Count);
				_imgRcvd2++;
				_processor2.AddImage(sequenceId, bitmap, offset_cam2, CameraSelect.Camera2);
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"相机二图像处理失败: {ex.Message}");
			}
		}

		private void OnCamera3Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			if (_isClosing || _Config.cameraDebug != 0 || bitmap == null) return;
			if (_processor3 == null) { bitmap.Dispose(); return; }
			try
			{
				long sequenceId = Interlocked.Increment(ref camera3Count);
				_imgRcvd3++;
				_processor3.AddImage(sequenceId, bitmap, offset_cam3, CameraSelect.Camera3);
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"相机三图像处理失败: {ex.Message}");
			}
		}

		private void OnCamera4Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			// 诊断：每次回调都计数，首个+每50个记录一次
			long callbackCount = Interlocked.Increment(ref _cam4CallbackCount);
			if (callbackCount <= 1 || callbackCount % 50 == 0)
				try { FastLogger.Instance.Info($"[回调] Camera4 #{callbackCount} 收到图片 bitmap={bitmap != null} bmpSize={bitmap?.Width}x{bitmap?.Height}"); } catch { }

			if (_isClosing) { try { FastLogger.Instance.Info("[回调] Camera4 被 _isClosing 拦截"); } catch { } return; }
			if (_Config.cameraDebug != 0) { try { FastLogger.Instance.Info($"[回调] Camera4 被 cameraDebug={_Config.cameraDebug} 拦截"); } catch { } return; }
			if (bitmap == null) { try { FastLogger.Instance.Info("[回调] Camera4 bitmap为null"); } catch { } return; }
			if (_processor4 == null) { try { FastLogger.Instance.Info("[回调] Camera4 _processor4为null，丢弃图片"); } catch { } bitmap.Dispose(); return; }
			try
			{
				long sequenceId = Interlocked.Increment(ref camera4Count);
				_imgRcvd4++;
				_processor4.AddImage(sequenceId, bitmap, offset_cam4, CameraSelect.Camera4);
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"相机四图像处理失败: {ex.Message}");
				try { FastLogger.Instance.Error("Camera4 AddImage异常", ex); } catch { }
			}
		}

		private void OnCamera5Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			// 诊断：每次回调都计数，首个+每50个记录一次
			long callbackCount = Interlocked.Increment(ref _cam5CallbackCount);
			if (callbackCount <= 1 || callbackCount % 50 == 0)
				try { FastLogger.Instance.Info($"[回调] Camera5 #{callbackCount} 收到图片 bitmap={bitmap != null} bmpSize={bitmap?.Width}x{bitmap?.Height}"); } catch { }

			if (_isClosing) { try { FastLogger.Instance.Info("[回调] Camera5 被 _isClosing 拦截"); } catch { } return; }
			if (_Config.cameraDebug != 0) { try { FastLogger.Instance.Info($"[回调] Camera5 被 cameraDebug={_Config.cameraDebug} 拦截"); } catch { } return; }
			if (bitmap == null) { try { FastLogger.Instance.Info("[回调] Camera5 bitmap为null"); } catch { } return; }
			if (_processor5 == null) { try { FastLogger.Instance.Info("[回调] Camera5 _processor5为null，丢弃图片"); } catch { } bitmap.Dispose(); return; }
			try
			{
				long sequenceId = Interlocked.Increment(ref camera5Count);
				_imgRcvd5++;
				_processor5.AddImage(sequenceId, bitmap, offset_cam5, CameraSelect.Camera5);
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"相机五图像处理失败: {ex.Message}");
				try { FastLogger.Instance.Error("Camera5 AddImage异常", ex); } catch { }
			}
		}
		#endregion

		#region 图像处理方法（优化版）- 保留原有逻辑，添加关闭检查
		private void ProcessCamera1Image(ImageProcessingContext context)
		{
			if (_isClosing) return;
			Stopwatch stageTimer = Stopwatch.StartNew();
			long id = context.SequenceId - context.Offset;

			Mat sourceMat = null;
			Mat labelImage = null;
			Mat labelImage1 = null;
			Mat compareMaskMat = null;
			Mat componentLabelsMat = null;
			Mat componentStatsMat = null;
			Mat componentCentroidsMat = null;
			Mat singleContourMaskMat = null;
			Bitmap displayBitmap = null;

			var pool = GetBufferPool(CameraSelect.Camera1);
			try
			{
				if (IFSaveLog)
					FastLogger.Instance.Debug($"time[{DateTime.Now:HH:mm:ss:fff}]相机一开始处理，ID: {id}");

				StartMethod(CameraSelect.Camera1);

				Bitmap bitmap = context.OriginalBitmap;
				bool result = true;
				double totalArea = 0;

				sourceMat = BitmapConverter.ToMat(bitmap);
				bool isGrayscale = sourceMat.Type() == MatType.CV_8UC1;

				if (pool != null)
				{
					labelImage = pool.RentMat();
					labelImage1 = pool.RentMat();
				}
				else
				{
					labelImage = new Mat();
					labelImage1 = new Mat();
				}

				if (isGrayscale)
				{
					Cv2.CvtColor(sourceMat, labelImage, ColorConversionCodes.GRAY2BGR);
					Cv2.CvtColor(sourceMat, labelImage1, ColorConversionCodes.GRAY2BGR);
				}
				else
				{
					sourceMat.CopyTo(labelImage);
					sourceMat.CopyTo(labelImage1);
				}

				stageTimer.Stop();
				context.StageTimes["图像前处理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				#region AI模型推理
				ResponseList<SegmentationResponse> rsp_segmentation = null;
				bool result_Segmentation = false;

				if (_cameraEnabled[0])
				{
					Model_Segmentation_Cam1.Run(labelImage, out rsp_segmentation);
				}

				stageTimer.Stop();
				context.StageTimes["AI模型推理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				int minArea_Camera1 = _Config.minArea_Camera1;
				int totalArea_Camera1 = _Config.totalArea_Camera1;

				if (rsp_segmentation != null)
				{
					compareMaskMat = new Mat();
					componentLabelsMat = new Mat();
					componentStatsMat = new Mat();
					componentCentroidsMat = new Mat();
					singleContourMaskMat = new Mat();

					for (int i = 0; i < rsp_segmentation.Count; i++)
					{
						var rspPair = rsp_segmentation[i];
						var rsp = rspPair.Item2;

						if (rsp.LabelMap != null)
						{
							var labelKeys = rsp.LabelMap.Keys.ToArray();
							for (int j = 0; j < labelKeys.Length; j++)
							{
								var kv = new KeyValuePair<string, int>(labelKeys[j], rsp.LabelMap[labelKeys[j]]);

								Cv2.Compare(rsp.Mask, Scalar.All(kv.Value), compareMaskMat, CmpType.EQ);
								var count_flaw = Cv2.ConnectedComponentsWithStats(compareMaskMat, componentLabelsMat, componentStatsMat, componentCentroidsMat);

								for (int k = 1; k < count_flaw; k++)
								{
									var area = componentStatsMat.At<int>(k, (int)ConnectedComponentsTypes.Area);
									if (area <= minArea_Camera1) continue;

									Cv2.Compare(componentLabelsMat, Scalar.All(k), singleContourMaskMat, CmpType.EQ);
									Cv2.FindContours(singleContourMaskMat, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
									if (contours.Length == 0) continue;

									RotatedRect minAreaRect = Cv2.MinAreaRect(contours[0]);
									var color = new Scalar(255, 0, 0);
									Cv2.DrawContours(labelImage1, contours, -1, new Scalar(0, 0, 255), 2);

									foreach (var ctr in contours)
									{
										if (RunLogEnabled) FastLogger.Instance.Debug($"相机一分割结果 Label：{kv.Key}, Area: {area}");
										totalArea += area;
									}

									DrawPointFast(labelImage1, minAreaRect.Center, color);
									DrawRotatedRectangleFast(labelImage1, minAreaRect, color);
								}
							}
						}
					}
					result_Segmentation = totalArea == 0 || totalArea <= totalArea_Camera1;
				}
				else
				{
					result_Segmentation = true;
					FastLogger.Instance.Error($"相机一分割模型出错：rsp_segmentation == null");
				}

				stageTimer.Stop();
				context.StageTimes["结果处理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();
				#endregion

				result = result_Segmentation;
				context.Result = new QueueResultItem
				{
					SequenceId = context.SequenceId,
					Offset = context.Offset,
					Result = result,
					Timestamp = DateTime.Now
				};

				if (!_isClosing)
				{
					// 【存图带字】先把界面同款中文信息画到结果Mat上，存图克隆与显示共用已绘制图像
					var swDraw1 = Stopwatch.StartNew();
					DrawOnMat(labelImage1, bmp => DrawGdiCam1(bmp, result, result_Segmentation, totalArea, totalArea_Camera1, id));
					swDraw1.Stop();
					context.StageTimes["存图绘制"] = swDraw1.ElapsedMilliseconds;
					CacheImageForDefectSave("Camera1", labelImage, labelImage1, id);

					// 降频刷新 UI（仅 1/3 帧显示，节约 GDI 渲染开销）
					if (id % 3 == 0)
					{
						displayBitmap = labelImage1.ToBitmap();   // 文字已绘制在Mat上，无需重复绘制
						UpdatePictureBox1(displayBitmap);
					}
				}

				stageTimer.Stop();
				context.StageTimes["存储图像"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				Interlocked.Increment(ref resultCount1);
				_resultMatcher?.SignalNewResult();
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"[Camera1] ID:{id} 处理异常: {ex.Message}\n{ex.StackTrace}");
				// context.Result 已在上方赋值，异常时保留该结果
				_resultMatcher?.SignalNewResult();
			}
			finally
			{
				compareMaskMat?.Dispose();
				componentLabelsMat?.Dispose();
				componentStatsMat?.Dispose();
				componentCentroidsMat?.Dispose();
				singleContourMaskMat?.Dispose();
				sourceMat?.Dispose();

				if (pool != null)
				{
					if (labelImage != null) pool.ReturnMat(labelImage);
					if (labelImage1 != null) pool.ReturnMat(labelImage1);
				}
				else
				{
					labelImage?.Dispose();
					labelImage1?.Dispose();
				}

				displayBitmap?.Dispose();

				EndMethod(CameraSelect.Camera1);
			}
		}


		private void ProcessCamera2Image(ImageProcessingContext context)
		{
			if (_isClosing) return;
			Stopwatch stageTimer = Stopwatch.StartNew();
			long id = context.SequenceId - context.Offset;

			Mat sourceMat = null;
			Mat labelImage = null;
			Mat labelImage1 = null;
			Bitmap displayBitmap = null;

			var pool = GetBufferPool(CameraSelect.Camera2);
			try
			{
				if (IFSaveLog)
					FastLogger.Instance.Debug($"time[{DateTime.Now:HH:mm:ss:fff}]相机二开始处理，ID: {id}");

				StartMethod(CameraSelect.Camera2);

				Bitmap bitmap = context.OriginalBitmap;
				bool result = false;

				sourceMat = BitmapConverter.ToMat(bitmap);
				bool isGrayscale = sourceMat.Type() == MatType.CV_8UC1;

				if (pool != null)
				{
					labelImage = pool.RentMat();
					labelImage1 = pool.RentMat();
				}
				else
				{
					labelImage = new Mat();
					labelImage1 = new Mat();
				}

				if (isGrayscale)
				{
					Cv2.CvtColor(sourceMat, labelImage, ColorConversionCodes.GRAY2BGR);
					Cv2.CvtColor(sourceMat, labelImage1, ColorConversionCodes.GRAY2BGR);
				}
				else
				{
					sourceMat.CopyTo(labelImage);
					sourceMat.CopyTo(labelImage1);
				}

				stageTimer.Stop();
				context.StageTimes["图像前处理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				#region AI模型推理 - 分类模型
				ResponseList<ClassificationResponse> rsp_class = null;
				bool result_flaw = false;
				string result_class = "";

				if (_cameraEnabled[1])
				{
					Model_Class_Cam2.Run(labelImage, out rsp_class);
				}

				stageTimer.Stop();
				context.StageTimes["AI模型推理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				var classBuilder = new System.Text.StringBuilder();

				if (rsp_class != null)
				{
					for (int i = 0; i < rsp_class.Count; i++)
					{
						var item = rsp_class[i];
						var labels = item.Item2?.Labels;

						if (labels != null)
						{
							foreach (var label in labels)
							{
								if (classBuilder.Length > 0)
								{
									classBuilder.Append("; ");
								}
								classBuilder.Append(label.Label);
							}
						}
					}

					result_class = classBuilder.ToString();
					result_flaw = string.IsNullOrEmpty(result_class);
				}
				else
				{
					result_flaw = true;
					FastLogger.Instance.Error($"分类模型出错：rsp_class == null");
				}

				stageTimer.Stop();
				context.StageTimes["结果处理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();
				#endregion

				result = result_flaw;
				context.Result = new QueueResultItem
				{
					SequenceId = context.SequenceId,
					Offset = context.Offset,
					Result = result,
					Timestamp = DateTime.Now
				};

				if (!_isClosing)
				{
					// 【存图带字】先把界面同款中文信息画到结果Mat上，存图克隆与显示共用已绘制图像
					var swDraw2 = Stopwatch.StartNew();
					DrawOnMat(labelImage1, bmp => DrawGdiCam2(bmp, result, result_class, id));
					swDraw2.Stop();
					context.StageTimes["存图绘制"] = swDraw2.ElapsedMilliseconds;
					CacheImageForDefectSave("Camera2", labelImage, labelImage1, id);

					// 降频刷新 UI（仅 1/3 帧显示，节约 GDI 渲染开销）
					if (id % 3 == 0)
					{
						displayBitmap = labelImage1.ToBitmap();   // 文字已绘制在Mat上，无需重复绘制
						UpdatePictureBox2(displayBitmap);
					}
				}

				stageTimer.Stop();
				context.StageTimes["存储图像"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				Interlocked.Increment(ref resultCount2);
				_resultMatcher?.SignalNewResult();
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"[Camera2] ID:{id} 处理异常: {ex.Message}\n{ex.StackTrace}");
				_resultMatcher?.SignalNewResult();
			}
			finally
			{
				sourceMat?.Dispose();
				if (pool != null)
				{
					if (labelImage != null) pool.ReturnMat(labelImage);
					if (labelImage1 != null) pool.ReturnMat(labelImage1);
				}
				else
				{
					labelImage?.Dispose();
					labelImage1?.Dispose();
				}

				displayBitmap?.Dispose();

				EndMethod(CameraSelect.Camera2);
			}
		}

		private void ProcessCamera3Image(ImageProcessingContext context)
		{
			if (_isClosing) return;
			Stopwatch stageTimer = Stopwatch.StartNew();
			long id = context.SequenceId - context.Offset;

			Mat sourceMat = null;
			Mat labelImage = null;
			Mat resultImage = null;
			Bitmap displayBitmap = null;

			var pool = GetBufferPool(CameraSelect.Camera3);
			try
			{
				if (IFSaveLog)
					FastLogger.Instance.Debug($"time[{DateTime.Now:HH:mm:ss:fff}]相机三开始处理，ID: {id}");

				StartMethod(CameraSelect.Camera3);

				Bitmap bitmap = context.OriginalBitmap;
				bool result = false;
				double roundness = 0;
				double PipeDiameter = _Config.Camera3PipeDiameter;
				double longEdge = 0;

				sourceMat = BitmapConverter.ToMat(bitmap);
				bool isGrayscale = sourceMat.Type() == MatType.CV_8UC1;

				if (pool != null)
				{
					labelImage = pool.RentMat();
				}
				else
				{
					labelImage = new Mat();
				}

				if (isGrayscale)
				{
					Cv2.CvtColor(sourceMat, labelImage, ColorConversionCodes.GRAY2BGR);
				}
				else
				{
					sourceMat.CopyTo(labelImage);
				}

				stageTimer.Stop();
				context.StageTimes["图像前处理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				#region 圆度检测
				if (labelImage.Empty())
				{
					FastLogger.Instance.Error($"相机三图像异常..");
					resultImage = labelImage.Clone();
				}
				else
				{
					DetectionResultV3 detectionResult = null;
					if (_cameraEnabled[2])
					{
						detectionResult = RoundnessDetectorV3.DetectRoundnessAndRect(labelImage);
					}

					stageTimer.Stop();
					context.StageTimes["AI模型推理"] = stageTimer.ElapsedMilliseconds;
					stageTimer.Restart();

					if (detectionResult != null)
					{
						resultImage = RoundnessDetectorV3.VisualizeDetection(labelImage, detectionResult);
						longEdge = detectionResult.LongEdge * _Config.K_Cam3;
						roundness = Math.Round((longEdge - PipeDiameter) / PipeDiameter, 3);
						result = _Config.Camera3RoundnessUp >= roundness && roundness >= _Config.Camera3RoundnessDown;
						context.ProcessResult = result;

						if (IFSaveLog)
						{
							if (FastLogger.DebugEnabled) FastLogger.Instance.Debug($"[Camera3] ID:{id} 圆度检测 - LongEdge:{longEdge:F3}, PipeDiameter:{PipeDiameter}, Roundness:{roundness:F3}, Up:{_Config.Camera3RoundnessUp}, Down:{_Config.Camera3RoundnessDown}, Result:{result}");
						}
					}
					else
					{
						if (FastLogger.DebugEnabled) FastLogger.Instance.Debug($"[Camera3] ID:{id} 圆度检测失败，设置为OK");
						result = true;
						context.ProcessResult = result;
						resultImage = labelImage.Clone();
					}
				}

				stageTimer.Stop();
				context.StageTimes["结果处理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				if (!_isClosing)
				{
					// 【存图带字】先把界面同款中文信息画到结果Mat上，存图克隆与显示共用已绘制图像
					var swDraw3 = Stopwatch.StartNew();
					DrawOnMat(resultImage, bmp => DrawGdiCam3(bmp, result, longEdge, PipeDiameter, roundness, id));
					swDraw3.Stop();
					context.StageTimes["存图绘制"] = swDraw3.ElapsedMilliseconds;
					CacheImageForDefectSave("Camera3", labelImage, resultImage, id);

					// 降频刷新 UI（仅 1/3 帧显示，节约 GDI 渲染开销）
					if (id % 3 == 0)
					{
						stageTimer.Stop();
						context.StageTimes["图像绘制"] = stageTimer.ElapsedMilliseconds;
						stageTimer.Restart();

						displayBitmap = resultImage.ToBitmap();   // 文字已绘制在Mat上，无需重复绘制
						UpdatePictureBox3(displayBitmap);

						stageTimer.Stop();
						context.StageTimes["显示更新"] = stageTimer.ElapsedMilliseconds;
						stageTimer.Restart();
					}
				}

				stageTimer.Stop();
				context.StageTimes["存储图像"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				if (IFSaveLog)
				{
					if (RunLogEnabled) FastLogger.Instance.Debug($"[Camera3] ID:{id} 最终结果 - ProcessResult:{result}, SequenceId:{context.SequenceId}, Offset:{context.Offset}");
				}

				Interlocked.Increment(ref resultCount3);
				_resultMatcher?.SignalNewResult();
				#endregion
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"[Camera3] ID:{context.SequenceId} 处理异常: {ex.Message}\n{ex.StackTrace}");
				// ProcessResult已在上方设置，异常时保留原值
				_resultMatcher?.SignalNewResult();
			}
			finally
			{
				sourceMat?.Dispose();
				if (pool != null)
				{
					if (labelImage != null) pool.ReturnMat(labelImage);
					resultImage?.Dispose();
				}
				else
				{
					labelImage?.Dispose();
					resultImage?.Dispose();
				}

				displayBitmap?.Dispose();

				EndMethod(CameraSelect.Camera3);
			}
		}

		private void ProcessCamera4Image(ImageProcessingContext context)
		{
			if (_isClosing) return;
			Stopwatch stageTimer = Stopwatch.StartNew();
			long id = context.SequenceId - context.Offset;

			Mat sourceMat = null;
			Mat labelImage = null;
			Mat labelImage1 = null;
			Bitmap displayBitmap = null;

			var pool = GetBufferPool(CameraSelect.Camera4);
			try
			{
				if (IFSaveLog)
					FastLogger.Instance.Debug($"time[{DateTime.Now:HH:mm:ss:fff}]相机四开始处理，ID: {id}");

				StartMethod(CameraSelect.Camera4);

				Bitmap bitmap = context.OriginalBitmap;
				bool result = false;
				bool result_char = false;
				string order_ocr = "";
				int camera4StandChar = _Config.Camera1StandChar;

				sourceMat = BitmapConverter.ToMat(bitmap);
				bool isGrayscale = sourceMat.Type() == MatType.CV_8UC1;

				if (pool != null)
				{
					labelImage = pool.RentMat();
					labelImage1 = pool.RentMat();
				}
				else
				{
					labelImage = new Mat();
					labelImage1 = new Mat();
				}

				if (isGrayscale)
				{
					Cv2.CvtColor(sourceMat, labelImage, ColorConversionCodes.GRAY2BGR);
					Cv2.CvtColor(sourceMat, labelImage1, ColorConversionCodes.GRAY2BGR);
				}
				else
				{
					sourceMat.CopyTo(labelImage);
					sourceMat.CopyTo(labelImage1);
				}

				stageTimer.Stop();
				context.StageTimes["图像前处理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				#region 字符识别
				ResponseList<OcrResponse> rsp_ocr = null;
				ResponseList<SegmentationResponse> rsp_segmentation = null;
				string result_Char_str = "0";
				int index_ocr = 0;

				var m4SwSeg = new Stopwatch();
				var m4SwOcr = new Stopwatch();

				if (_cameraEnabled[3])
				{
					_cam4Tasks[0] = Task.Run(() => { m4SwSeg.Start(); Model_Segmentation_Cam4.Run(labelImage, out rsp_segmentation); m4SwSeg.Stop(); });
					_cam4Tasks[1] = Task.Run(() => { m4SwOcr.Start(); Model_Char_Cam4.Run(labelImage, out rsp_ocr); m4SwOcr.Stop(); });
					Task.WaitAll(_cam4Tasks);
				}

				stageTimer.Stop();
				context.StageTimes["AI模型推理"] = stageTimer.ElapsedMilliseconds;
				context.StageTimes["  AI-分割"] = m4SwSeg.ElapsedMilliseconds;
				context.StageTimes["  AI-OCR"] = m4SwOcr.ElapsedMilliseconds;
				stageTimer.Restart();

				if (rsp_segmentation != null && rsp_ocr != null)
				{
					int segmentationCount = 0;
					for (int i = 0; i < rsp_segmentation.Count; i++)
					{
						segmentationCount += rsp_segmentation[i].Item2.LabelMap.Count;
					}

					if (segmentationCount > 0)
					{
						var ocrBuilder = new System.Text.StringBuilder();

						if (rsp_ocr != null)
						{
							for (int i = 0; i < rsp_ocr.Count; i++)
							{
								if (_Config.IFCamera4NG)
								{
									result_Char_str += "1";
								}
								var item = rsp_ocr[i];
								var blocks = item.Item2?.Blocks;

								if (blocks != null)
								{
									foreach (var block in blocks)
									{
										index_ocr++;
										ocrBuilder.Append(block.Label);
										var bbox = Cv2.BoundingRect(block.Polygon);

										Cv2.Rectangle(labelImage1, bbox, new Scalar(0, 255, 0), 2);
										Cv2.PutText(labelImage1, block.Label,
											new OpenCvSharp.Point(bbox.X, bbox.Y - 5),
											HersheyFonts.HersheySimplex, 0.8,
											new Scalar(0, 255, 0), 2);
									}
								}
							}

							order_ocr = ocrBuilder.ToString();
						}
						else
						{
							if (_Config.IFCamera4NG)
							{
								result_Char_str += "1";
							}
							result_Char_str += "0";
							FastLogger.Instance.Error($"相机四OCR模型出错：rsp_ocr == null");
						}

						if (index_ocr > 0)
						{
							if (index_ocr >= camera4StandChar)
							{
								result_Char_str += "0";
							}
							else
							{
								result_Char_str += "1";
							}
							order_ocr = $"Char Count: {index_ocr} / {camera4StandChar};";
						}
						else
						{
							order_ocr = $"Char Count: 0;";
							result_Char_str += "1";
						}

						result_char = Convert.ToInt32(result_Char_str, 2) == 0;
						if (RunLogEnabled) FastLogger.Instance.Debug($"[Camera4] ID:{id} 字符检测 - IndexOCR:{index_ocr}, StandChar:{camera4StandChar}, ResultStr:{result_Char_str}, ResultChar:{result_char}");
						if (!result_char && rsp_ocr != null)
					{
						foreach (var item in rsp_ocr)
						{
							var blocks = item.Item2?.Blocks;
							if (blocks != null)
							{
								foreach (var block in blocks)
									Cv2.Rectangle(labelImage1, Cv2.BoundingRect(block.Polygon), new Scalar(0, 0, 255), 2);
							}
						}
					}
					}
					else
					{
						order_ocr = $"Char Count: Workpiece Not Found";
						result_char = true;
						if (FastLogger.DebugEnabled) FastLogger.Instance.Debug($"[Camera4] ID:{id} 工件未找到");
					}
				}
				else
				{
					if (rsp_segmentation == null)
					{
						if (FastLogger.DebugEnabled) FastLogger.Instance.Debug($"[Camera4] ID:{id} rsp_segmentation == null");
						order_ocr = $"Cam4 rsp_segmentation == null;";
					}
					else if (rsp_ocr == null)
					{
						order_ocr = $"Cam4 result_char == null;";
						if (FastLogger.DebugEnabled) FastLogger.Instance.Debug($"[Camera4] ID:{id} result_char == null");
					}
					result_char = true;
				}
				result = result_char;
				context.Result = new QueueResultItem
				{
					SequenceId = context.SequenceId,
					Offset = context.Offset,
					Result = result,
					Timestamp = DateTime.Now
				};
				#endregion

				stageTimer.Stop();
				context.StageTimes["结果处理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				if (!_isClosing)
				{
					// 【存图带字】先把界面同款中文信息画到结果Mat上，存图克隆与显示共用已绘制图像
					var swDraw4 = Stopwatch.StartNew();
					DrawOnMat(labelImage1, bmp => DrawGdiCam4(bmp, result, result_char, order_ocr, id));
					swDraw4.Stop();
					context.StageTimes["存图绘制"] = swDraw4.ElapsedMilliseconds;
					CacheImageForDefectSave("Camera4", labelImage, labelImage1, id);

					// 降频刷新 UI（仅 1/3 帧显示，节约 GDI 渲染开销）
					if (id % 3 == 0)
					{
						stageTimer.Stop();
						context.StageTimes["图像绘制"] = stageTimer.ElapsedMilliseconds;
						stageTimer.Restart();

						displayBitmap = labelImage1.ToBitmap();   // 文字已绘制在Mat上，无需重复绘制
						UpdatePictureBox4(displayBitmap);

						stageTimer.Stop();
						context.StageTimes["显示更新"] = stageTimer.ElapsedMilliseconds;
						stageTimer.Restart();
					}
				}

				stageTimer.Stop();
				context.StageTimes["存储图像"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				if (RunLogEnabled) FastLogger.Instance.Debug($"[Camera4] ID:{id} 处理完成 - result_char:{result_char}, result:{result}");
				Interlocked.Increment(ref resultCount4);
				_resultMatcher?.SignalNewResult();
				if (RunLogEnabled && resultCount4 % 10 == 0) try { FastLogger.Instance.Debug("状态对比: imgRcvd4=" + _imgRcvd4 + " proc4=" + resultCount4 + " | imgRcvd5=" + _imgRcvd5 + " proc5=" + resultCount5 + " diff=" + (_imgRcvd4 - _imgRcvd5)); } catch { }
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"[Camera4] ID:{id} 处理异常: {ex.Message}\n{ex.StackTrace}");
				// context.Result 已在上方赋值，异常时保留该结果
				_resultMatcher?.SignalNewResult();
			}
			finally
			{
				sourceMat?.Dispose();
				if (pool != null)
				{
					if (labelImage != null) pool.ReturnMat(labelImage);
					if (labelImage1 != null) pool.ReturnMat(labelImage1);
				}
				else
				{
					labelImage?.Dispose();
					labelImage1?.Dispose();
				}

				displayBitmap?.Dispose();

				EndMethod(CameraSelect.Camera4);
			}
		}

		private void ProcessCamera5Image(ImageProcessingContext context)
		{
			if (_isClosing) return;
			Stopwatch stageTimer = Stopwatch.StartNew();
			long id = context.SequenceId - context.Offset;

			Mat sourceMat = null;
			Mat labelImage = null;
			Mat labelImage1 = null;
			Bitmap displayBitmap = null;

			var pool = GetBufferPool(CameraSelect.Camera5);
			try
			{
				if (IFSaveLog)
					FastLogger.Instance.Debug($"time[{DateTime.Now:HH:mm:ss:fff}]相机五开始处理，ID: {id}");

				StartMethod(CameraSelect.Camera5);

				Bitmap bitmap = context.OriginalBitmap;
				bool isEmptyCup = false; // 空杯标记
				bool result = true;
				bool result_flaw = true;
				bool result_char = true;
				bool result_PCode_char = true;
				bool result_Segmentation = true;
				string result_Class_str = "0";
				string result_class = "";
				string order_ocr = "字符数量: 0;";
				string pcode_ocr = "P码数量: 0;";
				double projectionLength = 0;
				string label_str = "";

				sourceMat = BitmapConverter.ToMat(bitmap);
				bool isGrayscale = sourceMat.Type() == MatType.CV_8UC1;

				if (pool != null)
				{
					labelImage = pool.RentMat();
					labelImage1 = pool.RentMat();
				}
				else
				{
					labelImage = new Mat();
					labelImage1 = new Mat();
				}

				if (isGrayscale)
				{
					Cv2.CvtColor(sourceMat, labelImage, ColorConversionCodes.GRAY2BGR);
					Cv2.CvtColor(sourceMat, labelImage1, ColorConversionCodes.GRAY2BGR);
				}
				else
				{
					sourceMat.CopyTo(labelImage);
					sourceMat.CopyTo(labelImage1);
				}

				stageTimer.Stop();
				context.StageTimes["图像前处理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				#region 多模型并行推理
				ResponseList<SegmentationResponse> rsp_segmentation = null;
				ResponseList<SegmentationResponse> rsp_color = null;
				ResponseList<SegmentationResponse> rsp_rests = null;
				ResponseList<OcrResponse> rsp_ocr = null;
				ResponseList<OcrResponse> rsp_PCode_ocr = null;

				var mSwSeg = new Stopwatch();
				var mSwOcr = new Stopwatch();
				var mSwColor = new Stopwatch();
				var mSwPCode = new Stopwatch();
				var mSwRests = new Stopwatch();

				if (_cameraEnabled[4])
				{
					_cam5Tasks[0] = Task.Run(() => { mSwSeg.Start(); Model_Segmentation_Cam5.Run(labelImage, out rsp_segmentation); mSwSeg.Stop(); });
					_cam5Tasks[1] = Task.Run(() => { mSwOcr.Start(); Model_Char_Cam5.Run(labelImage, out rsp_ocr); mSwOcr.Stop(); });
					_cam5Tasks[2] = Task.Run(() => { mSwColor.Start(); Model_Color_Cam5.Run(labelImage, out rsp_color); mSwColor.Stop(); });
					_cam5Tasks[3] = Task.Run(() => { mSwPCode.Start(); Model_Char_PCode_Cam5.Run(labelImage, out rsp_PCode_ocr); mSwPCode.Stop(); });
					_cam5Tasks[4] = Task.Run(() => { mSwRests.Start(); Model_Rests_Cam5.Run(labelImage, out rsp_rests); mSwRests.Stop(); });
					Task.WaitAll(_cam5Tasks);
				}

				stageTimer.Stop();
				context.StageTimes["AI模型推理"] = stageTimer.ElapsedMilliseconds;
				// 各模型单独耗时（并行执行，各自计时）
				context.StageTimes["  AI-分割"] = mSwSeg.ElapsedMilliseconds;
				context.StageTimes["  AI-OCR"] = mSwOcr.ElapsedMilliseconds;
				context.StageTimes["  AI-色标"] = mSwColor.ElapsedMilliseconds;
				context.StageTimes["  AI-PCode"] = mSwPCode.ElapsedMilliseconds;
				context.StageTimes["  AI-缺陷"] = mSwRests.ElapsedMilliseconds;
				stageTimer.Restart();
				#endregion

				float k = Convert.ToSingle(_Config.K);
				float offset = Convert.ToSingle(_Config.Offset);
				float astrict = Convert.ToSingle(_Config.Astrict);
				int camera5StandChar = _Config.Camera2StandChar;

				#region 处理OCR结果
				string result_Char_str = "0";
				int index_ocr = 0;
				var ocrBuilder = new System.Text.StringBuilder();

				if (rsp_ocr != null)
				{
					for (int i = 0; i < rsp_ocr.Count; i++)
					{
						var item = rsp_ocr[i];
						var blocks = item.Item2?.Blocks;

						if (blocks != null)
						{
							foreach (var block in blocks)
							{
								index_ocr++;
								ocrBuilder.Append(block.Label);
								var bbox = Cv2.BoundingRect(block.Polygon);

								Cv2.Rectangle(labelImage1, bbox, new Scalar(0, 255, 0), 2);
								Cv2.PutText(labelImage1, block.Label,
									new OpenCvSharp.Point(bbox.X, bbox.Y - 5),
									HersheyFonts.HersheySimplex, 0.8,
									new Scalar(0, 255, 0), 2);
							}
						}
					}

					if (index_ocr > 0)
					{
						if (index_ocr >= camera5StandChar)
						{
							result_Char_str += "0";
						}
						else
						{
							result_Char_str += "1";
						}
						order_ocr = $"字符数量: {index_ocr} / {camera5StandChar};";
					}
					else
					{
						order_ocr = "字符数量: 0;";
						result_Char_str += "1";
					}
				}
				else
				{
					result_Char_str += "0";
					FastLogger.Instance.Error($"OCR模型出错：rsp_ocr == null");
				}

				if (_Config.Camera5IFOcr)
				{
					result_char = Convert.ToInt32(result_Char_str, 2) == 0;
				}
				else
				{
					result_char = true;
					if (!result_char && rsp_ocr != null)
					{
						foreach (var item in rsp_ocr)
						{
							var blocks = item.Item2?.Blocks;
							if (blocks != null)
							{
								foreach (var block in blocks)
									Cv2.Rectangle(labelImage1, Cv2.BoundingRect(block.Polygon), new Scalar(0, 0, 255), 2);
							}
						}
					}
				}
				#endregion

				#region 处理P-Code结果
				string result_Char_PCode_str = "0";
				string PCode = "";
				index_ocr = 0;

				if (rsp_PCode_ocr != null)
				{
					for (int i = 0; i < rsp_PCode_ocr.Count; i++)
					{
						var item = rsp_PCode_ocr[i];
						var blocks = item.Item2?.Blocks;

						if (blocks != null)
						{
							var rightToLeft = TextBlockSorter.SortByCenterX(blocks.ToList(), TextBlockSorter.SortDirection.RightToLeft);

							foreach (var block in rightToLeft)
							{
								index_ocr++;
								PCode += block.Label;
								var bbox = Cv2.BoundingRect(block.Polygon);

								Cv2.Rectangle(labelImage1, bbox, new Scalar(0, 255, 0), 1);
								Cv2.PutText(labelImage1, block.Label,
									new OpenCvSharp.Point(bbox.X, bbox.Y - 5),
									HersheyFonts.HersheySimplex, 0.8,
									new Scalar(0, 255, 0), 2);
							}
						}
					}

					if (index_ocr > 0)
					{
						if (_Config.Standard_PCode == PCode)
						{
							result_Char_PCode_str += "0";
						}
						else
						{
							result_Char_PCode_str += "1";
						}
						pcode_ocr = $"P码内容: {PCode}";
					}
					else
					{
						pcode_ocr = "P码数量: 0;";
						result_Char_PCode_str += "1";
					}
				}
				else
				{
					result_Char_PCode_str += "0";
					FastLogger.Instance.Error($"OCR模型出错：rsp_ocr == null");
				}

				if (_Config.Camera5IFPCode)
				{
					result_PCode_char = Convert.ToInt32(result_Char_PCode_str, 2) == 0;
				}
				else
				{
					result_PCode_char = true;
				}
				if (!result_PCode_char && rsp_PCode_ocr != null)
				{
					foreach (var item in rsp_PCode_ocr)
					{
						var blocks = item.Item2?.Blocks;
						if (blocks != null)
						{
							foreach (var block in blocks)
								Cv2.Rectangle(labelImage1, Cv2.BoundingRect(block.Polygon), new Scalar(0, 0, 255), 2);
						}
					}
				}
				#endregion

				#region 处理分割结果
				SegmentationResult segmentationResult = new SegmentationResult();
				if (rsp_color != null && rsp_color.Count > 0 && rsp_segmentation != null && rsp_segmentation.Count > 0 && rsp_rests != null && rsp_rests.Count > 0)
				{
					segmentationResult = ProcessSegmentationResultsFast(
						rsp_segmentation, rsp_color, rsp_rests, labelImage1, ref result_class, ref result_Class_str, ref label_str);

					if (segmentationResult.DetectedBoth)
					{
						if (segmentationResult.SeBiaoX != 0 && segmentationResult.SeBiaoY != 0)
						{
							projectionLength = CalculateProjection(
								segmentationResult.SeBiaoX, segmentationResult.SeBiaoY,
								segmentationResult.PingShengX, segmentationResult.PingShengY,
								segmentationResult.PingShengA);

							projectionLength = projectionLength * k + offset;

							if (_Config.Camera5IFSeBiao)
							{
								result_Segmentation = projectionLength <= astrict;

								if (result_Segmentation)
								{
									DrawLine(labelImage1,
										new OpenCvSharp.Point((int)segmentationResult.SeBiaoX, (int)segmentationResult.SeBiaoY),
										new OpenCvSharp.Point((int)segmentationResult.PingShengX, (int)segmentationResult.PingShengY),
										new Scalar(0, 255, 0));
								}
								else
								{
									DrawLine(labelImage1,
										new OpenCvSharp.Point((int)segmentationResult.SeBiaoX, (int)segmentationResult.SeBiaoY),
										new OpenCvSharp.Point((int)segmentationResult.PingShengX, (int)segmentationResult.PingShengY),
										new Scalar(0, 0, 255));
								}
							}
							else
							{
								result_Segmentation = true;
							}
						}
					}
					else
					{
						result_Segmentation = false;
					}
				}
				else
				{
					result_Segmentation = true;
				}

				result_flaw = Convert.ToInt32(result_Class_str, 2) == 0;
				#endregion

				if (label_str.Contains("空杯"))
				{
					result_char = true;
					result_PCode_char = true;
					isEmptyCup = true;
					if (FastLogger.DebugEnabled) FastLogger.Instance.Debug($"[Camera5] ID:{id} 空杯产品");
				}
				result = result_char && result_PCode_char && result_flaw && result_Segmentation;

				// 兜底：NG但无缺陷分类时（模型偶发异常），用原始标签强制归类
				if (!result && result_char && result_PCode_char && result_flaw && result_Segmentation)
				{
					// 所有子项都OK但综合结果NG（理论上不可能，兜底保护）
					result_char = false; // 默认归为"背面工号缺失"
					if (FastLogger.DebugEnabled) FastLogger.Instance.Debug($"[Camera5] ID:{id} ⚠ 兜底触发! 综合NG但无缺陷分类, 原始标签:[{label_str}], result_class:[{result_class}]");
					result = false;
				}
				else if (!result && !result_flaw && string.IsNullOrEmpty(result_class))
				{
					if (FastLogger.DebugEnabled) FastLogger.Instance.Debug($"[Camera5] ID:{id} ⚠ 缺陷模型标记NG但result_class为空! 原始标签:[{label_str}], result_Class_str:[{result_Class_str}]");
					// 用label_str兜底：写入原始模型标签
					if (!string.IsNullOrEmpty(label_str))
						result_class = "标签:" + label_str.Replace(";", ",").TrimEnd(',', ' ');
				}

				context.ProcessResult = result;
				if (RunLogEnabled) FastLogger.Instance.Debug($"[Camera5] ID:{id} 最终结果 - result_char:{result_char}, result_PCode_char:{result_PCode_char}, result_flaw:{result_flaw}, result_Segmentation:{result_Segmentation}, FinalResult:{result}");

				stageTimer.Stop();
				context.StageTimes["结果处理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				if (!_isClosing)
				{
					// 【存图带字】先把界面同款中文信息画到结果Mat上，存图克隆与显示共用已绘制图像
					var swDraw5 = Stopwatch.StartNew();
					DrawOnMat(labelImage1, bmp => DrawGdiCam5(bmp, result, result_char, result_PCode_char, result_flaw, result_Segmentation, result_class, order_ocr, pcode_ocr, projectionLength, segmentationResult, id));
					swDraw5.Stop();
					context.StageTimes["存图绘制"] = swDraw5.ElapsedMilliseconds;
					// 始终缓存图像用于存图（无论是否积压）
					CacheImageForDefectSave("Camera5", labelImage, labelImage1, id);

					int queueDepth = _processor5?.ImageQueueCount ?? 0;
					bool needOutput = queueDepth <= 2;

					// Camera5积压时跳过显示，省50ms提速追赶
					if (needOutput)
					{
						displayBitmap = labelImage1.ToBitmap();   // 文字已绘制在Mat上，无需重复绘制
						UpdatePictureBox5(displayBitmap);
					}
					else if (queueDepth % 10 == 0)
					{
						if (RunLogEnabled) FastLogger.Instance.Debug($"[Camera5] 积压{queueDepth}帧，跳过显示提效");
					}

					stageTimer.Stop();
					context.StageTimes["显示更新及存储图像"] = stageTimer.ElapsedMilliseconds;
					stageTimer.Restart();
				}


				var cam5Result = new QueueResultItem
				{
					SequenceId = context.SequenceId,
					Offset = context.Offset,
					Result = result,
					Timestamp = DateTime.Now,
					Cam5_CharResult = result_char ? 1 : 0,
					Cam5_PCodeResult = result_PCode_char ? 1 : 0,
					Cam5_SebiaoResult = result_Segmentation ? 1 : 0,
					Cam5_BaoguanResult = result_class.Contains("爆管") ? 0 : 1,
					Cam5_XiekouResult = result_class.Contains("斜口") ? 0 : 1,
					Cam5_WeijianduanResult = result_class.Contains("未剪断") ? 0 : 1,
					IsEmptyCup = isEmptyCup
				};

				if (!result && result_flaw == false &&
					result_char && result_PCode_char && result_Segmentation &&
					!result_class.Contains("斜口") && !result_class.Contains("未剪断"))
				{
					cam5Result.IsPureBurst = true;
				}

				context.Result = cam5Result;

				Interlocked.Increment(ref resultCount5);
				_resultMatcher?.SignalNewResult();
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"[Camera5] ID:{context.SequenceId} 处理异常: {ex.Message}\n{ex.StackTrace}");
				// context.Result / context.ProcessResult 已在上方赋值，异常时保留原值
				_resultMatcher?.SignalNewResult();
			}
			finally
			{
				sourceMat?.Dispose();
				if (pool != null)
				{
					if (labelImage != null) pool.ReturnMat(labelImage);
					if (labelImage1 != null) pool.ReturnMat(labelImage1);
				}
				else
				{
					labelImage?.Dispose();
					labelImage1?.Dispose();
				}

				displayBitmap?.Dispose();

				EndMethod(CameraSelect.Camera5);
			}
		}
		#endregion

		#region 高速保存方法
		private void SaveImageIfNeeded_Fast(string cameraName, Mat original, Mat result, bool resultBool, long SequenceId, int Offset)
		{
			if (_isClosing) return;

			var timer = Stopwatch.StartNew();
			try
			{
				IFSaveOKImage = _Config.IsSaveOkImage;
				IFSaveNGImage = _Config.IsSaveNgImage;
				IFSaveOKRawImage = _Config.IsSaveOkRawImage;
				IFSaveNGRawImage = _Config.IsSaveNgRawImage;

				if (!IFSaveOKImage && !IFSaveNGImage && !IFSaveOKRawImage && !IFSaveNGRawImage)
					return;

				bool shouldSave = (resultBool && IFSaveOKImage) || (resultBool && IFSaveOKRawImage) ||
								  (!resultBool && IFSaveNGImage) || (!resultBool && IFSaveNGRawImage);
				if (!shouldSave) return;

				if (original == null && result == null) return;

				string dtFormat = DateTime.Now.ToString("yyMMddHHmmssfff");
				string dateFolder = DateTime.Now.ToString("yyMMdd");
				string shiftFolder = _currentShift;
				string skuFolder = GetCurrentSkuValue();
				string resultFolder = resultBool ? "OK" : "NG";

				// 根据相机和检测结果确定缺陷类型文件夹（存图时不考虑连续爆管剔除）
				string defectFolder = GetDefectFolder(cameraName, resultBool);

				var saver = GetHighSpeedSaver(cameraName);
				if (saver == null) return;

				// 构建完整路径：日期 -> 班次 -> SKU -> OK/NG -> 缺陷类型
				string basePath = Path.Combine(_Config.ImagePath, dateFolder, shiftFolder, skuFolder, resultFolder);
				if (!resultBool && !string.IsNullOrEmpty(defectFolder))
				{
					basePath = Path.Combine(basePath, defectFolder);
				}

				if (original != null && ((resultBool && IFSaveOKRawImage) || (!resultBool && IFSaveNGRawImage)))
				{
					SaveOriginalImage_Fast(saver, cameraName, original, basePath, dtFormat, SequenceId, Offset, resultBool);
				}

				if (result != null && ((resultBool && IFSaveOKImage) || (!resultBool && IFSaveNGImage)))
				{
					SaveResultImage_Fast(saver, cameraName, result, basePath, dtFormat, SequenceId, Offset, resultBool);
				}

				Interlocked.Increment(ref _totalSaveCount);
				Interlocked.Add(ref _totalSaveTimeMs, timer.ElapsedMilliseconds);

				if (_totalSaveCount % 100 == 0)
				{
					double avgTime = (double)_totalSaveTimeMs / _totalSaveCount;
					try { if (FastLogger.IsInitialized) FastLogger.Instance.Info($"高速保存平均耗时: {avgTime:F2}ms, 总次数: {_totalSaveCount}"); } catch { }
				}
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"{cameraName} 高速保存异常: {ex.Message}");
			}
			finally
			{
				if (timer.ElapsedMilliseconds > 10)
				{
					try { if (FastLogger.IsInitialized) FastLogger.Instance.Info($"{cameraName} 保存耗时较高: {timer.ElapsedMilliseconds}ms"); } catch { }
				}
			}
		}

		/// <summary>
		/// 根据相机名称和检测结果获取缺陷类型文件夹名称
		/// 存图时不考虑连续爆管剔除
		/// </summary>
		private string GetDefectFolder(string cameraName, bool isOk)
		{
			if (isOk) return "";

			switch (cameraName)
			{
				case "Camera1":
					return "管内异物";
				case "Camera2":
					return "管盖有无";
				case "Camera3":
					return "管口圆度";
				case "Camera4":
					return "正面工号缺失";
				case "Camera5":
					return "夹尾反面缺陷";
				default:
					return "其他缺陷";
			}
		}

		private void SaveOriginalImage_Fast(HighSpeedImageSaver saver, string cameraName, Mat original, string basePath, string dtFormat, long SequenceId, int Offset, bool resultBool)
		{
			try
			{
				string yFileName = $"{dtFormat}_Y_ID{SequenceId - Offset}_SequenceId{SequenceId}_Offset{Offset}_result-{resultBool}-{(resultBool ? "OK" : "NG")}.jpg";
				string yPath = Path.Combine(basePath, yFileName);

				byte[] yData = BitmapFastConverter.ToJpegBytesViaOpenCv(original, IMAGE_JPEG_QUALITY);
				if (yData != null && yData.Length > 0)
				{
					saver.AddSaveTask(yPath, yData, true, IMAGE_JPEG_QUALITY);
				}
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"{cameraName} 原图保存异常: {ex.Message}");
			}
		}

		private void SaveResultImage_Fast(HighSpeedImageSaver saver, string cameraName, Mat result, string basePath, string dtFormat, long SequenceId, int Offset, bool resultBool)
		{
			try
			{
				string rFileName = $"{dtFormat}_R_ID{SequenceId - Offset}_SequenceId{SequenceId}_Offset{Offset}_result-{resultBool}-{(resultBool ? "OK" : "NG")}.jpg";
				string rPath = Path.Combine(basePath, rFileName);

				byte[] rData = BitmapFastConverter.ToJpegBytesViaOpenCv(result, IMAGE_JPEG_QUALITY);
				if (rData != null && rData.Length > 0)
				{
					saver.AddSaveTask(rPath, rData, true, IMAGE_JPEG_QUALITY);
				}
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"{cameraName} 结果图保存异常: {ex.Message}");
			}
		}

		private HighSpeedImageSaver GetHighSpeedSaver(string cameraName)
		{
			switch (cameraName)
			{
				case "Camera1": return _highSpeedSaver1;
				case "Camera2": return _highSpeedSaver2;
				case "Camera3": return _highSpeedSaver3;
				case "Camera4": return _highSpeedSaver4;
				case "Camera5": return _highSpeedSaver5;
				default: return _highSpeedSaver1;
			}
		}

		/// <summary>
		/// 缓存图像，等结果匹配后统一按缺陷类型存图
		/// 注意：必须创建Bitmap副本，因为原始Bitmap会在相机处理完成后被释放
		/// </summary>
		private void CacheImageForDefectSave(string cameraName, Mat original, Mat result, long sequenceId)
		{
			// 存图功能完全关闭时跳过Clone，减少GC压力
			if (!_Config.IsSaveOkImage && !_Config.IsSaveNgImage && !_Config.IsSaveOkRawImage && !_Config.IsSaveNgRawImage)
				return;

			// 【内存保护】缓存超过硬上限时驱逐最旧条目为新帧腾空间
			if (_imageCache.Count >= MAX_IMAGE_CACHE_SIZE * 2)
			{
				EvictOldestCacheEntries(1);
			}
			Mat originalCopy = null, resultCopy = null;
			try
			{
				originalCopy = original != null ? original.Clone() : null;
				resultCopy = result != null ? result.Clone() : null;

				// 线程安全：GetOrAdd + lock，防止 AddOrUpdate 的 addFactory 并发覆盖
				var origDict = _imageCache.GetOrAdd(sequenceId, _ => new Dictionary<string, Mat>());
				lock (origDict)
				{
					if (origDict.TryGetValue(cameraName, out var oldBmp))
						oldBmp?.Dispose();
					origDict[cameraName] = originalCopy;
				}

				var resDict = _resultImageCache.GetOrAdd(sequenceId, _ => new Dictionary<string, Mat>());
				lock (resDict)
				{
					if (resDict.TryGetValue(cameraName, out var oldBmp))
						oldBmp?.Dispose();
					resDict[cameraName] = resultCopy;
				}
			}
			catch (Exception ex)
			{
				originalCopy?.Dispose();
				resultCopy?.Dispose();
				FastLogger.Instance.Error($"缓存图像异常: {ex.Message}");
			}
		}

		/// <summary>
		/// 根据检测结果获取缺陷类型文件夹名称
		/// 规则：
		/// 1. OK -> OK
		/// 2. Camera1~4：NG就固定存为对应缺陷名称
		/// 3. Camera5：如果缺陷种类>=2就为混合缺陷，否则存为对应缺陷名称
		/// </summary>
		private string GetDefectTypeFolder(QueueResultItem[] results)
		{
			List<string> defects = new List<string>();

			// Camera1 - 管内异物
			if (!results[0].Result) defects.Add("管内异物");
			// Camera2 - 管盖有无
			if (!results[1].Result) defects.Add("管盖有无");
			// Camera3 - 管口圆度
			if (!results[2].Result) defects.Add("管口圆度");
			// Camera4 - 正面工号缺失
			if (!results[3].Result) defects.Add("正面工号缺失");
			// Camera5 - 细分缺陷
			if (results[4].Cam5_BaoguanResult == 0) defects.Add("爆管");
			if (results[4].Cam5_XiekouResult == 0) defects.Add("斜口");
			if (results[4].Cam5_WeijianduanResult == 0) defects.Add("未剪断");
			if (results[4].Cam5_CharResult == 0) defects.Add("背面工号缺失");
			if (results[4].Cam5_PCodeResult == 0) defects.Add("P-Code");
			if (results[4].Cam5_SebiaoResult == 0) defects.Add("色标对中");

			if (defects.Count == 0)
				return "OK";
			else if (defects.Count >= 2)
				return "混合缺陷";
			else
				return defects[0];
		}

		/// <summary>
		/// 统一按缺陷类型保存所有相机的图像
		/// 路径结构：日期 -> 班次 -> SKU -> OK/NG -> 相机名 -> 缺陷类型
		/// </summary>

		/// <summary>
		/// DB记录提交后回调：从缓存中取出图像并保存
		/// 确保记录已入库后才存图，保证记录与图片一一对应
		/// </summary>
		private void OnDbRecordCommitted(long unifiedId)
		{
			if (_pendingImageSaves.TryRemove(unifiedId, out var results))
			{
				// 卸载JPEG编码到独立线程，不阻塞DB消费者线程，也不与AI推理争抢ThreadPool
				long captureId = unifiedId;
				var captureResults = results;
				Task.Factory.StartNew(() =>
				{
					try
					{
						SaveImagesByDefectType(captureId, captureResults);
					}
					catch (Exception ex)
					{
						FastLogger.Instance.Error($"[存图] UnifiedId:{captureId} 存图异常: {ex.Message}");
					}
					finally
					{
						if (captureResults != null)
						{
							foreach (var item in captureResults)
							{
								try { QueueResultItem.Return(item); } catch { }
							}
						}
					}
				}, TaskCreationOptions.LongRunning);
			}
			else
			{
				// 可能已被ResultMatcher跳过或缓存已清理，属正常情况
			}
		}
		private void SaveImagesByDefectType(long sequenceId, QueueResultItem[] results)
		{
			try
			{
				IFSaveOKImage = _Config.IsSaveOkImage;
				IFSaveNGImage = _Config.IsSaveNgImage;
				IFSaveOKRawImage = _Config.IsSaveOkRawImage;
				IFSaveNGRawImage = _Config.IsSaveNgRawImage;

				if (!IFSaveOKImage && !IFSaveNGImage && !IFSaveOKRawImage && !IFSaveNGRawImage)
				{
					// 如果不需要存图，也要清理缓存
					ClearImageCache(sequenceId);
					return;
				}

				// 构建基础路径：日期 -> 班次 -> SKU -> OK/NG
				// 所有时间戳从单次 DateTime.Now 快照派生，保证目录/文件名一致
				var now = DateTime.Now;
				string dateFolder = now.ToString("yyMMdd");
				string hourFolder = now.ToString("HH"); // 小时级分片，防单目录文件破万
				string shiftFolder = _currentShift;
				string skuFolder = GetCurrentSkuValue();

				string dtFormat = now.ToString("yyMMddHHmmssfff");

				// 保存各相机的图像。
				// 【竞态修复】在锁内拿 Mat 引用后立即编码为 JPEG byte[]，
				// 防止内存清理线程在编码完成前 Dispose Mat 导致"无法访问已释放的对象"
				if (_imageCache.TryGetValue(sequenceId, out var originalImages) &&
					_resultImageCache.TryGetValue(sequenceId, out var resultImages))
				{
					string overallDefect = GetDefectTypeFolder(results);

					KeyValuePair<string, Mat>[] snap;
					lock (originalImages) { snap = originalImages.ToArray(); }
					foreach (var kvp in snap)
					{
						string cameraName = kvp.Key;
						Mat original = kvp.Value;
						Mat result = null;
						lock (resultImages) { resultImages.TryGetValue(cameraName, out result); }

						bool isCameraNg = IsCameraNg(cameraName, results);
						string defectFolder = GetDefectTypeForCamera(cameraName, results);
						bool isOk = defectFolder == "OK";

						if (overallDefect == "混合缺陷" && !isOk)
							defectFolder = "混合缺陷";

						if (!IFSaveOKImage && !IFSaveOKRawImage && isOk)
							continue;

						var saver = GetHighSpeedSaver(cameraName);
						if (saver == null) continue;

						string resultFolder = isOk ? "OK" : "NG";
						string basePath = Path.Combine(_Config.ImagePath, dateFolder, shiftFolder, skuFolder, resultFolder, cameraName, hourFolder);
						if (!isOk)
							basePath = Path.Combine(basePath, defectFolder);

						// 【竞态修复核心】锁内立即编码为 byte[]，之后 Mat 被 Dispose 也不影响
						byte[] origJpg = null, rstJpg = null;
						if (original != null && ((isOk && IFSaveOKRawImage) || (!isOk && IFSaveNGRawImage)))
							origJpg = BitmapFastConverter.ToJpegBytesViaOpenCv(original, IMAGE_JPEG_QUALITY);
						if (result != null && ((isOk && IFSaveOKImage) || (!isOk && IFSaveNGImage)))
							rstJpg = BitmapFastConverter.ToJpegBytesViaOpenCv(result, IMAGE_JPEG_QUALITY);

						// Mat 已编码完成，后续只用 byte[] 入队，不再访问 Mat
						if (origJpg != null && origJpg.Length > 0)
						{
							string yFileName = $"{dtFormat}_Y_ID{sequenceId - (results[0]?.Offset ?? 0)}_SequenceId{sequenceId}_Offset0_result-{isOk}-{(isOk ? "OK" : "NG")}.jpg";
							saver.AddSaveTask(Path.Combine(basePath, yFileName), origJpg, true, IMAGE_JPEG_QUALITY);
						}
						if (rstJpg != null && rstJpg.Length > 0)
						{
							string rFileName = $"{dtFormat}_R_ID{sequenceId - (results[0]?.Offset ?? 0)}_SequenceId{sequenceId}_Offset0_result-{isOk}-{(isOk ? "OK" : "NG")}.jpg";
							saver.AddSaveTask(Path.Combine(basePath, rFileName), rstJpg, true, IMAGE_JPEG_QUALITY);
						}
					}

					// 编码完成后安全清理缓存
					ClearImageCache(sequenceId);
				}
				else
				{
					FastLogger.Instance.Debug($"缓存中未找到图像: SequenceId={sequenceId}");
				}
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"按缺陷类型存图异常: {ex.Message}");
				// 发生异常时也要清理缓存，避免内存泄漏
				ClearImageCache(sequenceId);
			}
		}

		/// <summary>
		/// 测试模式存图:路径总在 Test/日期/手动导入或模拟运行/HHmmss/CameraN/OK-or-NG/ 下,不依赖班次/SKU
		/// </summary>
		private void SaveTestImages(long unifiedId, QueueResultItem[] results, string testType)
		{
			try
			{
				var now = DateTime.Now;
				string dateFolder = now.ToString("yyMMdd");
				string timeFolder = now.ToString("HHmmss");
				string basePath = Path.Combine(_Config.ImagePath, "Test", dateFolder, testType, timeFolder);
				string dtFormat = now.ToString("yyMMddHHmmssfff");

				if (_imageCache.TryGetValue(unifiedId, out var originalImages) &&
					_resultImageCache.TryGetValue(unifiedId, out var resultImages))
				{
					KeyValuePair<string, Mat>[] snap;
					lock (originalImages) { snap = originalImages.ToArray(); }
					foreach (var kvp in snap)
					{
						string cameraName = kvp.Key;
						Mat original = kvp.Value;
						Mat result = null;
						lock (resultImages) { resultImages.TryGetValue(cameraName, out result); }

						bool isOk = !IsCameraNg(cameraName, results);
						string resultDir = Path.Combine(basePath, cameraName, isOk ? "OK" : "NG");
						var saver = GetHighSpeedSaver(cameraName);
						if (saver == null) continue;

						if (original != null)
							SaveOriginalImage_Fast(saver, cameraName, original, resultDir, dtFormat, unifiedId, 0, isOk);
						if (result != null)
							SaveResultImage_Fast(saver, cameraName, result, resultDir, dtFormat, unifiedId, 0, isOk);
					}
				}
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"[手动测试] 测试存图异常: {ex.Message}");
			}
		}

		/// <summary>
		/// 判断指定相机是否为NG
		/// </summary>
		private bool IsCameraNg(string cameraName, QueueResultItem[] results)
		{
			if (results == null || results.Length == 0)
				return false;

			switch (cameraName)
			{
				case "Camera1":
					return results[0]?.Result == false;
				case "Camera2":
					return results[1]?.Result == false;
				case "Camera3":
					return results[2]?.Result == false;
				case "Camera4":
					return results[3]?.Result == false;
				case "Camera5":
					return results[4]?.Result == false;
				default:
					return false;
			}
		}

		/// <summary>
		/// 获取指定相机的缺陷类型
		/// </summary>
		private string GetDefectTypeForCamera(string cameraName, QueueResultItem[] results)
		{
			if (results == null || results.Length == 0)
				return "OK";

			switch (cameraName)
			{
				case "Camera1":
					return results[0]?.Result == false ? "管内异物" : "OK";
				case "Camera2":
					return results[1]?.Result == false ? "管盖有无" : "OK";
				case "Camera3":
					return results[2]?.Result == false ? "管口圆度" : "OK";
				case "Camera4":
					return results[3]?.Result == false ? "正面工号缺失" : "OK";
				case "Camera5":
					if (results[4]?.Result != false) return "OK";
					// 统计Camera5的缺陷种类数
					int defectCount = 0;
					if (results[4].Cam5_BaoguanResult == 0) defectCount++;
					if (results[4].Cam5_XiekouResult == 0) defectCount++;
					if (results[4].Cam5_WeijianduanResult == 0) defectCount++;
					if (results[4].Cam5_CharResult == 0) defectCount++;
					if (results[4].Cam5_PCodeResult == 0) defectCount++;
					if (results[4].Cam5_SebiaoResult == 0) defectCount++;
					// 2种及以上缺陷 → 混合缺陷
					if (defectCount >= 2) return "混合缺陷";
					// 单一缺陷按优先级返回
					if (defectCount == 0)
					{
						FastLogger.Instance.Debug($"[存图] Camera5 NG但defectCount=0! Seq={results[4].SequenceId}, Baoguan={results[4].Cam5_BaoguanResult}, Xiekou={results[4].Cam5_XiekouResult}, Weijianduan={results[4].Cam5_WeijianduanResult}, Char={results[4].Cam5_CharResult}, PCode={results[4].Cam5_PCodeResult}, Sebiao={results[4].Cam5_SebiaoResult}, ConfigBaoGuan={_Config.Camera5IFBaoGuan}, Result={results[4].Result}");
						return "无分类";
					}
					return results[4].Cam5_BaoguanResult == 0 ? "爆管" :
						results[4].Cam5_XiekouResult == 0 ? "斜口" :
						results[4].Cam5_WeijianduanResult == 0 ? "未剪断" :
						results[4].Cam5_CharResult == 0 ? "背面工号缺失" :
						results[4].Cam5_PCodeResult == 0 ? "P-Code" :
						results[4].Cam5_SebiaoResult == 0 ? "色标对中" : "无分类";
				default:
					return "OK";
			}
		}

		/// <summary>
		/// 清理指定SequenceId的图像缓存（释放Bitmap并移除）
		/// </summary>
		private void ClearImageCache(long sequenceId)
		{
			try
			{
				if (_imageCache.TryRemove(sequenceId, out var originalImages))
				{
					lock (originalImages)
					{
						foreach (var kvp in originalImages) kvp.Value?.Dispose();
						originalImages.Clear();
					}
				}

				if (_resultImageCache.TryRemove(sequenceId, out var resultImages))
				{
					lock (resultImages)
					{
						foreach (var kvp in resultImages) kvp.Value?.Dispose();
						resultImages.Clear();
					}
				}
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"清理图像缓存异常: {ex.Message}");
			}
		}
		#endregion

		#region GDI+中文绘制
		/// <summary>
		/// 用 Bitmap 零拷贝包裹 BGR Mat 的像素内存执行 GDI+ 绘制，文字直接落入 Mat 数据，
		/// 使后续的存图克隆与界面显示共用同一份已绘制图像（包裹写法先例：AIsdk Vimo.Visualize）。
		/// </summary>
		private static void DrawOnMat(Mat mat, Action<Bitmap> drawAction)
		{
			if (mat == null || mat.Empty() || mat.Type() != MatType.CV_8UC3) return;
			try
			{
				if (mat.Step() % 4 == 0)   // GDI+ 要求 stride 4字节对齐，工业相机分辨率基本都满足
				{
					using (var bmp = new Bitmap(mat.Cols, mat.Rows, (int)mat.Step(), PixelFormat.Format24bppRgb, mat.Data))
						drawAction(bmp);
				}
				else                       // 兜底：非对齐分辨率走一次拷贝绘制再写回，保证功能正确
				{
					using (var bmp = mat.ToBitmap())
					{
						drawAction(bmp);
						using (var tmp = BitmapConverter.ToMat(bmp))
							tmp.CopyTo(mat);
					}
				}
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"DrawOnMat 绘制异常: {ex.Message}");
			}
		}

		private void DrawGdiCam1(Bitmap bmp, bool result, bool result_Segmentation, double totalArea, int totalArea_Camera1, long id)
		{
			try
			{
				using (var g = Graphics.FromImage(bmp))
				{
					int x = bmp.Width - 500; int y = 30;
					g.DrawString("相机：底部异物检测", _processor1.DrawFontTitle, _processor1.DrawBrushGreen, x, y); y += 70;
					g.DrawString("检测时间：" + DateTime.Now.ToString("HH:mm:ss.fff"), _processor1.DrawFontText, _processor1.DrawBrushGreen, x, y); y += 70;
					g.DrawString("综合结果：" + (result ? "OK" : "NG"), _processor1.DrawFontText, result ? _processor1.DrawBrushGreen : _processor1.DrawBrushRed, x, y); y += 70;
					if (!result_Segmentation) { g.DrawString("异物面积：" + totalArea.ToString("F2") + " / " + totalArea_Camera1, _processor1.DrawFontText, _processor1.DrawBrushRed, x, y); y += 70; }
					g.DrawString("SequenceId：" + id, _processor1.DrawFontText, _processor1.DrawBrushGreen, x, y);
				}
			}
			catch { }
		}
		private void DrawGdiCam2(Bitmap bmp, bool result, string result_class, long id)
		{
			try
			{
				using (var g = Graphics.FromImage(bmp))
				{
					int x = bmp.Width - 500; int y = 30;
					g.DrawString("相机：瓶盖有无检测", _processor2.DrawFontTitle, _processor2.DrawBrushGreen, x, y); y += 70;
					g.DrawString("检测时间：" + DateTime.Now.ToString("HH:mm:ss.fff"), _processor2.DrawFontText, _processor2.DrawBrushGreen, x, y); y += 70;
					g.DrawString("综合结果：" + (result ? "OK" : "NG"), _processor2.DrawFontText, result ? _processor2.DrawBrushGreen : _processor2.DrawBrushRed, x, y); y += 70;
					if (!string.IsNullOrEmpty(result_class)) { g.DrawString("缺陷标签：" + result_class, _processor2.DrawFontText, _processor2.DrawBrushRed, x, y); y += 70; }
					g.DrawString("SequenceId：" + id, _processor2.DrawFontText, _processor2.DrawBrushGreen, x, y);
				}
			}
			catch { }
		}
		private void DrawGdiCam3(Bitmap bmp, bool result, double longEdge, double PipeDiameter, double roundness, long id)
		{
			try
			{
				using (var g = Graphics.FromImage(bmp))
				{
					int x = bmp.Width - 500; int y = 30;
					g.DrawString("相机：管口圆度检测", _processor3.DrawFontTitle, _processor3.DrawBrushGreen, x, y); y += 70;
					g.DrawString("检测时间：" + DateTime.Now.ToString("HH:mm:ss.fff"), _processor3.DrawFontText, _processor3.DrawBrushGreen, x, y); y += 70;
					g.DrawString("综合结果：" + (result ? "OK" : "NG"), _processor3.DrawFontText, result ? _processor3.DrawBrushGreen : _processor3.DrawBrushRed, x, y); y += 70;
					g.DrawString("长边：" + longEdge.ToString("F2"), _processor3.DrawFontText, _processor3.DrawBrushGreen, x, y); y += 70;
					g.DrawString("管径：" + PipeDiameter.ToString(), _processor3.DrawFontText, result ? _processor3.DrawBrushGreen : _processor3.DrawBrushRed, x, y); y += 70;
					if (!result) { g.DrawString("圆度上限：" + _Config.Camera3RoundnessUp, _processor3.DrawFontText, _processor3.DrawBrushGreen, x, y); y += 70; }
					g.DrawString("圆度：" + roundness.ToString("F3"), _processor3.DrawFontText, result ? _processor3.DrawBrushGreen : _processor3.DrawBrushRed, x, y); y += 70;
					if (!result) { g.DrawString("圆度下限：" + _Config.Camera3RoundnessDown, _processor3.DrawFontText, _processor3.DrawBrushGreen, x, y); y += 70; }
					g.DrawString("SequenceId：" + id, _processor3.DrawFontText, _processor3.DrawBrushGreen, x, y);
				}
			}
			catch { }
		}
		private void DrawGdiCam4(Bitmap bmp, bool result, bool result_char, string order_ocr, long id)
		{
			try
			{
				using (var g = Graphics.FromImage(bmp))
				{
					int x = bmp.Width - 500; int y = 30;
					g.DrawString("相机：夹尾正面字符检测", _processor4.DrawFontTitle, _processor4.DrawBrushGreen, x, y); y += 70;
					g.DrawString("检测时间：" + DateTime.Now.ToString("HH:mm:ss.fff"), _processor4.DrawFontText, _processor4.DrawBrushGreen, x, y); y += 70;
					g.DrawString("综合结果：" + (result ? "OK" : "NG"), _processor4.DrawFontText, result ? _processor4.DrawBrushGreen : _processor4.DrawBrushRed, x, y); y += 70;
					g.DrawString("字符结果：" + (result_char ? "OK" : "NG"), _processor4.DrawFontText, result_char ? _processor4.DrawBrushGreen : _processor4.DrawBrushRed, x, y); y += 70;
					if (!string.IsNullOrEmpty(order_ocr)) { g.DrawString(order_ocr, _processor4.DrawFontText, result_char ? _processor4.DrawBrushGreen : _processor4.DrawBrushRed, x, y); y += 70; }
					g.DrawString("SequenceId：" + id, _processor4.DrawFontText, _processor4.DrawBrushGreen, x, y);
				}
			}
			catch { }
		}
		private void DrawGdiCam5(Bitmap bmp, bool result, bool result_char, bool result_PCode_char, bool result_flaw, bool result_Segmentation, string result_class, string order_ocr, string pcode_ocr, double projectionLength, SegmentationResult segRst, long id)
		{
			try
			{
				using (var g = Graphics.FromImage(bmp))
				{
					int x = bmp.Width - 600; int y = 30;
					g.DrawString("相机：夹尾反面字符检测", _processor5.DrawFontTitle, _processor5.DrawBrushGreen, x, y); y += 70;
					g.DrawString("检测时间：" + DateTime.Now.ToString("HH:mm:ss.fff"), _processor5.DrawFontText, _processor5.DrawBrushGreen, x, y); y += 70;
					g.DrawString("综合结果：" + (result ? "OK" : "NG"), _processor5.DrawFontText, result ? _processor5.DrawBrushGreen : _processor5.DrawBrushRed, x, y); y += 70;
					g.DrawString("字符结果：" + (result_char ? "OK" : "NG"), _processor5.DrawFontText, result_char ? _processor5.DrawBrushGreen : _processor5.DrawBrushRed, x, y); y += 70;
					if (!string.IsNullOrEmpty(order_ocr)) { g.DrawString(order_ocr, _processor5.DrawFontText, result_char ? _processor5.DrawBrushGreen : _processor5.DrawBrushRed, x, y); y += 70; }
					if (_Config.Camera5IFPCode)
					{
						g.DrawString("P-Code结果：" + (result_PCode_char ? "OK" : "NG"), _processor5.DrawFontText, result_PCode_char ? _processor5.DrawBrushGreen : _processor5.DrawBrushRed, x, y); y += 70;
						if (!string.IsNullOrEmpty(pcode_ocr)) { g.DrawString(pcode_ocr, _processor5.DrawFontText, result_PCode_char ? _processor5.DrawBrushGreen : _processor5.DrawBrushRed, x, y); y += 70; }
						if (!result_PCode_char) { g.DrawString("P-Code标准: " + _Config.Standard_PCode, _processor5.DrawFontText, _processor5.DrawBrushGreen, x, y); y += 70; }
					}
					g.DrawString("色标结果：" + (result_Segmentation ? "OK" : "NG"), _processor5.DrawFontText, result_Segmentation ? _processor5.DrawBrushGreen : _processor5.DrawBrushRed, x, y); y += 70;
					if (segRst.DetectedBoth) { g.DrawString("色标测量值：" + projectionLength.ToString("F2") + "mm", _processor5.DrawFontText, result_Segmentation ? _processor5.DrawBrushGreen : _processor5.DrawBrushRed, x, y); y += 70; }
					g.DrawString("缺陷结果：" + (result_flaw ? "OK" : "NG"), _processor5.DrawFontText, result_flaw ? _processor5.DrawBrushGreen : _processor5.DrawBrushRed, x, y); y += 70;
					if (!string.IsNullOrEmpty(result_class)) { g.DrawString("缺陷标签：" + result_class, _processor5.DrawFontText, result_flaw ? _processor5.DrawBrushGreen : _processor5.DrawBrushRed, x, y); y += 70; }
					g.DrawString("SequenceId：" + id, _processor5.DrawFontText, _processor5.DrawBrushGreen, x, y);
				}
			}
			catch { }
		}
		#endregion

		#region 性能优化辅助方法
		public static List<List<OpenCvSharp.Point>> SortByCenterX(List<List<OpenCvSharp.Point>> polygons, bool leftToRight = true)
		{
			return leftToRight
				? polygons.OrderBy(p => p.Average(point => point.X)).ToList()
				: polygons.OrderByDescending(p => p.Average(point => point.X)).ToList();
		}

		private struct SegmentationResult
		{
			public float SeBiaoX; public float SeBiaoY; public float SeBiaoA;
			public float PingShengX; public float PingShengY; public float PingShengA;
			public bool DetectedBoth;
		}

		public static void DrawLine(Mat image, OpenCvSharp.Point pt1, OpenCvSharp.Point pt2, Scalar color, int thickness = 8, LineTypes lineType = LineTypes.Link8, int shift = 0)
		{
			Cv2.Line(image, pt1, pt2, color, thickness, lineType, shift);
		}

		private SegmentationResult ProcessSegmentationResultsFast(
			ResponseList<SegmentationResponse> rsp_segmentation,
			ResponseList<SegmentationResponse> rsp_color,
			ResponseList<SegmentationResponse> rsp_rests,
			Mat resultImage,
			ref string result_class,
			ref string result_Class_str,
			ref string label_str)
		{
			var result = new SegmentationResult();
			int result_Segmentation_str = 0;
			var classBuilder = new System.Text.StringBuilder(result_class);

			ProcessSegmentationBatch(rsp_segmentation, resultImage, ref result, ref result_Segmentation_str, ref classBuilder, ref result_Class_str, ref label_str);
			ProcessSegmentationBatch(rsp_color, resultImage, ref result, ref result_Segmentation_str, ref classBuilder, ref result_Class_str, ref label_str);
			ProcessSegmentationBatch(rsp_rests, resultImage, ref result, ref result_Segmentation_str, ref classBuilder, ref result_Class_str, ref label_str);

			result_class = classBuilder.ToString();

			if (_Config.Camera5IFSeBiao)
				result.DetectedBoth = result_Segmentation_str == 1001;
			else
				result.DetectedBoth = true;

			return result;
		}

		private void ProcessSegmentationBatch(
			ResponseList<SegmentationResponse> rspList,
			Mat resultImage,
			ref SegmentationResult result,
			ref int result_Segmentation_str,
			ref System.Text.StringBuilder classBuilder,
			ref string result_Class_str,
			ref string label_str)
		{
			if (rspList == null) return;

			using (Mat compareMask = new Mat())
			using (Mat labels = new Mat())
			using (Mat stats = new Mat())
			using (Mat centroids = new Mat())
			using (Mat singleContourMask = new Mat())
			{
				for (int i = 0; i < rspList.Count; i++)
				{
					var rspPair = rspList[i];
					var rsp = rspPair.Item2;
					if (rsp.LabelMap == null) continue;

					var labelKeys = rsp.LabelMap.Keys.ToArray();
					for (int j = 0; j < labelKeys.Length; j++)
					{
						var kv = new KeyValuePair<string, int>(labelKeys[j], rsp.LabelMap[labelKeys[j]]);

						Cv2.Compare(rsp.Mask, Scalar.All(kv.Value), compareMask, CmpType.EQ);
						var count_flaw = Cv2.ConnectedComponentsWithStats(compareMask, labels, stats, centroids);

						for (int k = 1; k < count_flaw; k++)
						{
							Cv2.Compare(labels, Scalar.All(k), singleContourMask, CmpType.EQ);
							Cv2.FindContours(singleContourMask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
							if (contours.Length == 0) continue;

							RotatedRect minAreaRect = Cv2.MinAreaRect(contours[0]);
							label_str += $"{kv.Key};";

							switch (kv.Key)
							{
								case "色标":
									result.SeBiaoX = minAreaRect.Center.X;
									result.SeBiaoY = minAreaRect.Center.Y;
									result.SeBiaoA = minAreaRect.Angle;
									result_Segmentation_str += 1;
									DrawPointFast(resultImage, minAreaRect.Center, new Scalar(255, 69, 0));
									DrawRotatedRectangleFast(resultImage, minAreaRect, new Scalar(255, 69, 0));
									break;
								case "夹尾":
									result.PingShengX = minAreaRect.Center.X;
									result.PingShengY = minAreaRect.Center.Y;
									result.PingShengA = minAreaRect.Angle;
									result_Segmentation_str += 1000;
									DrawPointFast(resultImage, minAreaRect.Center, new Scalar(160, 32, 240));
									DrawRotatedRectangleFast(resultImage, minAreaRect, new Scalar(160, 32, 240));
									break;
								case "爆管":
									if (_Config.Camera5IFBaoGuan)
									{
										Cv2.DrawContours(resultImage, contours, -1, new Scalar(0, 0, 255), 2);
										if (classBuilder.Length > 0) classBuilder.Append("; ");
										classBuilder.Append("爆管");
										result_Class_str += "1";
									}
									else
									{
										Cv2.DrawContours(resultImage, contours, -1, new Scalar(0, 165, 255), 2);
										result_Class_str += "0";
									}
									break;
								case "未剪断":
									if (_Config.Camera5IFWeiJianDuan)
									{
										Cv2.DrawContours(resultImage, contours, -1, new Scalar(0, 0, 255), 2);
										if (classBuilder.Length > 0) classBuilder.Append("; ");
										classBuilder.Append("未剪断");
										result_Class_str += "1";
									}
									else
									{
										Cv2.DrawContours(resultImage, contours, -1, new Scalar(0, 165, 255), 2);
										result_Class_str += "0";
									}
									break;
								case "斜口":
									if (_Config.Camera5IFXieKou)
									{
										Cv2.DrawContours(resultImage, contours, -1, new Scalar(0, 0, 255), 2);
										if (classBuilder.Length > 0) classBuilder.Append("; ");
										classBuilder.Append("斜口");
										result_Class_str += "1";
									}
									else
									{
										Cv2.DrawContours(resultImage, contours, -1, new Scalar(0, 165, 255), 2);
										result_Class_str += "0";
									}
									break;
								case "空杯":
									result_Class_str = "0";
									result_Segmentation_str = 1001;
									// 空杯产品不再回退计数器（计数由ResultCountMethod统一管理）
									break;
							}
						}
					}
				}
			}
		}

		private void DrawPointFast(Mat image, Point2f point, Scalar color, int radius = 15)
		{
			try { Cv2.Circle(image, new OpenCvSharp.Point((int)point.X, (int)point.Y), radius, color, -1, LineTypes.AntiAlias); }
			catch { }
		}

		private void DrawRotatedRectangleFast(Mat image, RotatedRect rotatedRect, Scalar color, int thickness = 2)
		{
			try
			{
				Point2f[] vertices = rotatedRect.Points();
				var points = new OpenCvSharp.Point[4];
				for (int i = 0; i < 4; i++) points[i] = new OpenCvSharp.Point((int)vertices[i].X, (int)vertices[i].Y);
				Cv2.Polylines(image, new OpenCvSharp.Point[][] { points }, true, color, thickness, LineTypes.AntiAlias);
			}
			catch { }
		}

		public static double CalculateProjection(double ax, double ay, double cx, double cy, double angleDegrees)
		{
			return Math.Abs(ax - cx);
		}
		#endregion

		#region 结果匹配回调
		private void OnResultsMatched(QueueResultItem[] results)
		{
			if (_isClosing || results == null || results.Length < 5) return;

			try
			{
				// 【P0修复】入口立即快照所有值，防止后续空杯/重复key分支 Return() 回对象池
				// 导致 Reset() 清零后被 ResultCountMethod / SendResultList / BeginInvoke 读到脏数据
				bool r0 = results[0].Result, r1 = results[1].Result, r2 = results[2].Result;
				bool r3 = results[3].Result, r4 = results[4].Result;
				bool finalResult = r0 && r1 && r2 && r3 && r4;
				long unifiedId = results[0].SequenceId - results[0].Offset;
				long seq0 = results[0].SequenceId, seq1 = results[1].SequenceId, seq2 = results[2].SequenceId;
				long seq3 = results[3].SequenceId, seq4 = results[4].SequenceId;
				int off0 = results[0].Offset, off1 = results[1].Offset, off2 = results[2].Offset;
				int off3 = results[3].Offset, off4 = results[4].Offset;
				bool isEmptyCup = results[4].IsEmptyCup;

				// 【调试日志】记录每个相机的匹配结果（含虚拟 OK 标记）
				if (IFSaveLog)
				{
					string[] camNames = { "Cam1", "Cam2", "Cam3", "Cam4", "Cam5" };
					for (int ci = 0; ci < results.Length; ci++)
					{
						var r = results[ci];
						bool isVirtual = r.SequenceId == unifiedId && r.Offset == 0 && r.Result == true &&
										 r.Cam5_BaoguanResult == 1 && r.Cam5_WeijianduanResult == 1 &&
										 r.Cam5_XiekouResult == 1 && r.Cam5_CharResult == 1 &&
										 r.Cam5_PCodeResult == 1 && r.Cam5_SebiaoResult == 1;
						string marker = isVirtual ? "(虚拟OK)" : "";
						if (FastLogger.DebugEnabled) FastLogger.Instance.Debug("[ResultMatch] " + camNames[ci] + ": Seq=" + r.SequenceId + " Offset=" + r.Offset + " Result=" + r.Result + " " + marker);
					}
				}

				// 调试日志：周期输出结果匹配汇总（每N帧一次，避免热路径IO）
				if (RunLogEnabled)
				{
					long debugCount = Interlocked.Increment(ref _debugMatchCount);
					if (debugCount <= 1 || debugCount % DEBUG_LOG_INTERVAL == 0)
					{
						try { if (FastLogger.IsInitialized) FastLogger.Instance.Info($"[ResultMatch-Debug] 匹配#{debugCount} ID:{unifiedId} Cam1:{r0} Cam2:{r1} Cam3:{r2} Cam4:{r3} Cam5:{r4} Final:{finalResult} Total:{_Config.total} OK:{_Config.ok} SendQueue:{SendResultList.Count}"); } catch { }
					}
				}
				if (IFSaveLog && unifiedId % 100 == 0) { try { if (FastLogger.IsInitialized) FastLogger.Instance.Debug("里程碑: ID=" + unifiedId + " FinalResult=" + (finalResult ? "OK" : "NG") + " Total=" + _Config.total + " OK=" + _Config.ok); } catch { } }

				// 【测试模式】手动导入/模拟运行的结果只用于查看模型识别情况：
				// 不写数据库、不计产量、不进PLC发送队列；渲染图和原图存入独立的 Test 子目录
				if (IsTestResult())
				{
					// 先判定测试类型再递减计数：手动导入以待处理帧数为准，其余(运行中/尾窗)归为模拟运行
					bool isManualImport = !_simRunning && Interlocked.Read(ref _testPendingFrames) > 0;
					if (isManualImport)
						Interlocked.Decrement(ref _testPendingFrames);

					FastLogger.Instance.Info($"[手动测试] ID:{unifiedId} 模型识别: Cam1:{(r0 ? "OK" : "NG")} Cam2:{(r1 ? "OK" : "NG")} Cam3:{(r2 ? "OK" : "NG")} Cam4:{(r3 ? "OK" : "NG")} Cam5:{(r4 ? "OK" : "NG")} 综合:{(finalResult ? "OK" : "NG")} (测试模式: 不计产量/不写库/不发PLC)");

					SaveTestImages(unifiedId, results, isManualImport ? "手动导入" : "模拟运行");
					ClearImageCache(unifiedId);
					foreach (var item in results)
						try { QueueResultItem.Return(item); } catch { }
				}
				else
				{
					// 空杯产品：不计入数据库记录，不保存图像
					bool isRealProduct = !isEmptyCup;
					if (isRealProduct)
					{
						AddProductionRecordBuffered(results, finalResult);
						// 图像保存改为DB记录提交后触发（OnDbRecordCommitted回调），确保记录与图片一一对应
						// 【内存保护】待存队列深度守卫：超过上限驱逐最旧条目防止泄漏
						if (_pendingImageSaves.Count >= MAX_IMAGE_CACHE_SIZE * 2)
						{
							// ToArray 快照防御 TOCTOU: Count>=100 和 Min() 之间可能被其他线程清空
							var keys = _pendingImageSaves.Keys.ToArray();
							if (keys.Length == 0) goto skipEvict;
							var oldestKey = keys.Min();
							if (_pendingImageSaves.TryRemove(oldestKey, out var oldResults))
							{
								try { FastLogger.Instance.Warn($"[存图] 待存队列满，丢弃旧条目 UnifiedId={oldestKey}"); } catch { }
								ClearImageCache(oldestKey);
								foreach (var item in oldResults)
									try { QueueResultItem.Return(item); } catch { }
							}
						skipEvict:;
						}
						if (!_pendingImageSaves.TryAdd(unifiedId, results))
						{
							// 重复key：旧条目已泄露，归还当前results到对象池
							// 注意：快照已在入口完成，此处安全归还
							FastLogger.Instance.Warn($"[存图] UnifiedId={unifiedId} 重复，清理缓存+归还results");
							ClearImageCache(unifiedId);
							foreach (var item in results)
								try { QueueResultItem.Return(item); } catch { }
						}
					}
					else
					{
						// 空杯产品：立即归还QueueResultItem到对象池，防止泄露
						// 注意：快照已在入口完成，此处安全归还
						foreach (var item in results)
							try { QueueResultItem.Return(item); } catch { }
					}

					// 计数器更新必须与burstExcludeCount同步，不能放在BeginInvoke中延迟
					// 使用入口快照值，不访问 results[]（可能已被归还到对象池）
					ResultCountMethod(r0, r1, r2, r3, r4, isEmptyCup);

					// 空杯仍加入PLC发送队列（保持流水线序列号连续性）
					if (!_isClosing) // P0: 无论PLC是否连接都入队，重连后补发
					{
						lock (SendResultList)
						{
							if (SendResultList.Count >= 200)
							{
								try { FastLogger.Instance.Error($"[严重] SendResultList异常:{SendResultList.Count}条 >200, 当前unifiedId={unifiedId}"); } catch { }
							}
							SendResultList.Add(new QueueResultItem
							{
								SequenceId = unifiedId,
								Offset = 0,
								Result = finalResult,
								Timestamp = DateTime.Now
							});
						}
						if (RunLogEnabled) try { if (FastLogger.IsInitialized) FastLogger.Instance.Info($"time[{DateTime.Now:HH:mm:ss:fff}]结果匹配成功 ID: {unifiedId} Result: {finalResult}"); } catch { }
					}
				}

				if (!_isClosing)
				{
					// UI 闭包只捕获值类型快照，不引用 results[]（已被归还或正在异步存图）
					long uiSeq1 = seq1, uiSeq2 = seq2, uiSeq3 = seq3, uiSeq4 = seq4;
					int uiOff1 = off1, uiOff2 = off2, uiOff3 = off3, uiOff4 = off4;
					this.BeginInvoke(new Action(() =>
					{
						if (_isClosing) return;
						try
						{
							ResultShowMethod(finalResult);
							ImageIDTxt1.Text = unifiedId.ToString();
							ImageIDTxt2.Text = uiSeq1.ToString();
							ImageIDTxt3.Text = uiSeq2.ToString();
							ImageIDTxt4.Text = uiSeq3.ToString();
							ImageIDTxt5.Text = uiSeq4.ToString();
							OffsetTxt1.Text = uiOff1.ToString();
							OffsetTxt2.Text = uiOff2.ToString();
							OffsetTxt3.Text = uiOff3.ToString();
							OffsetTxt4.Text = uiOff4.ToString();
						}
						catch (Exception uiEx) { FastLogger.Instance.Error($"UI更新异常: {uiEx.Message}"); }
					}));
				}
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"结果匹配回调异常: {ex.Message}");
			}
		}

		private void AddProductionRecordBuffered(QueueResultItem[] results, bool finalResult)
		{
			if (_dbRecorder == null) return;
			try
			{
				var record = new ProductionRecord
				{
					DetectionTime = DateTime.Now,
					SequenceId = results[0].SequenceId,
					UnifiedId = results[0].SequenceId - results[0].Offset,
					FinalResult = finalResult ? "OK" : "NG",
					Sku = GetCurrentSkuValue(),
					Cam1Result = results[0].Result ? 1 : 0,
					Cam2Result = results[1].Result ? 1 : 0,
					Cam3Result = results[2].Result ? 1 : 0,
					Cam4Result = results[3].Result ? 1 : 0,
					Cam5Result = results[4].Result ? 1 : 0,
					Cam5_CharResult = results[4].Cam5_CharResult,
					Cam5_PCodeResult = results[4].Cam5_PCodeResult,
					Cam5_SebiaoResult = results[4].Cam5_SebiaoResult,
					Cam5_BaoguanResult = results[4].Cam5_BaoguanResult,
					Cam5_XiekouResult = results[4].Cam5_XiekouResult,
					Cam5_WeijianduanResult = results[4].Cam5_WeijianduanResult
				};

				bool isBurstNg = results[4].Cam5_BaoguanResult == 0;
				_dbRecorder.UpdateConsecutiveBurst(results[0].SequenceId, isBurstNg);

				List<ProductionRecord> flushList = null;
				lock (_pendingLock)
				{
					_pendingRecords.Add(record);
					_lastRecordTime = DateTime.Now;
					if (_pendingRecords.Count >= 10)
					{
						flushList = _pendingRecords;
						_pendingRecords = new List<ProductionRecord>();
					}
				}

				if (flushList != null)
				{
					foreach (var r in flushList)
					{
						_dbRecorder.AddRecord(r);

					}

				}
			}
			catch (Exception ex) { FastLogger.Instance.Error($"批量生产记录异常: {ex.Message}"); }
		}

		private void FlushPendingRecords()
		{
			List<ProductionRecord> flushList = null;
			lock (_pendingLock)
			{
				if (_pendingRecords.Count > 0)
				{
					flushList = _pendingRecords;
					_pendingRecords = new List<ProductionRecord>();
				}
			}
			if (flushList != null && _dbRecorder != null)
			{
				foreach (var r in flushList)
				{
					_dbRecorder.AddRecord(r);
					// 爆管计数已由ResultCountMethod同步处理，此处不再累加
				}
				if (_Config.burstExcludeCount > 0 || flushList.Any(r => r.IsExcluded))
				{
					// 关闭时不 Invoke，避免 Handle 已释放导致卡死
					if (!_isClosing && this.IsHandleCreated && !this.IsDisposed)
					{
						try
						{
							this.Invoke(new Action(() => { if (BaoGuanNGTxt != null) BaoGuanNGTxt.Text = _Config.burstExcludeCount.ToString(); }));
						}
						catch { }
					}
				}
			}
		}

		private void ResultShowMethod(bool result)
		{
			try
			{
				if (ResultPanel.InvokeRequired)
					ResultPanel.Invoke(new Action(() => UpdateResultPanel(result)));
				else
					UpdateResultPanel(result);
			}
			catch (Exception ex) { FastLogger.Instance.Error($"显示结果异常: {ex.Message}"); }
		}

		private void UpdateResultPanel(bool result)
		{
			if (_isClosing) return;
			try
			{
				ResultPanel.Text = result ? "OK" : "NG";
				ResultPanel.ForeColor = result ? Color.Green : Color.Red;
				ResultPanel.BackColor = Color.FromArgb(50, result ? Color.Green : Color.Red);
			}
			catch { }
		}

		private void ResultCountMethod(bool result1, bool result2, bool result3, bool result4, bool result5, bool isEmptyCup = false)
		{
			try
			{
				// 空杯产品不计入检测总数
				if (isEmptyCup) return;
				_Config.total++;
				_localTotal++;
				if (_localTotal <= 5 || _localTotal % 100 == 0) try { FastLogger.Instance.Info($"[计数] total={_Config.total} OK={_Config.ok}"); } catch { }
				if (!result1) _Config.ng_cam1++;
				if (!result2) _Config.ng_cam2++;
				if (!result3) _Config.ng_cam3++;
				if (!result4) _Config.ng_cam4++;
				if (!result5) _Config.ng_cam5++;
				if (result1 && result2 && result3 && result4 && result5) _Config.ok++;

				// 同步爆管计数：每3支连续Camera5 NG → burstExcludeCount+3
				if (!result5)
				{
					_consecutiveC5NG++;
					if (_consecutiveC5NG >= 3)
					{
						_Config.burstExcludeCount += 3;
						_consecutiveC5NG = 0;
					}
				}
				else
				{
					_consecutiveC5NG = 0;
				}

				this.BeginInvoke(new Action(() =>
				{
					if (_isClosing) return;
					try
					{
						if (totalTxt != null) totalTxt.Text = _Config.total.ToString();
						if (okTxt != null) okTxt.Text = _Config.ok.ToString();
						if (ngTxt != null) ngTxt.Text = (_Config.total - _Config.ok).ToString();
						if (BaoGuanNGTxt != null) BaoGuanNGTxt.Text = _Config.burstExcludeCount.ToString();

						if (yieldTxt != null && _Config.total > 0)
						{
							// 良率 = OK合格数 ÷（总检测数 - 被剔除的连续爆管异常数量）
							double effectiveCount = _Config.total - _Config.burstExcludeCount;
							double yieldRate = effectiveCount > 0 ? Math.Min(100.0, Math.Max(0.0, (_Config.ok * 100.0) / effectiveCount)) : 0;
							yieldTxt.Text = yieldRate.ToString("F2") + "%";
							yieldTxt.ForeColor = yieldRate > 95 ? Color.Green : yieldRate > 90 ? Color.Orange : Color.Red;
						}
					}
					catch { }
				}));

				// 【P0修复】跨线程安全：InitData 操作 UI 控件必须通过 BeginInvoke
				this.BeginInvoke(new Action(() => { if (!_isClosing) InitData(); }));
			}
			catch (Exception ex) { FastLogger.Instance.Error($"更新计数异常: {ex.Message}"); }
		}

		private void InitData()
		{
			if (_isClosing) return;
			versionNum.Text = _Config.CurCheckSpec.ToString() != "" ? _Config.CurCheckSpec.ToString() : " - ";
			ImageQueueTxt1.Text = _processor1?.ImageQueueCount.ToString() ?? "0";
			ImageQueueTxt2.Text = _processor2?.ImageQueueCount.ToString() ?? "0";
			ImageQueueTxt3.Text = _processor3?.ImageQueueCount.ToString() ?? "0";
			ImageQueueTxt4.Text = _processor4?.ImageQueueCount.ToString() ?? "0";
			ImageQueueTxt5.Text = _processor5?.ImageQueueCount.ToString() ?? "0";
			ResultQueueTxt1.Text = _processor1?.ResultQueueCount.ToString() ?? "0";
			ResultQueueTxt2.Text = _processor2?.ResultQueueCount.ToString() ?? "0";
			ResultQueueTxt3.Text = _processor3?.ResultQueueCount.ToString() ?? "0";
			ResultQueueTxt4.Text = _processor4?.ResultQueueCount.ToString() ?? "0";
			ResultQueueTxt5.Text = _processor5?.ResultQueueCount.ToString() ?? "0";
			InitState.State = IFInit == 1 ? UILightState.On : UILightState.Off;
			InitState.Text = IFInit == 1 ? "系统正常" : "未初始化";
			offset = _Config.Offset;
			k = _Config.K;
			astrict = _Config.Astrict;
		}
		#endregion

		#region 窗体关闭和资源清理（优化版）
		private void MainFrm_FormClosing(object sender, FormClosingEventArgs e)
		{
			// 防止重复关闭
			lock (_closeLock)
			{
				if (_isClosing) return;
				_isClosing = true;
			}

			try
			{
				// CloseReason 区分关闭来源: UserClosing=用户手动关 / WindowsShutDown=系统关机
				// / TaskManagerClosing=任务管理器结束 / ApplicationExitCall=代码调用退出
				try { FastLogger.Instance.Info($"MainFrm_FormClosing 开始关闭流程 (关闭原因: {e.CloseReason})"); } catch { }
				FastLogger.Instance.Info("应用程序正在关闭...");

				// 取消所有任务
				// 【P2】事件反注册，防止委托泄漏
				try { modbusClass.EventConnectState -= ModbusConnectState; } catch { }
				try { modbusClass.EventCount -= PLCCountMethod; } catch { }
				_cts?.Cancel();

				// 停止接收新图像
				StopCameraGrab();

				// 停止性能监控
				_performanceTimer?.Stop();
				_performanceTimer?.Dispose();

				// 先断开PLC，让WriteResultThread中的PLC写入立即失败返回
				modbusClass?.Dispose();

				// 停止处理器
				StopProcessors();

				// 停止并等待线程（PLC已断开，WriteResultThread能快速退出）
				StopAllThreads();

				// 释放保存器
				DisposeHighSpeedSavers();

				// 释放内存池
				DisposeMemoryPools();

				// 关闭相机
				DisposeCameras();

				// 停止定时器 + 刷新最后的数据库记录
				timer1?.Stop();
				_batchFlushTimer?.Stop();
				_batchFlushTimer?.Dispose();
				FlushPendingRecords();

				// 清理其他资源
				CleanupAllResources();

				// 关闭运动控制卡连接
				try
				{
					if (g_handle != IntPtr.Zero)
					{
						myZmcaux.CloseConnect(g_handle);
						g_handle = IntPtr.Zero;
						FastLogger.Instance.Info("运动控制卡已断开");
					}
				}
				catch (Exception ex) { FastLogger.Instance.Error($"断开运控卡异常: {ex.Message}"); }

				// 释放AI模型资源
				DisposeAIModels();

				// 通知等待完成
				_closeWaitHandle.Set();

				// 【P2】退出前强制落盘计数器（无5秒节流）
				try { CommonLib.Class_Config.FlushCountersNow(); } catch { }

				// 释放数据库记录器前，清理所有滞留的存图待处理条目，归还对象池
				{
					var pendingKeys = _pendingImageSaves.Keys.ToArray();
					foreach (var key in pendingKeys)
					{
						if (_pendingImageSaves.TryRemove(key, out var items))
						{
							foreach (var item in items)
								try { QueueResultItem.Return(item); } catch { }
						}
					}
					try { FastLogger.Instance.Info($"[关机] 清理{pendingKeys.Length}个残留存图条目"); } catch { }
				}

				_dbRecorder?.Dispose();
				_diskGuard?.Dispose();

				try { FastLogger.Instance.Info("MainFrm_FormClosing 关闭完成"); } catch { }
				FastLogger.Instance.Info("应用程序关闭完成");
				// 走到这里说明完整关闭流程全部执行完毕，删除会话标记（崩溃/强杀不会到达此处）
				SessionMarker.MarkCleanExit();
			}
			catch (Exception ex)
			{
				try { FastLogger.Instance.Error("MainFrm_FormClosing 异常", ex); } catch { }
				FastLogger.Instance.Error($"关闭时异常: {ex.Message}");
			}
			finally
			{
				_closeWaitHandle?.Dispose();
				_cts?.Dispose();
				// 等待日志刷完，强制退出（防止后台线程残留导致进程不结束）
				System.Threading.Thread.Sleep(500);
				Environment.Exit(0);
			}
		}

		private void StopCameraGrab()
		{
			try
			{
				const int CAM_TIMEOUT = 2000;
				// 每个相机操作加超时，防止SDK内部卡死拖住UI线程
				var cameras = new[] { camera1SDK, camera2SDK, camera3SDK, camera4SDK, camera5SDK };
				string[] names = { "Cam1", "Cam2", "Cam3", "Cam4", "Cam5" };
				for (int i = 0; i < cameras.Length; i++)
				{
					var cam = cameras[i];
					if (cam == null) continue;
					try
					{
						var task = Task.Run(() => cam.StopStreamGrabber());
						if (!task.Wait(CAM_TIMEOUT))
							FastLogger.Instance.Info($"⚠ {names[i]} StopStreamGrabber 超时({CAM_TIMEOUT}ms)，跳过");
					}
					catch (Exception ex) { FastLogger.Instance.Error($"{names[i]} StopStreamGrabber 异常: {ex.Message}"); }
				}
				FastLogger.Instance.Info("相机采图已停止");
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"停止相机采图异常: {ex.Message}");
			}
		}

		private void StopProcessors()
		{
			try
			{
				_processor1?.Dispose();
				_processor2?.Dispose();
				_processor3?.Dispose();
				_processor4?.Dispose();
				_processor5?.Dispose();
				_resultMatcher?.Dispose();
				FastLogger.Instance.Info("处理器已释放");
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"释放处理器异常: {ex.Message}");
			}
		}

		private void StopAllThreads()
		{
			try
			{
				var threads = new Thread[] { updateThread, ReadIO1Thread, WriteResultThread };
				int timeout = 3000;

				foreach (var thread in threads)
				{
					if (thread != null && thread.IsAlive)
					{
						try
						{
							if (!thread.Join(timeout))
							{
								FastLogger.Instance.Info($"线程 {thread.Name} 未在 {timeout}ms 内结束");
							}
						}
						catch (Exception ex)
						{
							FastLogger.Instance.Error($"等待线程结束异常: {ex.Message}");
						}
					}
				}

				FastLogger.Instance.Info("所有工作线程已停止");
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"停止线程异常: {ex.Message}");
			}
		}

		private void DisposeHighSpeedSavers()
		{
			try
			{
				_highSpeedSaver1?.Dispose();
				_highSpeedSaver2?.Dispose();
				_highSpeedSaver3?.Dispose();
				_highSpeedSaver4?.Dispose();
				_highSpeedSaver5?.Dispose();
				FastLogger.Instance.Info("高速保存器已释放");
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"释放高速保存器异常: {ex.Message}");
			}
		}

		private void DisposeMemoryPools()
		{
			try
			{
				_bufferPool1?.Dispose();
				_bufferPool2?.Dispose();
				_bufferPool3?.Dispose();
				_bufferPool4?.Dispose();
				_bufferPool5?.Dispose();
				FastLogger.Instance.Info("内存池已释放");
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"释放内存池异常: {ex.Message}");
			}
		}

		private void DisposeCameras()
		{
			try
			{
				const int CAM_TIMEOUT = 2000;
				var cameras = new[] { camera1SDK, camera2SDK, camera3SDK, camera4SDK, camera5SDK };
				string[] names = { "Cam1", "Cam2", "Cam3", "Cam4", "Cam5" };
				for (int i = 0; i < cameras.Length; i++)
				{
					var cam = cameras[i];
					if (cam == null) continue;
					try
					{
						var task = Task.Run(() => cam.Close());
						if (!task.Wait(CAM_TIMEOUT))
							FastLogger.Instance.Info($"⚠ {names[i]} Close 超时({CAM_TIMEOUT}ms)，跳过");
					}
					catch (Exception ex) { FastLogger.Instance.Error($"{names[i]} Close 异常: {ex.Message}"); }
				}
				FastLogger.Instance.Info("相机已关闭");
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"关闭相机异常: {ex.Message}");
			}
		}

		private void CleanupAllResources()
		{
			try
			{
				FastLogger.Instance.Info("开始清理所有资源...");

				lock (SendResultList)
				{
					SendResultList?.Clear();
				}

				// 关闭时直接操作，不需要 Invoke（已在UI线程）
				if (this.IsHandleCreated)
				{
					var pictureBoxes = new[] { xlPictureBox1, xlPictureBox2, xlPictureBox3, xlPictureBox4, xlPictureBox5 };
					foreach (var pb in pictureBoxes)
					{
						if (pb != null && pb.Image != null)
						{
							try { pb.Image.Dispose(); pb.Image = null; } catch { }
						}
					}
				}

				// Server GC 会在空闲时自动回收，关闭路径上正常 Dispose 已足够，
				// 无需手动 GC.Collect()——这会强制全代回收，在大量 Mat 等非托管资源
				// 仍在使用时可能造成长时间卡顿甚至死锁
				FastLogger.Instance.Info("资源清理完成");
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"清理资源异常: {ex.Message}");
			}
		}

		/// <summary>
		/// 释放AI模型资源，防止程序关闭时GPU资源未释放导致卡死
		/// </summary>
		private void DisposeAIModels()
		{
			try
			{
				Model_Segmentation_Cam1?.Dispose();
				Model_Class_Cam2?.Dispose();
				Model_Segmentation_Cam4?.Dispose();
				Model_Char_Cam4?.Dispose();
				Model_Char_Cam5?.Dispose();
				Model_Char_PCode_Cam5?.Dispose();
				Model_Color_Cam5?.Dispose();
				Model_Rests_Cam5?.Dispose();
				Model_Segmentation_Cam5?.Dispose();
				FastLogger.Instance.Info("AI模型已释放");
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"释放AI模型异常: {ex.Message}");
			}
		}
		#endregion

		#region 数据保存
		/// <summary>
		/// 检查班次变化并自动保存（在定时器中调用）
		/// </summary>
		private void CheckShiftChangeAndAutoSave()
		{
			if (_isClosing) return;
			try
			{
				DateTime now = DateTime.Now;
				string newShift = GetShiftByTime(now);
				string newShiftDate = GetShiftDate(now);

				// 班次发生变化
				if (!string.IsNullOrEmpty(_currentShift) && _currentShift != newShift)
				{
					// 上一个班次结束，自动保存
					AutoSaveShiftReport(_currentShiftDate, _currentShift);
					FastLogger.Instance.Debug($"班次切换: {_currentShift} -> {newShift}, 已自动保存{_currentShift}班次报表");

					// 清空统计数据，准备新班次
					this.Invoke(new Action(() =>
					{
						ClearStatisticsDisplay();
					}));
				}

				_currentShift = newShift;
				_currentShiftDate = newShiftDate;
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"检查班次变化异常: {ex.Message}");
			}
		}

		/// <summary>
		/// 根据时间获取班次
		/// </summary>
		private string GetShiftByTime(DateTime time)
		{
			int hour = time.Hour;
			if (hour >= 8 && hour <= 15) return "早班";
			if (hour >= 16 && hour <= 23) return "中班";
			return "夜班";  // 00:00 - 07:59
		}

		/// <summary>
		/// 获取班次归属日期
		/// </summary>
		private string GetShiftDate(DateTime time)
		{
			int hour = time.Hour;
			if (hour >= 0 && hour <= 7)  // 夜班归属前一天
				return time.AddDays(-1).ToString("yyyy-MM-dd");
			return time.ToString("yyyy-MM-dd");
		}

		/// <summary>
		/// 自动保存班次报表到本地
		/// </summary>
		private void AutoSaveShiftReport(string date, string shift)
		{
			// 班次切换时自动保存仅导出汇总表（主表），不导出明细记录（副表）
			if (_dbRecorder != null)
			{
				_dbRecorder.ExportFullShiftReport(date, shift, skipDetailExport: true);
			}
		}

		/// <summary>
		/// 手动保存当前班次报表
		/// </summary>
		public void ManualSaveCurrentShiftReport()
		{
			if (!string.IsNullOrEmpty(_currentShiftDate) && !string.IsNullOrEmpty(_currentShift))
			{
				FastLogger.Instance.Debug($"开始手动保存班次报表: {_currentShiftDate} {_currentShift}");

				// 异步导出汇总表，保存完成后自动打开文件夹
				if (_dbRecorder != null)
				{
					_dbRecorder.ExportFullShiftReport(_currentShiftDate, _currentShift, openFolderAfterExport: true, skipDetailExport: true);
				}
			}
		}
		#endregion

		#region 原有方法
		private void uiButton5_Click(object sender, EventArgs e)
		{
			lock (SendResultList)
			{
				SendResultList.Clear();
			}
		}

		private void uiButton1_Click(object sender, EventArgs e)
		{
			Random random = new Random();
			testflag = true;

			Bitmap bitmap1 = new Bitmap(imagePath1);
			Bitmap bitmap2 = new Bitmap(imagePath2);
			Bitmap bitmap3 = new Bitmap(imagePath3);
			Bitmap bitmap4 = new Bitmap(imagePath4);
			Bitmap bitmap5 = new Bitmap(imagePath5);

			Task.Run(() =>
			{
				while (testflag && !_isClosing)
				{
					Thread.Sleep(200);
					OnCamera1Image(bitmap1, null, null);
					OnCamera2Image(bitmap2, null, null);
					OnCamera3Image(bitmap3, null, null);
					OnCamera4Image(bitmap4, null, null);
					OnCamera5Image(bitmap5, null, null);
				}
			});
		}

		private void uiButton3_Click(object sender, EventArgs e)
		{
			testflag = false;
		}

		bool testflag = true;
		string imagePath1 = @"D:\bin\AI\Image\Camera1.bmp";
		string imagePath2 = @"D:\bin\AI\Image\Camera2.bmp";
		string imagePath2_1 = @"E:\公司-张皓茗\项目\高露洁\广州\夹尾正反面\广州高露洁牙膏字符测试\bin\AI\Cam2\Pic_2026_01_10_192517_1220.bmp";
		string imagePath3 = @"D:\bin\AI\Image\Camera3-6.bmp";
		string imagePath4 = @"D:\bin\AI\Image\Camera4.bmp";
		string imagePath5 = @"D:\bin\AI\Image\camera5_3.bmp";    // Camera5 第1张
		string imagePath5_2 = @"D:\bin\AI\Image\camera5_2.bmp";  // Camera5 第2张
		string imagePath5_3 = @"D:\bin\AI\Image\camera5_2.bmp";  // Camera5 第3张
		string imagePath5_4 = @"D:\bin\AI\Image\camera5_2.bmp";  // Camera5 第4张
		string imagePath5_5 = @"D:\bin\AI\Image\Camera5_6.png";  // Camera5 第5张

		private void uiButton4_Click(object sender, EventArgs e)
		{
			Random random = new Random();
			testflag = true;

			FastLogger.Instance.Info($"imagePath1: {imagePath1}");
			FastLogger.Instance.Info($"imagePath2: {imagePath2}");
			FastLogger.Instance.Info($"imagePath3: {imagePath3}");
			FastLogger.Instance.Info($"imagePath4: {imagePath4}");
			FastLogger.Instance.Info($"imagePath5: {imagePath5}");

			try
			{
				Bitmap bitmap1 = new Bitmap(imagePath1);
				Bitmap bitmap2 = new Bitmap(imagePath2);
				Bitmap bitmap3 = new Bitmap(imagePath3);
				Bitmap bitmap4 = new Bitmap(imagePath4);
				Bitmap bitmap5 = new Bitmap(imagePath5);

				OnCamera1Image(bitmap1, null, null);
				OnCamera2Image(bitmap2, null, null);
				OnCamera3Image(bitmap3, null, null);
				OnCamera4Image(bitmap4, null, null);
				OnCamera5Image(bitmap5, null, null);
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"创建 Bitmap 错误: {ex.Message}");
			}
		}

		private void totalTxt_Click(object sender, EventArgs e)
		{
			// 保留事件绑定（Designer 引用），仅清空清统计
			ClearStatisticsDisplay();
		}

		/// <summary>
		/// 清空统计数据显示
		/// </summary>
		private void ClearStatisticsDisplay()
		{
			try
			{
				Class_Config.ResetAllCounters();

				if (totalTxt != null) totalTxt.Text = "0";
				if (okTxt != null) okTxt.Text = "0";
				if (ngTxt != null) ngTxt.Text = "0";
				if (BaoGuanNGTxt != null) BaoGuanNGTxt.Text = "0";
				if (yieldTxt != null)
				{
					yieldTxt.Text = "0.00%";
					yieldTxt.ForeColor = Color.Green;
				}
			}
			catch { }
		}

		private void DrawChineseText(Bitmap bmp, string title, bool result, string id, string extraInfo)
		{
			using (Graphics g = Graphics.FromImage(bmp))
			using (var brushGreen = new SolidBrush(Color.LawnGreen))
			using (var brushRed = new SolidBrush(Color.Red))
			using (var brushWhite = new SolidBrush(Color.White))
			{
				int drawX = bmp.Width - 550;
				g.DrawString(title, Font_Main, brushGreen, drawX, 50);
				g.DrawString($"综合结果：{(result ? "合格" : "NG")}", Font_Main, result ? brushGreen : brushRed, drawX, 100);
				g.DrawString($"产品序号：{id}", Font_Sub, brushWhite, drawX, 150);
				if (!string.IsNullOrEmpty(extraInfo))
					g.DrawString(extraInfo, Font_Sub, brushWhite, drawX, 200);
			}
		}

		private volatile bool _simRunning = false;
		// 测试模式：手动导入/固定路径模拟时跳过计数、写库、PLC，结果图单独存入Test子目录
		private long _testPendingFrames;       // 手动导入的待处理帧数（Increment/Decrement 原子操作）
		private long _lastSimStopTicks;        // 模拟运行停止时刻（用作5秒尾窗兜住管道中的残留帧）
		private bool IsTestResult()
		{
			return _simRunning
				|| Interlocked.Read(ref _testPendingFrames) > 0
				|| (Interlocked.Read(ref _lastSimStopTicks) > 0
					&& DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastSimStopTicks) < 5L * TimeSpan.TicksPerSecond);
		}
		private int _simCam5Idx = 0;  // Camera5 五张图轮播
		private long _simBatchNum = 0;  // 模拟批次计数
		private Stopwatch _simGroupSw = new Stopwatch();
		private List<ProductionRecord> _pendingRecords = new List<ProductionRecord>();
		private readonly object _pendingLock = new object();
		private DateTime _lastRecordTime = DateTime.Now;
		private System.Timers.Timer _batchFlushTimer;

		private async void clearBtn_Click(object sender, EventArgs e)
		{
			if (_simRunning)
			{
				_simRunning = false;
				// 记录停止时刻用作5秒尾窗，兜住流水线里还在路上的残留帧
				Interlocked.Exchange(ref _lastSimStopTicks, DateTime.UtcNow.Ticks);
				clearBtn.Text = "模拟运行";
				FastLogger.Instance.Info($"[手动测试] 固定路径模拟运行停止 (已模拟{_simBatchNum}批)");
				return;
			}

			// 仅检查已启用工位的图片路径
			if (_cameraEnabled[0] && !File.Exists(imagePath1)) { FastLogger.Instance.Warn($"[手动测试] 模拟运行启动失败: Camera1 图片不存在 {imagePath1}"); MessageBox.Show($"Camera1 图片不存在:\n{imagePath1}"); return; }
			if (_cameraEnabled[1] && !File.Exists(imagePath2)) { FastLogger.Instance.Warn($"[手动测试] 模拟运行启动失败: Camera2 图片不存在 {imagePath2}"); MessageBox.Show($"Camera2 图片不存在:\n{imagePath2}"); return; }
			if (_cameraEnabled[2] && !File.Exists(imagePath3)) { FastLogger.Instance.Warn($"[手动测试] 模拟运行启动失败: Camera3 图片不存在 {imagePath3}"); MessageBox.Show($"Camera3 图片不存在:\n{imagePath3}"); return; }
			if (_cameraEnabled[3] && !File.Exists(imagePath4)) { FastLogger.Instance.Warn($"[手动测试] 模拟运行启动失败: Camera4 图片不存在 {imagePath4}"); MessageBox.Show($"Camera4 图片不存在:\n{imagePath4}"); return; }
			if (_cameraEnabled[4])
			{
				if (!File.Exists(imagePath5)) { FastLogger.Instance.Warn($"[手动测试] 模拟运行启动失败: Camera5 图片不存在 {imagePath5}"); MessageBox.Show($"Camera5 图片不存在:\n{imagePath5}"); return; }
			}

			_simRunning = true;
			_simBatchNum = 0;
			clearBtn.Text = "停止模拟";
			int enabledCount = _cameraEnabled.Count(x => x);
			FastLogger.Instance.Info($"[手动测试] 固定路径模拟运行启动, {enabledCount}个工位, 3个/组间隔10ms");
			if (_cameraEnabled[0]) FastLogger.Instance.Info($"[手动测试] Camera1 固定图: {imagePath1}");
			if (_cameraEnabled[1]) FastLogger.Instance.Info($"[手动测试] Camera2 固定图: {imagePath2}");
			if (_cameraEnabled[2]) FastLogger.Instance.Info($"[手动测试] Camera3 固定图: {imagePath3}");
			if (_cameraEnabled[3]) FastLogger.Instance.Info($"[手动测试] Camera4 固定图: {imagePath4}");
			if (_cameraEnabled[4]) FastLogger.Instance.Info($"[手动测试] Camera5 固定图: {imagePath5} (共5张轮播)");

			await Task.Run(() =>
			{
				while (_simRunning && !_isClosing)
				{
					_simGroupSw.Restart();
					for (int g = 0; g < 3 && _simRunning && !_isClosing; g++)
					{
						try
						{
							_simBatchNum++;
							var sw = Stopwatch.StartNew();

							if (_cameraEnabled[0]) { var bmp1 = new Bitmap(imagePath1); OnCamera1Image(bmp1, null, null); }
							if (_cameraEnabled[1]) { var bmp2 = new Bitmap(imagePath2); OnCamera2Image(bmp2, null, null); }
							if (_cameraEnabled[2]) { var bmp3 = new Bitmap(imagePath3); OnCamera3Image(bmp3, null, null); }
							if (_cameraEnabled[3]) { var bmp4 = new Bitmap(imagePath4); OnCamera4Image(bmp4, null, null); }
							if (_cameraEnabled[4])
							{
								_simCam5Idx = (_simCam5Idx + 1) % 5;
								string cam5Path = _simCam5Idx == 0 ? imagePath5 : _simCam5Idx == 1 ? imagePath5_2 : _simCam5Idx == 2 ? imagePath5_3 : _simCam5Idx == 3 ? imagePath5_4 : imagePath5_5;
								var bmp5 = new Bitmap(cam5Path);
								OnCamera5Image(bmp5, null, null);
							}

							sw.Stop();
							try { if (FastLogger.IsInitialized) FastLogger.Instance.Info($"[模拟] 批次{_simBatchNum} 添加耗时{sw.ElapsedMilliseconds}ms"); } catch { }

							if (g < 2)
								Thread.Sleep(100);
						}
						catch (Exception ex) { FastLogger.Instance.Error($"模拟运行异常: {ex.Message}"); }
					}

					_simGroupSw.Stop();
					long remaining = 600 - _simGroupSw.ElapsedMilliseconds;
					if (remaining > 10)
						Thread.Sleep((int)remaining);
				}
			});

			_simRunning = false;
			this.Invoke(new Action(() => clearBtn.Text = "模拟运行"));
			FastLogger.Instance.Info($"[手动测试] 固定路径模拟运行循环结束 (共模拟{_simBatchNum}批)");
		}

		private Stopwatch _sendIntervalTimer = Stopwatch.StartNew();
		private List<long> _sendIntervals = new List<long>();

		private void WriteResultMethod()
		{
			try
			{
				FastLogger.Instance.Info("写入结果线程启动");

				int consecutiveFailures = 0;
				const int MAX_CONSECUTIVE_FAILURES = 50;
				int startIndex = -1; // -1=尚未初始化，首次匹配时取列表中实际最小 SequenceId
				bool result1 = false, result2 = false, result3 = false;
				int _plcSendCount = 0; // PLC发送计数器

				while (!_isClosing)
				{
					try
					{
						if (!modbusClass.modbusState)
						{
							if (consecutiveFailures < MAX_CONSECUTIVE_FAILURES)
							{
								consecutiveFailures++;
								Thread.Sleep(1000);
							}
							else
							{
								Thread.Sleep(5000);
							}
							continue;
						}

						consecutiveFailures = 0;

						QueueResultItem station1 = null, station2 = null, station3 = null;
						lock (SendResultList)
						{
							// 首次初始化：用列表中实际最小 SequenceId 作为起点（兼容 offset>0 等非 1 起始场景）
							if (startIndex < 0 && SendResultList.Count > 0)
							{
								startIndex = (int)SendResultList.Min(item => item.SequenceId);
								FastLogger.Instance.Debug($"[PLC] startIndex 初始化为 {startIndex}");
							}

							if (SendResultList.Count >= 3 + offset_send)
							{
								// 【保持原状】维持原有的单向线性检索，确保绝对按照你原本的一致性匹配
								station1 = SendResultList.FirstOrDefault(item => item != null && item.SequenceId == startIndex);
								station2 = SendResultList.FirstOrDefault(item => item != null && item.SequenceId == startIndex + 1);
								station3 = SendResultList.FirstOrDefault(item => item != null && item.SequenceId == startIndex + 2);
							}
						}

						if (station1 != null && station2 != null && station3 != null)
						{
							result1 = station1?.Result ?? false;
							result2 = station2?.Result ?? false;
							result3 = station3?.Result ?? false;

							bool writeSuccess = false;
							int retryCount = 0;
							const int MAX_RETRY = 3;

							while (!writeSuccess && retryCount < MAX_RETRY && !_isClosing)
							{
								try
								{
									var _plcSw = Stopwatch.StartNew();
									writeSuccess = modbusClass.WriteResult(result1, result2, result3);
									_plcSw.Stop();
									long _plcMs = _plcSw.ElapsedMilliseconds;
									Interlocked.Increment(ref _plcPerfSendCount);
									Interlocked.Add(ref _plcPerfTotalMs, _plcMs);
									// CAS 无锁更新写入耗时最大值（PerfSlot 自旋模式）
								long _cm = _plcPerfMaxMs;
								while (_plcMs > _cm)
								{
									long _o = Interlocked.CompareExchange(ref _plcPerfMaxMs, _plcMs, _cm);
									if (_o == _cm) break;
									_cm = _o;
								}
									long _ivMs = -1;
									if (_plcPerfIntervalWatch.IsRunning)
								{
									_ivMs = _plcPerfIntervalWatch.ElapsedMilliseconds;
									_plcPerfIntervalWatch.Restart();
									long _ci = _plcPerfIntervalMaxMs;
									while (_ivMs > _ci)
									{
										long _ox = Interlocked.CompareExchange(ref _plcPerfIntervalMaxMs, _ivMs, _ci);
										if (_ox == _ci) break;
										_ci = _ox;
									}
								}
									else { _plcPerfIntervalWatch.Start(); }
									_plcSendCount++;
									try
									{
										FastLogger.Instance.Info("PLC发送[" + _plcSendCount + "]: ID=" + startIndex + "-" + (startIndex + 2)
											+ " R1=" + result1 + " R2=" + result2 + " R3=" + result3
											+ " 成功=" + writeSuccess + " 写耗时=" + _plcMs + "ms"
											+ (_ivMs >= 0 ? " 距上次=" + _ivMs + "ms" : ""));
									}
									catch { }
									// 调试日志：周期输出PLC发送统计
									if (RunLogEnabled)
									{
										long debugCount = Interlocked.Increment(ref _debugPlcSendCount);
										if (debugCount <= 1 || debugCount % DEBUG_LOG_INTERVAL == 0)
											try { if (FastLogger.IsInitialized) FastLogger.Instance.Info($"[PLC-Debug] 发送#{debugCount} ID:{startIndex}-{startIndex + 2} R1:{result1} R2:{result2} R3:{result3} 成功:{writeSuccess} 队列:{SendResultList.Count}"); } catch { }
									}
									if (writeSuccess)
									{
										long interval = _sendIntervalTimer.ElapsedMilliseconds;
										_sendIntervals.Add(interval);
										_sendIntervalTimer.Restart();

										if (_sendIntervals.Count > 10)
										{
											double avg = _sendIntervals.Average();
											try { if (FastLogger.IsInitialized) FastLogger.Instance.Info($"平均发送间隔: {avg:F0}ms"); } catch { }

											// 【唯一改动点：真·内存泄漏修复】
											// 仅在此处对测速队列进行清空，切断内存无限制暴涨的隐患。对生产队列零干扰。
											_sendIntervals.Clear();
										}

										if (!_isClosing)
										{
											this.BeginInvoke(new Action(() =>
											{
												if (_isClosing) return;
												try
												{
													if (!result1) writeNGCount++;
													if (!result2) writeNGCount++;
													if (!result3) writeNGCount++;
													resultBool1.Text = result1.ToString();
													resultBool2.Text = result2.ToString();
													resultBool3.Text = result3.ToString();
													resultBool1.ForeColor = result1 ? Color.Green : Color.Red;
													resultBool2.ForeColor = result2 ? Color.Green : Color.Red;
													resultBool3.ForeColor = result3 ? Color.Green : Color.Red;
													WriteCountTxt.Text = writeNGCount.ToString();
												}
												catch (Exception uiEx) { FastLogger.Instance.Error($"更新UI异常: {uiEx.Message}"); }
											}));
										}

										// 【保持原状】维持原汁原味的串行擦除逻辑，确保产品数量、递增步长绝对不跑偏
										lock (SendResultList)
										{
											SendResultList.RemoveAll(item => item.SequenceId == startIndex);
											SendResultList.RemoveAll(item => item.SequenceId == startIndex + 1);
											SendResultList.RemoveAll(item => item.SequenceId == startIndex + 2);
										}
										startIndex += 3;
										Thread.Sleep(1); // 成功发送后短暂让出CPU，防止自旋耗尽单核
									}
									else
									{
										retryCount++;
										FastLogger.Instance.Error($"写入PLC失败，重试 {retryCount}/{MAX_RETRY}");
										Thread.Sleep(1);
									}
								}
								catch (Exception writeEx)
								{
									retryCount++;
									FastLogger.Instance.Error($"写入PLC异常，重试 {retryCount}/{MAX_RETRY}: {writeEx.Message}");
									Thread.Sleep(1);
								}
							}

							if (!writeSuccess)
							{
								FastLogger.Instance.Error(string.Format("PLC写入失败已达最大重试{0}次, 跳过 startIndex={1}", MAX_RETRY, startIndex));
								lock (SendResultList)
								{
									SendResultList.RemoveAll(item => item.SequenceId == startIndex);
									SendResultList.RemoveAll(item => item.SequenceId == startIndex + 1);
									SendResultList.RemoveAll(item => item.SequenceId == startIndex + 2);
								}
								startIndex += 3;
							}
						}
						else
						{
							// 等待:结果还没匹配完,凑不齐连续3个就睡10ms再轮询,不补假结果
							Thread.Sleep(10);
						}
					}
					catch (Exception ex)
					{
						FastLogger.Instance.Error($"写入结果线程异常: {ex.Message}");
						Thread.Sleep(1000);
					}
				}
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"写入结果线程严重异常: {ex.Message}\n{ex.StackTrace}");
			}
		}

		private void CountMethod()
		{
			if (_isClosing) return;
			try
			{
				this.BeginInvoke(new Action(() =>
				{
					if (_isClosing) return;
					input1Txt.Text = inputCount1.ToString();
					input2Txt.Text = inputCount2.ToString();
					input3Txt.Text = inputCount3.ToString();
					input4Txt.Text = inputCount4.ToString();
					input5Txt.Text = inputCount5.ToString();
					output1Txt.Text = outputCount1.ToString();
					output2Txt.Text = outputCount2.ToString();
					output3Txt.Text = outputCount3.ToString();
					output4Txt.Text = outputCount4.ToString();
					output5Txt.Text = outputCount5.ToString();
					resultCount1Txt.Text = resultCount1.ToString();
					resultCount2Txt.Text = resultCount2.ToString();
					resultCount3Txt.Text = resultCount3.ToString();
					resultCount4Txt.Text = resultCount4.ToString();
					resultCount5Txt.Text = resultCount5.ToString();
				}));
			}
			catch (Exception ex) { throw; }
		}

		private void UpdateMethod()
		{
			try
			{
				int updateCounter = 0;
				while (!_isClosing)
				{
					Thread.Sleep(500);
					updateCounter++;

					if (updateCounter % 6 == 0)
					{
						this.BeginInvoke(new Action(() =>
						{
							if (!_isClosing)
							{
								try
								{
									UpdatePerformanceDisplay();
									CountMethod();
								}
								catch { }
							}
						}));
					}

					if (updateCounter % 60 == 0)
					{
						int totalQueue = (_processor1?.ImageQueueCount ?? 0) + (_processor2?.ImageQueueCount ?? 0) +
										 (_processor3?.ImageQueueCount ?? 0) + (_processor4?.ImageQueueCount ?? 0) +
										 (_processor5?.ImageQueueCount ?? 0);
						if (totalQueue > 500)
						{
							FastLogger.Instance.Debug($"队列积压: {totalQueue}");
						}
					}
				}
			}
			catch (Exception ex)
			{
				if (!_isClosing)
					FastLogger.Instance.Error($"更新线程异常: {ex.Message}");
			}
		}
		#endregion

		#region 窗体按钮
		private void mainTitleBar1_OnMenuButtonClick(object sender, EventArgs e)
		{
			System.Drawing.Point point = new System.Drawing.Point(3, 5);
			TabFrm tabFrm = new TabFrm(point, this);
			tabFrm.Show();
		}

		private void mainTitleBar1_OnMinButtonClick(object sender, EventArgs e)
		{
			this.WindowState = FormWindowState.Minimized;
		}

		private void mainTitleBar1_OnCloseButtonClick(object sender, EventArgs e)
		{
			if (MessageBox.Show("确定要退出程序吗？", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				try { FastLogger.Instance.Info("[会话] 用户点击退出按钮并确认退出"); } catch { }
				this.Close();
				// Application.Exit() 已移除，Close() 足以触发完整清理流程
			}
		}
		#endregion

		public void Vision_ChangeMode(string spec)
		{
			try
			{
				this.Invoke(new Action(() =>
				{
					FastLogger.Instance.Info("切换事件触发了 spec: " + spec);
					versionNum.Text = _Config.CurCheckSpec.ToString() != "" ? _Config.CurCheckSpec.ToString() : " - ";
					Task.Run(() =>
					{
						if (!_isClosing)
						{
							FastLogger.Instance.Info($"切换型号时，轴1自动移动至拍照位。Position：{_Config.zhengPosition}");
							try { FastLogger.Instance.Info("运动控制: 轴1移动至 " + _Config.zhengPosition); } catch { }
							myZmcaux.GoPosition(g_handle, 1, Convert.ToSingle(_Config.zhengPosition));
							FastLogger.Instance.Info($"轴1自动移动至拍照位完成，当前轴1位置：{myZmcaux.GetLocation(g_handle, 1)}\r\n");
							try { FastLogger.Instance.Info("运动控制: 轴1移动完成 位置=" + myZmcaux.GetLocation(g_handle, 1)); } catch { }

							FastLogger.Instance.Info($"切换型号时，轴0自动移动至拍照位。Position：{_Config.fanPosition}");
							try { FastLogger.Instance.Info("运动控制: 轴0移动至 " + _Config.fanPosition); } catch { }
							myZmcaux.GoPosition(g_handle, 0, Convert.ToSingle(_Config.fanPosition));
							FastLogger.Instance.Info($"轴0自动移动至拍照位完成，当前轴0位置：{myZmcaux.GetLocation(g_handle, 0)}");
							try { FastLogger.Instance.Info("运动控制: 轴0移动完成 位置=" + myZmcaux.GetLocation(g_handle, 0)); } catch { }

							MessageBox.Show("切换型号完成，已将轴移动至对应拍照位！");
						}
					});
				}));
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"系统初始化时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void uiButton3_Click_1(object sender, EventArgs e)
		{
			Test1Method();
		}

		private void Test1Method()
		{
			try
			{
				int enabledCount = _cameraEnabled.Count(x => x);
				if (enabledCount <= 0) { MessageBox.Show("没有启用的工位"); return; }

				// 标记测试模式：后续流水线产出不计产量/不写库/不交互PLC，存图进Test子目录
				Interlocked.Increment(ref _testPendingFrames);
				FastLogger.Instance.Info($"[手动测试] 手动导入图片测试开始 (启用{enabledCount}个工位, 待匹配帧数={Interlocked.Read(ref _testPendingFrames)})");

				// 仅启动启用的相机
				if (_cameraEnabled[0]) StartMethod(CameraSelect.Camera1);
				if (_cameraEnabled[1]) StartMethod(CameraSelect.Camera2);
				if (_cameraEnabled[2]) StartMethod(CameraSelect.Camera3);
				if (_cameraEnabled[3]) StartMethod(CameraSelect.Camera4);
				if (_cameraEnabled[4]) StartMethod(CameraSelect.Camera5);

				OpenFileDialog dialog = new OpenFileDialog();
				dialog.Title = $"请选择{enabledCount}张图片（仅启用的工位）";
				dialog.Multiselect = true;
				dialog.Filter = "图片文件(*.jpg;*.png;*.bmp)|*.jpg;*.png;*.bmp";
				if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
				{
					if (dialog.FileNames.Length != enabledCount)
					{
						FastLogger.Instance.Warn($"[手动测试] 选图数量不符: 需要{enabledCount}张, 实选{dialog.FileNames.Length}张, 已取消");
						MessageBox.Show($"需要选择{enabledCount}张图片（当前启用了{enabledCount}个工位），但选择了{dialog.FileNames.Length}张");
						return;
					}

					// 按启用顺序分配图片到对应相机
					int fileIdx = 0;
					if (_cameraEnabled[0]) { imagePath1 = dialog.FileNames[fileIdx]; FastLogger.Instance.Info($"[手动测试] Camera1 导入图: {imagePath1}"); OnCamera1Image(new Bitmap(imagePath1), null, null); fileIdx++; }
					if (_cameraEnabled[1]) { imagePath2 = dialog.FileNames[fileIdx]; FastLogger.Instance.Info($"[手动测试] Camera2 导入图: {imagePath2}"); OnCamera2Image(new Bitmap(imagePath2), null, null); fileIdx++; }
					if (_cameraEnabled[2]) { imagePath3 = dialog.FileNames[fileIdx]; FastLogger.Instance.Info($"[手动测试] Camera3 导入图: {imagePath3}"); OnCamera3Image(new Bitmap(imagePath3), null, null); fileIdx++; }
					if (_cameraEnabled[3]) { imagePath4 = dialog.FileNames[fileIdx]; FastLogger.Instance.Info($"[手动测试] Camera4 导入图: {imagePath4}"); OnCamera4Image(new Bitmap(imagePath4), null, null); fileIdx++; }
					if (_cameraEnabled[4]) { imagePath5 = dialog.FileNames[fileIdx]; FastLogger.Instance.Info($"[手动测试] Camera5 导入图: {imagePath5}"); OnCamera5Image(new Bitmap(imagePath5), null, null); fileIdx++; }
				}
				else
				{
					FastLogger.Instance.Info("[手动测试] 用户取消选图, 手动导入测试结束");
				}
				return;
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"[手动测试] 手动导入图片异常: {ex.Message}\r\n{ex.StackTrace}");
			}
		}

		private void uiLabel20_Click(object sender, EventArgs e)
		{
			inputCount1 = 0; inputCount2 = 0; inputCount3 = 0; inputCount4 = 0; inputCount5 = 0;
			outputCount1 = 0; outputCount2 = 0; outputCount3 = 0; outputCount4 = 0; outputCount5 = 0;
			resultCount1 = 0; resultCount2 = 0; resultCount3 = 0; resultCount4 = 0; resultCount5 = 0;
			CountMethod();
		}

		#region 工具方法
		private static void DrawPoint(Mat image, OpenCvSharp.Point point, Scalar color, int thickness = -1, int radius = 15)
		{
			try { Cv2.Circle(image, point, radius, color, thickness); }
			catch { }
		}

		private static void DrawRotatedRectangle(Mat image, RotatedRect rotatedRect, Scalar color, int thickness = 2)
		{
			try
			{
				Point2f[] vertices = rotatedRect.Points();
				OpenCvSharp.Point[] points = vertices.Select(v => new OpenCvSharp.Point((int)v.X, (int)v.Y)).ToArray();
				if (thickness == -1)
					Cv2.FillConvexPoly(image, points, color);
				else
					Cv2.Polylines(image, new OpenCvSharp.Point[][] { points }, true, color, thickness);
			}
			catch { }
		}
		#endregion

		#region UI更新方法
		private void UpdatePictureBox1(Bitmap image)
		{
			if (xlPictureBox1.InvokeRequired)
			{
				var cloned = (Bitmap)image.Clone();
				xlPictureBox1.BeginInvoke(new Action(() => UpdatePictureBoxInternal(xlPictureBox1, cloned)));
			}
			else
			{
				UpdatePictureBoxInternal(xlPictureBox1, (Bitmap)image.Clone());
			}
		}

		private void UpdatePictureBoxInternal(PictureBox pictureBox, Bitmap image)
		{
			if (_isClosing) return;
			try
			{
				var oldImage = pictureBox.Image;
				pictureBox.Image = image;
				if (oldImage != null) { try { oldImage.Dispose(); } catch { } }
			}
			catch { }
		}

		private void UpdatePictureBox2(Bitmap image)
		{
			if (xlPictureBox2.InvokeRequired)
			{
				var cloned = (Bitmap)image.Clone();
				xlPictureBox2.BeginInvoke(new Action(() => UpdatePictureBoxInternal2(xlPictureBox2, cloned)));
			}
			else
			{
				UpdatePictureBoxInternal2(xlPictureBox2, (Bitmap)image.Clone());
			}
		}

		private void UpdatePictureBoxInternal2(PictureBox pictureBox, Bitmap image)
		{
			if (_isClosing) return;
			try
			{
				var oldImage = pictureBox.Image;
				pictureBox.Image = image;
				if (oldImage != null) { try { oldImage.Dispose(); } catch { } }
			}
			catch { }
		}

		private void UpdatePictureBox3(Bitmap image)
		{
			if (xlPictureBox3.InvokeRequired)
			{
				var cloned = (Bitmap)image.Clone();
				xlPictureBox3.BeginInvoke(new Action(() => UpdatePictureBoxInternal3(xlPictureBox3, cloned)));
			}
			else
			{
				UpdatePictureBoxInternal3(xlPictureBox3, (Bitmap)image.Clone());
			}
		}

		private void UpdatePictureBoxInternal3(PictureBox pictureBox, Bitmap image)
		{
			if (_isClosing) return;
			try
			{
				var oldImage = pictureBox.Image;
				pictureBox.Image = image;
				if (oldImage != null) { try { oldImage.Dispose(); } catch { } }
			}
			catch { }
		}

		private void timer1_Tick(object sender, EventArgs e)
		{
			// 每隔一段时间检查班次变化（例如每分钟检查一次）
			CheckShiftChangeAndAutoSave();
		}

		private void UpdatePictureBox4(Bitmap image)
		{
			if (xlPictureBox4.InvokeRequired)
			{
				var cloned = (Bitmap)image.Clone();
				xlPictureBox4.BeginInvoke(new Action(() => UpdatePictureBoxInternal4(xlPictureBox4, cloned)));
			}
			else
			{
				UpdatePictureBoxInternal4(xlPictureBox4, (Bitmap)image.Clone());
			}
		}

		private void UpdatePictureBoxInternal4(PictureBox pictureBox, Bitmap image)
		{
			if (_isClosing) return;
			try
			{
				var oldImage = pictureBox.Image;
				pictureBox.Image = image;
				if (oldImage != null) { try { oldImage.Dispose(); } catch { } }
			}
			catch { }
		}

		private void UpdatePictureBox5(Bitmap image)
		{
			if (xlPictureBox5.InvokeRequired)
			{
				var cloned = (Bitmap)image.Clone();
				xlPictureBox5.BeginInvoke(new Action(() => UpdatePictureBoxInternal5(xlPictureBox5, cloned)));
			}
			else
			{
				UpdatePictureBoxInternal5(xlPictureBox5, (Bitmap)image.Clone());
			}
		}

		private void UpdatePictureBoxInternal5(PictureBox pictureBox, Bitmap image)
		{
			if (_isClosing) return;
			try
			{
				var oldImage = pictureBox.Image;
				pictureBox.Image = image;
				if (oldImage != null) { try { oldImage.Dispose(); } catch { } }
			}
			catch { }
		}
		#endregion

		/// <summary>一分钟指标快照，趋势对比用</summary>
		private class MinuteSnapshot
		{
			public Dictionary<string, double> CamAvgMs = new Dictionary<string, double>();
			public double CpuPct, GpuUtil, GpuMemMB, PrivateMB;
			public int Gc2Count;
		}
	}

	#region 原有辅助类（保持兼容）
	public class QueueResultItem : IDisposable
	{
		// 对象池（避免热路径每帧 new，减少 GC 压力），上限 300 个
		private static readonly ConcurrentBag<QueueResultItem> _pool = new ConcurrentBag<QueueResultItem>();
		private const int MAX_POOL_SIZE = 300;

		/// <summary>从对象池租用一个已重置的实例（池空时 new 兜底）</summary>
		public static QueueResultItem Rent()
		{
			if (_pool.TryTake(out var item)) { item.Reset(); return item; }
			return new QueueResultItem();
		}

		/// <summary>归还实例到对象池（自动重置所有字段）</summary>
		public static void Return(QueueResultItem item)
		{
			if (item == null) return;
			item.Reset();
			if (_pool.Count < MAX_POOL_SIZE) _pool.Add(item);
		}

		/// <summary>重置所有字段为默认值</summary>
		public void Reset()
		{
			SequenceId = 0;
			Offset = 0;
			ImageData_Y = null;
			ImageData_R = null;
			Result = false;
			Timestamp = DateTime.Now;
			ProcessStartTime = default;
			ProcessEndTime = default;
			Cam5_CharResult = 1;
			Cam5_PCodeResult = 1;
			Cam5_SebiaoResult = 1;
			Cam5_BaoguanResult = 1;
			Cam5_XiekouResult = 1;
			Cam5_WeijianduanResult = 1;
			IsEmptyCup = false;
			IsPureBurst = false;
			StageTimes?.Clear();
		}

		public long SequenceId { get; set; }
		public int Offset { get; set; }
		public byte[] ImageData_Y { get; set; }
		public byte[] ImageData_R { get; set; }
		public bool Result { get; set; }
		public DateTime Timestamp { get; set; } = DateTime.Now;
		public DateTime ProcessStartTime { get; set; }
		public DateTime ProcessEndTime { get; set; }
		public Dictionary<string, long> StageTimes { get; set; }

		public int Cam5_CharResult { get; set; } = 1;
		public int Cam5_PCodeResult { get; set; } = 1;
		public int Cam5_SebiaoResult { get; set; } = 1;
		public int Cam5_BaoguanResult { get; set; } = 1;
		public int Cam5_XiekouResult { get; set; } = 1;
		public int Cam5_WeijianduanResult { get; set; } = 1;
		public bool IsEmptyCup { get; set; } = false;

		// 是否纯爆管（用于连续异常判定）
		public bool IsPureBurst { get; set; }

		public QueueResultItem() { StageTimes = new Dictionary<string, long>(); }

		public void Dispose()
		{
			// 归还到对象池（Reset 由 Return 内部调用）
			Return(this);
		}
	}

	#endregion
	public enum CameraSelect
	{
		Camera1 = 1, Camera2, Camera3, Camera4, Camera5
	}

	public struct PerformanceStats
	{
		public long TotalTimeMs, MaxTimeMs, MinTimeMs;
		public int ProcessCount;
		public Dictionary<string, long> StageTimes;
		public Dictionary<string, long> StageMin;
		public Dictionary<string, long> StageMax;
		public Dictionary<string, int> StageCount;
		public double AverageTimeMs => ProcessCount > 0 ? (double)TotalTimeMs / ProcessCount : 0;

		public void Reset()
		{
			TotalTimeMs = 0; MaxTimeMs = long.MinValue; MinTimeMs = long.MaxValue; ProcessCount = 0;
			StageTimes?.Clear(); StageMin?.Clear(); StageMax?.Clear(); StageCount?.Clear();
		}

		public void AddTime(long elapsedMs)
		{
			TotalTimeMs += elapsedMs; MaxTimeMs = Math.Max(MaxTimeMs, elapsedMs); MinTimeMs = Math.Min(MinTimeMs, elapsedMs); ProcessCount++;
		}

		public void AddStageTime(string stageName, long elapsedMs)
		{
			if (StageTimes == null) StageTimes = new Dictionary<string, long>();
			if (StageMin == null) StageMin = new Dictionary<string, long>();
			if (StageMax == null) StageMax = new Dictionary<string, long>();
			if (StageCount == null) StageCount = new Dictionary<string, int>();
			if (StageTimes.ContainsKey(stageName)) StageTimes[stageName] += elapsedMs;
			else StageTimes[stageName] = elapsedMs;
			if (StageMin.ContainsKey(stageName)) StageMin[stageName] = Math.Min(StageMin[stageName], elapsedMs);
			else StageMin[stageName] = elapsedMs;
			if (StageMax.ContainsKey(stageName)) StageMax[stageName] = Math.Max(StageMax[stageName], elapsedMs);
			else StageMax[stageName] = elapsedMs;
			if (StageCount.ContainsKey(stageName)) StageCount[stageName]++;
			else StageCount[stageName] = 1;
		}

		public string GetStageReport(string cameraName)
		{
			if (ProcessCount == 0) return $"[{cameraName}] 无数据";
			var sb = new StringBuilder();
			sb.Append($"[{cameraName}] 帧数:{ProcessCount} 总Avg:{AverageTimeMs:F1}ms Min:{MinTimeMs}ms Max:{MaxTimeMs}ms");
			if (StageTimes != null && StageTimes.Count > 0)
			{
				var ordered = StageTimes.OrderByDescending(kv => kv.Value);
				foreach (var kv in ordered)
				{
					string name = kv.Key;
					long total = kv.Value;
					int cnt = StageCount != null && StageCount.ContainsKey(name) ? StageCount[name] : ProcessCount;
					long min = StageMin != null && StageMin.ContainsKey(name) ? StageMin[name] : 0;
					long max = StageMax != null && StageMax.ContainsKey(name) ? StageMax[name] : 0;
					double avg = cnt > 0 ? (double)total / cnt : 0;
					// 每阶段独占一行，缩进对齐便于肉眼扫描（AI子项键自带缩进，层级自然呈现）
					sb.AppendLine();
					sb.Append($"    {name}: Avg={avg:F1} Min={min} Max={max}ms");
				}
			}
			return sb.ToString();
		}
	}

	public class ImageProcessingContext : IDisposable
	{
		public long SequenceId { get; set; }
		public int Offset { get; set; }
		public Bitmap OriginalBitmap { get; set; }
		public CameraSelect Camera { get; set; }
		public DateTime ReceiveTime { get; set; }
		public DateTime StartProcessTime { get; set; }
		public DateTime EndProcessTime { get; set; }
		public Stopwatch ProcessingTimer { get; set; }
		public Dictionary<string, long> StageTimes { get; set; }
		public QueueResultItem Result { get; set; }
		public bool ProcessResult { get; set; }

		public ImageProcessingContext() { StageTimes = new Dictionary<string, long>(); }
		public void Dispose()
		{
			OriginalBitmap?.Dispose();
			OriginalBitmap = null;
		}
	}

	public class OrderedQueueManager<T> : IDisposable where T : class
	{
		// 使用 ConcurrentDictionary 替代 SortedDictionary+lock，消除热路径锁竞争
		private readonly ConcurrentDictionary<long, T> _items = new ConcurrentDictionary<long, T>();
		private readonly int _maxSize;
		private readonly string _queueName;
		private long _expectedSequence = 1;
		private volatile bool _disposed = false;

		public int Count => _items.Count;

		public OrderedQueueManager(int maxSize, string queueName)
		{
			_maxSize = maxSize;
			_queueName = queueName ?? "未命名队列";
		}

		public bool Enqueue(long sequenceId, T item)
		{
			if (_disposed || item == null) return false;
			// ConcurrentDictionary.TryAdd 无锁入队
			if (!_items.TryAdd(sequenceId, item))
			{
				// key已存在（异常情况），替换旧值
				if (_items.TryGetValue(sequenceId, out var old)) TryDisposeItem(old);
				_items[sequenceId] = item;
			}
			// 超量清理：只清理已过期（key < _expectedSequence，消费者已跳过）的项
			// 绝不能清理 >=_expectedSequence 的项：ResultMatcher 可能正持有引用！
			if (_items.Count > _maxSize)
			{
				// 从 _expectedSequence-1 向下找，移除找到的第一个过期项
				for (long k = _expectedSequence - 1; k > 0 && _items.Count > _maxSize; k--)
				{
					if (_items.TryRemove(k, out var removed))
					{
						TryDisposeItem(removed);
						break;
					}
				}
			}
			return true;
		}

		public T DequeueExpected()
		{
			if (_disposed) return null;
			if (_items.TryRemove(_expectedSequence, out T item))
			{
				Interlocked.Increment(ref _expectedSequence);
				return item;
			}
			return null;
		}

		public T PeekExpected()
		{
			if (_disposed) return null;
			return _items.TryGetValue(_expectedSequence, out T item) ? item : null;
		}

		public T DequeueOldest()
		{
			if (_disposed) return null;
			if (_items.IsEmpty) return null;
			long minKey = long.MaxValue;
			foreach (var k in _items.Keys) { if (k < minKey) minKey = k; }
			return _items.TryRemove(minKey, out T item) ? item : null;
		}

		public void Clear()
		{
			foreach (var kv in _items)
			{
				TryDisposeItem(kv.Value);
				_items.TryRemove(kv.Key, out _);
			}
			_expectedSequence = 1;
		}

		public void ResetExpectedSequence(long newSequence)
		{
			Interlocked.Exchange(ref _expectedSequence, newSequence);
		}

		// 适配 FindResultById：返回所有元素（不再保证排序，取消early-exit优化）
		public IEnumerable<T> GetAllItems()
		{
			return _items.Values.ToList();
		}

		public void Dispose()
		{
			if (!_disposed) { _disposed = true; Clear(); }
		}

		private void TryDisposeItem(T item)
		{
			try { if (item is IDisposable disposable) disposable.Dispose(); }
			catch { }
		}
	}

	public class ImageProcessor : IDisposable
	{
		private readonly OrderedQueueManager<ImageProcessingContext> _imageQueue;
		private readonly OrderedQueueManager<QueueResultItem> _resultQueue;
		private readonly Thread _processorThread;
		private readonly AutoResetEvent _processSignal;
		private readonly string _cameraName;
		private readonly Action<ImageProcessingContext> _processAction;
		private PerformanceStats _performanceStats;
		private bool _disposed = false;
		private bool _isProcessing = false;
		// 每个相机独立的GDI+绘制资源
		public readonly Font DrawFontTitle;
		public readonly Font DrawFontText;
		public readonly SolidBrush DrawBrushGreen;
		public readonly SolidBrush DrawBrushRed;
		public int ImageQueueCount => _imageQueue.Count;
		public int ResultQueueCount => _resultQueue.Count;
		public string CameraName => _cameraName;
		public PerformanceStats Performance => _performanceStats;

		public ImageProcessor(string cameraName, Action<ImageProcessingContext> processAction, int maxQueueSize = 100)
		{
			_cameraName = cameraName;
			_processAction = processAction ?? throw new ArgumentNullException(nameof(processAction));
			_imageQueue = new OrderedQueueManager<ImageProcessingContext>(maxQueueSize, $"{cameraName}_ImageQueue");
			_resultQueue = new OrderedQueueManager<QueueResultItem>(maxQueueSize, $"{cameraName}_ResultQueue");
			_processSignal = new AutoResetEvent(false);
			_performanceStats = new PerformanceStats();

			_processorThread = new Thread(ProcessorWorker)
			{
				Name = $"Processor_{cameraName}",
				IsBackground = true,
				Priority = ThreadPriority.AboveNormal
			};
			_processorThread.Start();
			DrawFontTitle = new Font("Microsoft YaHei", 24, FontStyle.Bold);
			DrawFontText = new Font("Microsoft YaHei", 24, FontStyle.Bold);
			DrawBrushGreen = new SolidBrush(Color.LawnGreen);
			DrawBrushRed = new SolidBrush(Color.Red);
		}

		public bool AddImage(long sequenceId, Bitmap bitmap, int offset, CameraSelect camera)
		{
			if (_disposed || bitmap == null) return false;

			var context = new ImageProcessingContext
			{
				SequenceId = sequenceId,
				Offset = offset,
				OriginalBitmap = bitmap,
				Camera = camera,
				ReceiveTime = DateTime.Now,
				ProcessingTimer = Stopwatch.StartNew()
			};

			bool success = _imageQueue.Enqueue(sequenceId, context);
			if (success) _processSignal.Set();
			return success;
		}

		private void ProcessorWorker()
		{
			try
			{
				while (!_disposed)
				{
					_processSignal.WaitOne(100);
					if (_disposed) break;
					// 【P1】排空队列：信号到达后处理所有可用帧，不限于 1 帧
					// AutoResetEvent 可能合并多次 Set()，导致积压帧等待下一个 100ms 超时
					while (!_disposed && ProcessNextImage()) { }
				}
			}
			catch (ThreadAbortException) { }
			catch (Exception ex)
			{
				FastLogger.Instance.Error($"{_cameraName} 处理器异常: {ex.Message}");
			}
		}

		private bool ProcessNextImage()
		{
			if (_isProcessing) return false;
			_isProcessing = true;

			try
			{
				var context = _imageQueue.DequeueExpected();
				if (context == null) return false;

				// 排队等待耗时：从 AddImage 入队到此刻被 Worker 取走的时间差
				if (context.ProcessingTimer != null)
					context.StageTimes["排队等待"] = context.ProcessingTimer.ElapsedMilliseconds;

				context.StartProcessTime = DateTime.Now;

				try
				{
					_processAction(context);
					if (context.Result != null)
						_resultQueue.Enqueue(context.SequenceId, context.Result);
					else
					{
						var queueResult = QueueResultItem.Rent();
						queueResult.SequenceId = context.SequenceId;
						queueResult.Offset = context.Offset;
						queueResult.Result = context.ProcessResult;
						queueResult.Timestamp = DateTime.Now;
						_resultQueue.Enqueue(context.SequenceId, queueResult);
					}
				}
				catch (Exception ex)
				{
					FastLogger.Instance.Error($"{_cameraName} 处理异常: {ex.Message}");
					var errorResult = QueueResultItem.Rent();
					errorResult.SequenceId = context.SequenceId;
					errorResult.Offset = context.Offset;
					errorResult.Result = context.ProcessResult;
					errorResult.Timestamp = DateTime.Now;
					_resultQueue.Enqueue(context.SequenceId, errorResult);
				}
				finally
				{
					context.EndProcessTime = DateTime.Now;
					context.ProcessingTimer?.Stop();

					if (context.ProcessingTimer != null)
					{
						long elapsedMs = context.ProcessingTimer.ElapsedMilliseconds;
						_performanceStats.AddTime(elapsedMs);
						// AddStageTime always needed; StringBuilder only when RunLogEnabled
						foreach (var stage in context.StageTimes)
						{
							_performanceStats.AddStageTime(stage.Key, stage.Value);
						}
						if (MainFrm.RunLogEnabled)
						{
							var sb = new StringBuilder();
							sb.Append($"\r\nCamera: {context.Camera}\r\nID:{context.SequenceId - context.Offset}\r\n总耗时: {elapsedMs}ms\r\n");
							foreach (var stage in context.StageTimes)
								sb.Append($"{stage.Key}: {stage.Value}\r\n");
							FastLogger.Instance.Info(sb.ToString());
						}
						// FastLogger 耗时日志（每50帧汇总一次，减少输出量）
						if (_performanceStats.ProcessCount % 50 == 0)
						{
							try
							{
								FastLogger.Instance.Debug(_cameraName + " 耗时汇总: Avg=" + _performanceStats.AverageTimeMs
									+ "ms Min=" + _performanceStats.MinTimeMs + "ms Max=" + _performanceStats.MaxTimeMs
									+ "ms 帧数=" + _performanceStats.ProcessCount);
								foreach (var stage in context.StageTimes)
								{
									try { FastLogger.Instance.Debug(_cameraName + " " + stage.Key + ": " + stage.Value + "ms"); }
									catch { }
								}
							}
							catch { }
						}

						// 每50帧输出性能汇总（仅调试模式）
						if (MainFrm.RunLogEnabled && _performanceStats.ProcessCount > 0 && _performanceStats.ProcessCount % 50 == 0)
						{
							try { if (FastLogger.IsInitialized) FastLogger.Instance.Info($"[性能] {_cameraName} 累计{_performanceStats.ProcessCount}帧 Avg={_performanceStats.AverageTimeMs:F0}ms Min={_performanceStats.MinTimeMs}ms Max={_performanceStats.MaxTimeMs}ms"); } catch { }
						}
					}

					context.OriginalBitmap?.Dispose();
				}
			}
			finally
			{
				_isProcessing = false;
			}
			return true; // processed one frame
		}

		public QueueResultItem GetNextResult() => _resultQueue.DequeueExpected();
		public QueueResultItem PeekNextResult() => _resultQueue.PeekExpected();

		public List<QueueResultItem> GetAllResults()
		{
			var results = new List<QueueResultItem>();
			while (true)
			{
				var result = _resultQueue.DequeueOldest();
				if (result == null) break;
				results.Add(result);
			}
			return results;
		}

		/// <summary>
		/// 查找匹配的结果（只读方式，不移除任何结果）
		/// </summary>
		public QueueResultItem FindResultById(long targetSequenceId)
		{
			// GetAllItems 不再保证排序（ConcurrentDictionary），需全量扫描
			var allItems = _resultQueue.GetAllItems();
			foreach (var result in allItems)
			{
				if (result.SequenceId - result.Offset == targetSequenceId) return result;
			}
			return null;
		}

		public QueueResultItem GetAndRemoveResultById(long targetSequenceId)
		{
			var result = FindResultById(targetSequenceId);
			if (result != null) GetNextResult();
			return result;
		}

		public void ResetPerformanceStats() => _performanceStats.Reset();

		public void Dispose()
		{
			if (!_disposed)
			{
				_disposed = true;
				_processSignal?.Set();
				_processSignal?.Dispose();

				if (_processorThread != null && _processorThread.IsAlive)
				{
					try { _processorThread.Join(2000); }
					catch { }
				}

				DrawFontTitle?.Dispose();
				DrawFontText?.Dispose();
				DrawBrushGreen?.Dispose();
				DrawBrushRed?.Dispose();
				_imageQueue?.Dispose();
				_resultQueue?.Dispose();
			}
		}
	}


	public class ResultMatcher : IDisposable
	{
		private readonly ImageProcessor[] _processors;
		private long _debugFrameCount = 0;
		private readonly int _activeProcessorCount;
		private readonly int _firstActiveIndex;
		private readonly Thread _matchingThread;
		private readonly AutoResetEvent _matchSignal;
		private readonly Action<QueueResultItem[]> _matchCallback;
		private bool _disposed = false;
		private long _lastEmittedId = -1;

		public ResultMatcher(ImageProcessor[] processors, Action<QueueResultItem[]> matchCallback)
		{
			_processors = processors ?? throw new ArgumentNullException(nameof(processors));
			_matchCallback = matchCallback ?? throw new ArgumentNullException(nameof(matchCallback));
			_matchSignal = new AutoResetEvent(false);

			_activeProcessorCount = 0;
			_firstActiveIndex = -1;
			for (int i = 0; i < _processors.Length; i++)
			{
				if (_processors[i] != null)
				{
					_activeProcessorCount++;
					if (_firstActiveIndex < 0) _firstActiveIndex = i;
				}
			}

			if (_activeProcessorCount == 0)
			{
				FastLogger.Instance.Debug("[ResultMatcher] All processors disabled, matcher will not trigger");
				_disposed = true;
				return;
			}

			FastLogger.Instance.Debug(string.Format("[ResultMatcher] {0}/{1} active, anchorIdx={2}", _activeProcessorCount, _processors.Length, _firstActiveIndex));
			_matchingThread = new Thread(MatchingWorker)
			{
				Name = "ResultMatcher",
				IsBackground = true,
				Priority = ThreadPriority.Highest
			};
			_matchingThread.Start();
		}

		private static QueueResultItem CreateVirtualOkResult(long sequenceId)
		{
			var item = QueueResultItem.Rent();
			item.SequenceId = sequenceId;
			item.Offset = 0;
			item.Result = true;
			item.Timestamp = DateTime.Now;
			return item;
		}

		private static QueueResultItem CreateVirtualNgResult(long sequenceId)
		{
			var item = QueueResultItem.Rent();
			item.SequenceId = sequenceId;
			item.Offset = 0;
			item.Result = false;
			item.Timestamp = DateTime.Now;
			return item;
		}

		private void EmitGapFiller(long unifiedId, string reason)
		{
			var results = new QueueResultItem[_processors.Length];
			for (int i = 0; i < _processors.Length; i++)
				results[i] = CreateVirtualNgResult(unifiedId);
			try
			{
				FastLogger.Instance.Warn(string.Format("[GapFill-NG] UnifiedId={0} Reason={1} - 5-cam all NG placeholder. Check for frame drops.", unifiedId, reason));
			}
			catch { }
			_matchCallback?.Invoke(results);
			_lastEmittedId = unifiedId;
		}

		private void MatchingWorker()
		{
			if (_activeProcessorCount == 0) return;
			try
			{
				while (!_disposed)
				{
					bool allHaveData = true;
					for (int i = 0; i < _processors.Length; i++)
					{
						if (_processors[i] == null) continue;
						if (_processors[i].PeekNextResult() == null) { allHaveData = false; break; }
					}
					if (allHaveData) PerformMatching();
					else _matchSignal.WaitOne(1);
				}
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error(string.Format("ResultMatcher worker crashed: {0}", ex.Message));
			}
		}

		private void PerformMatching()
		{
			if (_firstActiveIndex < 0) return;
			try
			{
				var anchorResult = _processors[_firstActiveIndex].PeekNextResult();
				if (anchorResult == null) return;

				long targetSequenceId = anchorResult.SequenceId - anchorResult.Offset;
				string anchorCamName = "Cam" + (_firstActiveIndex + 1);

				if (_lastEmittedId >= 0 && targetSequenceId > _lastEmittedId + 1)
				{
					long gapCount = targetSequenceId - _lastEmittedId - 1;
					try { FastLogger.Instance.Warn(string.Format("[GapFill-NG] Anchor gap: lastEmitted={0} target={1} gap={2}. Filling NG.", _lastEmittedId, targetSequenceId, gapCount)); } catch { }
					for (long gapId = _lastEmittedId + 1; gapId < targetSequenceId; gapId++)
						EmitGapFiller(gapId, "AnchorGap(lastEmitted=" + _lastEmittedId + "->target=" + targetSequenceId + ")");
				}

				if (MainFrm.RunLogEnabled) try { if (FastLogger.IsInitialized) FastLogger.Instance.Info(string.Format("[ResultMatch] anchor={0} RawSeq={1} Offset={2} Target={3}", anchorCamName, anchorResult.SequenceId, anchorResult.Offset, targetSequenceId)); } catch { }

				var matchedResults = new QueueResultItem[_processors.Length];
				for (int i = 0; i < _processors.Length; i++)
					matchedResults[i] = _processors[i] == null ? CreateVirtualOkResult(targetSequenceId) : null;
				matchedResults[_firstActiveIndex] = anchorResult;

				bool allMatch = true;
				for (int i = 0; i < _processors.Length; i++)
				{
					if (i == _firstActiveIndex || _processors[i] == null) continue;
					var mr = _processors[i].FindResultById(targetSequenceId);
					if (mr != null) matchedResults[i] = mr;
					else { allMatch = false; break; }
				}

				if (allMatch)
				{
					for (int i = 0; i < _processors.Length; i++)
					{
						if (_processors[i] == null) continue;
						QueueResultItem cur;
						while ((cur = _processors[i].PeekNextResult()) != null)
						{
							long cid = cur.SequenceId - cur.Offset;
							if (cid == targetSequenceId) { _processors[i].GetNextResult(); break; }
							else if (cid < targetSequenceId) _processors[i].GetNextResult();
							else break;
						}
					}
					for (int i = 0; i < matchedResults.Length; i++)
						if (matchedResults[i] == null) matchedResults[i] = CreateVirtualOkResult(targetSequenceId);

					if (MainFrm.RunLogEnabled)
					{
						string s = "";
						for (int ri = 0; ri < matchedResults.Length; ri++)
						{
							long rid = matchedResults[ri].SequenceId - matchedResults[ri].Offset;
							s += "Cam" + (ri + 1) + "=" + rid + "(" + (matchedResults[ri].Result ? "OK" : "NG") + ") ";
						}
						try { if (FastLogger.IsInitialized) FastLogger.Instance.Info("[ResultMatch] ID:" + targetSequenceId + " matched -> " + s); } catch { }
					}

					_debugFrameCount++;
					if (_debugFrameCount % 200 == 0)
						try { if (FastLogger.IsInitialized) FastLogger.Instance.Debug("Periodic: " + _debugFrameCount + " frames, anchor=" + anchorCamName); } catch { }

					_matchCallback?.Invoke(matchedResults);
					_lastEmittedId = targetSequenceId;
					_matchSignal.Set();
				}
				else
				{
					bool anyPast = false;
					for (int i = 0; i < _processors.Length; i++)
					{
						if (_processors[i] == null || i == _firstActiveIndex) continue;
						var pk = _processors[i].PeekNextResult();
						if (pk != null && (pk.SequenceId - pk.Offset) > targetSequenceId) { anyPast = true; break; }
					}

					if (anyPast)
					{
						string missingCams = "";
						for (int i = 0; i < _processors.Length; i++)
						{
							if (_processors[i] == null || i == _firstActiveIndex) continue;
							if (matchedResults[i] == null)
							{
								matchedResults[i] = CreateVirtualNgResult(targetSequenceId);
								missingCams += "Cam" + (i + 1) + " ";
							}
						}
						for (int i = 0; i < matchedResults.Length; i++)
							if (matchedResults[i] == null) matchedResults[i] = CreateVirtualNgResult(targetSequenceId);

						try { FastLogger.Instance.Warn(string.Format("[GapFill-NG] UnifiedId={0} Reason=MatchTimeout missingCams=[{1}] - Filled with NG.", targetSequenceId, missingCams.Trim())); } catch { }

						_processors[_firstActiveIndex].GetNextResult();
						for (int i = 0; i < _processors.Length; i++)
						{
							if (_processors[i] == null || i == _firstActiveIndex) continue;
							QueueResultItem cur;
							while ((cur = _processors[i].PeekNextResult()) != null)
							{
								long cid = cur.SequenceId - cur.Offset;
								if (cid < targetSequenceId) _processors[i].GetNextResult();
								else break;
							}
						}

						_matchCallback?.Invoke(matchedResults);
						_lastEmittedId = targetSequenceId;
						_matchSignal.Set();
					}
				}
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Error(string.Format("PerformMatching error: {0}", ex.Message));
			}
		}

		public void SignalNewResult() => _matchSignal?.Set();

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			_matchSignal?.Set();
			_matchSignal?.Dispose();

			if (_matchingThread != null && _matchingThread.IsAlive)
			{
				try { _matchingThread.Join(1000); }
				catch { }
			}
		}
	}

	#region 高性能图像保存方案
	public class HighSpeedImageSaver : IDisposable
	{
		private readonly BlockingCollection<SaveTask> _saveQueue;
		private readonly Thread[] _workerThreads;
		private bool _disposed = false;
		private readonly string _saverName;

		private class SaveTask
		{
			public string FilePath { get; set; }
			public byte[] ImageData { get; set; }
			public bool IsJpg { get; set; }
			public int Quality { get; set; }
			public DateTime EnqueueTime { get; set; }
		}

		public int QueueCount => _saveQueue.Count;

		public HighSpeedImageSaver(string name = "高速保存器", int threadCount = 4, int maxQueueSize = 200)
		{
			_saverName = name;
			_saveQueue = new BlockingCollection<SaveTask>(new ConcurrentQueue<SaveTask>(), maxQueueSize);
			_workerThreads = new Thread[Math.Max(1, threadCount)];

			for (int i = 0; i < _workerThreads.Length; i++)
			{
				_workerThreads[i] = new Thread(WorkerMethod)
				{
					Name = $"{name}_Worker{i + 1}",
					IsBackground = true,
					Priority = ThreadPriority.BelowNormal
				};
				_workerThreads[i].Start();
			}
		}

		public bool AddSaveTask(string filePath, byte[] imageData, bool isJpg = true, int quality = 85)
		{
			if (_disposed || imageData == null || imageData.Length == 0) return false;

			if (_saveQueue.Count >= _saveQueue.BoundedCapacity)
			{
				if (_saveQueue.TryTake(out var discardedTask))
					FastLogger.Instance.Warn($"{_saverName} 队列已满，丢弃最旧任务: {discardedTask.FilePath}");
				else return false;
			}

			var task = new SaveTask
			{
				FilePath = filePath,
				ImageData = imageData,
				IsJpg = isJpg,
				Quality = quality,
				EnqueueTime = DateTime.Now
			};

			try { return _saveQueue.TryAdd(task, 10); }
			catch { return false; }
		}

		private void WorkerMethod()
		{
			try
			{
				foreach (var task in _saveQueue.GetConsumingEnumerable())
				{
					if (_disposed) break;
					try { SaveImageDirect(task); }
					catch (Exception ex) { FastLogger.Instance.Error($"{_saverName} 保存失败: {ex.Message}"); }
				}
			}
			catch { }
		}

		private void SaveImageDirect(SaveTask task)
		{
			try
			{
				string directory = Path.GetDirectoryName(task.FilePath);
				if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
				File.WriteAllBytes(task.FilePath, task.ImageData);

				var delay = (DateTime.Now - task.EnqueueTime).TotalMilliseconds;
				if (delay > 100) try { if (FastLogger.IsInitialized) FastLogger.Instance.Info($"{_saverName} 保存延迟较高: {delay:F1}ms"); } catch { }
			}
			catch (Exception ex) { FastLogger.Instance.Error($"{_saverName} 文件写入失败: {ex.Message}"); }
		}

		public void Dispose()
		{
			if (!_disposed)
			{
				_disposed = true;
				_saveQueue.CompleteAdding();
				foreach (var thread in _workerThreads)
				{
					if (thread != null && thread.IsAlive) thread.Join(500);
				}
				_saveQueue.Dispose();
			}
		}
	}

	public static class BitmapFastConverter
	{
		// 缓存默认质量的编码参数，避免热路径每次 new int[2]
		private static readonly int[] s_jpegEncodeParams = new int[] { (int)ImwriteFlags.JpegQuality, 85 };

		/// <summary>
		/// 【修复编译版】直接使用 OpenCV 原生 C++ 算头进行 Jpeg 压缩
		/// 严格适配 out 关键字，零托管内存开辟，防止高速存图时由于 MemoryStream 导致 GC 卡顿
		/// </summary>
		public static byte[] ToJpegBytesViaOpenCv(Mat mat, int quality = 85)
		{
			if (mat == null || mat.Empty()) return null;
			try
			{
				// 缓存编码参数数组，避免每次编码时 new int[2] 产生堆分配
				int[] encodeParams = quality == 85 ? s_jpegEncodeParams : new int[] { (int)ImwriteFlags.JpegQuality, quality };

				// 【修复】使用 out 关键字传递参数，承接编码后的字节数组
				byte[] buf;
				bool success = Cv2.ImEncode(".jpg", mat, out buf, encodeParams);

				return success ? buf : null;
			}
			catch
			{
				return null;
			}
		}


		public static byte[] ToJpegBytesViaOpenCv(Bitmap bitmap, int quality = 85)
		{
			if (bitmap == null) return null;
			try
			{
				using (var mat = BitmapConverter.ToMat(bitmap))
				{
					return ToJpegBytesViaOpenCv(mat, quality);
				}
			}
			catch
			{
				return null;
			}
		}

		// 保留原有的 Bmp 转换以向下兼容
		public static byte[] ToBmpBytesFast(this Bitmap bitmap)
		{
			if (bitmap == null) return null;
			try
			{
				using (var ms = new MemoryStream())
				{
					bitmap.Save(ms, ImageFormat.Bmp);
					return ms.ToArray();
				}
			}
			catch { return null; }
		}



		public static byte[] ToBmpBytesViaOpenCv_MatDirect(Mat mat)
		{
			if (mat == null || mat.Empty()) return null;
			try
			{
				byte[] buf;
				bool success = Cv2.ImEncode(".bmp", mat, out buf);
				return success ? buf : null;
			}
			catch { return null; }
		}

		public static byte[] ToBmpBytesViaOpenCv(Bitmap bitmap)
		{
			if (bitmap == null) return null;
			try
			{
				using (var mat = BitmapConverter.ToMat(bitmap))
					return ToBmpBytesViaOpenCv_MatDirect(mat);
			}
			catch { return null; }
		}

		public static byte[] ToJpegBytesFast(this Bitmap bitmap, int quality = 85)
		{
			if (bitmap == null) return null;
			try
			{
				using (var ms = new MemoryStream(bitmap.Width * bitmap.Height * 3))
				{
					var jpegEncoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(codec => codec.MimeType == "image/jpeg");
					if (jpegEncoder != null)
					{
						var encoderParams = new EncoderParameters(1);
						encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
						bitmap.Save(ms, jpegEncoder, encoderParams);
					}
					else bitmap.Save(ms, ImageFormat.Jpeg);
					return ms.ToArray();
				}
			}
			catch { return null; }
		}


		public static Bitmap CreateCompatibleCopy(this Bitmap source)
		{
			if (source == null) return null;
			if (source.PixelFormat == PixelFormat.Format32bppArgb || source.PixelFormat == PixelFormat.Format32bppPArgb)
			{
				var compatible = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
				using (var g = Graphics.FromImage(compatible))
				{
					g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
					g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
					g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
					g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
					g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;
					g.DrawImage(source, 0, 0, source.Width, source.Height);
				}
				return compatible;
			}
			return new Bitmap(source);
		}
	}
	#endregion

	#region 图像池
	public class ImageBufferPool : IDisposable
	{
		private class PoolItem<T> where T : IDisposable
		{
			public T Resource { get; set; }
			public DateTime LastUsed { get; set; }
			public long Size { get; set; }
		}

		private readonly ConcurrentBag<PoolItem<Bitmap>> _bitmapPool = new ConcurrentBag<PoolItem<Bitmap>>();
		private readonly ConcurrentBag<PoolItem<Mat>> _matPool = new ConcurrentBag<PoolItem<Mat>>();

		private readonly int _defaultWidth, _defaultHeight;
		private readonly PixelFormat _pixelFormat;
		private readonly int _initialCapacity, _maxCapacity;
		private readonly long _maxMemoryBytes;

		private long _totalAllocatedMemory = 0;
		private bool _disposed = false;
		private readonly object _lock = new object();

		private long _rentCount = 0, _returnCount = 0, _newAllocationCount = 0, _poolHitCount = 0;

		public string PoolName { get; set; } = "ImageBufferPool";
		public int AvailableBitmapCount => _bitmapPool.Count;
		public int AvailableMatCount => _matPool.Count;
		public long TotalAllocatedMemory => _totalAllocatedMemory;
		public double PoolHitRate => _rentCount > 0 ? (double)_poolHitCount / _rentCount * 100 : 0;

		public ImageBufferPool(int width, int height, PixelFormat pixelFormat, int initialCapacity = 5, int maxCapacity = 20, long maxMemoryBytes = 500 * 1024 * 1024)
		{
			_defaultWidth = width;
			_defaultHeight = height;
			_pixelFormat = pixelFormat;
			_initialCapacity = initialCapacity;
			_maxCapacity = maxCapacity;
			_maxMemoryBytes = maxMemoryBytes;

			InitializePool();
			ThreadPool.QueueUserWorkItem(MonitorPool);
		}

		private void InitializePool()
		{
			for (int i = 0; i < _initialCapacity; i++)
			{
				try
				{
					var bitmap = new Bitmap(_defaultWidth, _defaultHeight, _pixelFormat);
					var mat = new Mat(_defaultHeight, _defaultWidth, _pixelFormat == PixelFormat.Format8bppIndexed ? MatType.CV_8UC1 : MatType.CV_8UC3);

					_bitmapPool.Add(new PoolItem<Bitmap> { Resource = bitmap, LastUsed = DateTime.Now, Size = EstimateBitmapSize(bitmap) });
					_matPool.Add(new PoolItem<Mat> { Resource = mat, LastUsed = DateTime.Now, Size = EstimateMatSize(mat) });

					Interlocked.Add(ref _totalAllocatedMemory, EstimateBitmapSize(bitmap) + EstimateMatSize(mat));
				}
				catch (Exception ex) { FastLogger.Instance.Error($"{PoolName} 初始化失败: {ex.Message}"); }
			}
			FastLogger.Instance.Info($"{PoolName} 初始化完成: {_initialCapacity}个Bitmap和Mat已预分配");
		}

		private long EstimateBitmapSize(Bitmap bitmap)
		{
			try
			{
				var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
				long size = Math.Abs(data.Stride) * bitmap.Height;
				bitmap.UnlockBits(data);
				return size;
			}
			catch
			{
				int bpp = System.Drawing.Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
				return bitmap.Width * bitmap.Height * bpp;
			}
		}

		private long EstimateMatSize(Mat mat)
		{
			if (mat == null || mat.Empty()) return 0;
			return mat.Total() * mat.ElemSize();
		}

		public Bitmap RentBitmap()
		{
			Interlocked.Increment(ref _rentCount);
			if (_bitmapPool.TryTake(out var poolItem))
			{
				Interlocked.Increment(ref _poolHitCount);
				poolItem.LastUsed = DateTime.Now;
				return poolItem.Resource;
			}

			Interlocked.Increment(ref _newAllocationCount);
			var newBitmap = new Bitmap(_defaultWidth, _defaultHeight, _pixelFormat);
			Interlocked.Add(ref _totalAllocatedMemory, EstimateBitmapSize(newBitmap));
			return newBitmap;
		}

		public Mat RentMat()
		{
			Interlocked.Increment(ref _rentCount);
			if (_matPool.TryTake(out var poolItem))
			{
				Interlocked.Increment(ref _poolHitCount);
				poolItem.LastUsed = DateTime.Now;
				return poolItem.Resource;
			}

			Interlocked.Increment(ref _newAllocationCount);
			var newMat = new Mat(_defaultHeight, _defaultWidth, _pixelFormat == PixelFormat.Format8bppIndexed ? MatType.CV_8UC1 : MatType.CV_8UC3);
			Interlocked.Add(ref _totalAllocatedMemory, EstimateMatSize(newMat));
			return newMat;
		}

		public void ReturnBitmap(Bitmap bitmap)
		{
			if (bitmap == null || _disposed) return;
			Interlocked.Increment(ref _returnCount);

			if (bitmap.Width == _defaultWidth && bitmap.Height == _defaultHeight && bitmap.PixelFormat == _pixelFormat && _bitmapPool.Count < _maxCapacity)
			{
				_bitmapPool.Add(new PoolItem<Bitmap> { Resource = bitmap, LastUsed = DateTime.Now, Size = EstimateBitmapSize(bitmap) });
			}
			else
			{
				// 【P0修复】先算尺寸再 Dispose，防止对已释放对象调用 EstimateBitmapSize 抛异常
				long size = EstimateBitmapSize(bitmap);
				bitmap.Dispose();
				Interlocked.Add(ref _totalAllocatedMemory, -size);
			}
		}

		public void ReturnMat(Mat mat)
		{
			if (mat == null || _disposed) return;
			Interlocked.Increment(ref _returnCount);

			if (mat.Width == _defaultWidth && mat.Height == _defaultHeight && _matPool.Count < _maxCapacity)
			{
				// 【关键优化】移除极其吃CPU和非托管总线带宽的 mat.SetTo(Scalar.All(0)) 填充清零操作。
				// 下一帧图像进行 CopyTo 或 转灰度图时会自动覆写内存，无需提前清零。
				_matPool.Add(new PoolItem<Mat> { Resource = mat, LastUsed = DateTime.Now, Size = EstimateMatSize(mat) });
			}
			else
			{
				mat.Dispose();
				Interlocked.Add(ref _totalAllocatedMemory, -EstimateMatSize(mat));
			}
		}

		private void MonitorPool(object state)
		{
			while (!_disposed)
			{
				Thread.Sleep(30000);
				try
				{
					DateTime cutoff = DateTime.Now.AddMinutes(-5);
					CleanOldItems(_bitmapPool, cutoff, item => item.Resource.Dispose());
					CleanOldItems(_matPool, cutoff, item => item.Resource.Dispose());
				}
				catch (Exception ex) { FastLogger.Instance.Error($"{PoolName} 监控异常: {ex.Message}"); }
			}
		}

		private void CleanOldItems<T>(ConcurrentBag<PoolItem<T>> pool, DateTime cutoff, Action<PoolItem<T>> disposeAction) where T : IDisposable
		{
			var tempList = new List<PoolItem<T>>();
			while (pool.TryTake(out var item)) tempList.Add(item);

			foreach (var item in tempList)
			{
				if (item.LastUsed < cutoff && pool.Count > _initialCapacity)
				{
					disposeAction?.Invoke(item);
					Interlocked.Add(ref _totalAllocatedMemory, -item.Size);
				}
				else pool.Add(item);
			}
		}

		public void Clear()
		{
			lock (_lock)
			{
				while (_bitmapPool.TryTake(out var bitmapItem)) bitmapItem.Resource?.Dispose();
				while (_matPool.TryTake(out var matItem)) matItem.Resource?.Dispose();
				_totalAllocatedMemory = 0;
				_rentCount = 0; _returnCount = 0; _newAllocationCount = 0; _poolHitCount = 0;
			}
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			try { Clear(); } catch { }
			GC.SuppressFinalize(this);
			FastLogger.Instance.Info($"{PoolName} 已释放");
		}
	}

	public static class BitmapExtensions
	{
		[DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
		private static extern void CopyMemory(IntPtr dest, IntPtr src, int length);

		public static Bitmap FastClone(this Bitmap source)
		{
			if (source == null) return null;
			try
			{
				if (source.PixelFormat == PixelFormat.Format24bppRgb || source.PixelFormat == PixelFormat.Format32bppArgb || source.PixelFormat == PixelFormat.Format8bppIndexed)
					return (Bitmap)source.Clone();
				else return ConvertToCompatibleFormat(source);
			}
			catch { return new Bitmap(source); }
		}

		private static Bitmap ConvertToCompatibleFormat(Bitmap source)
		{
			Bitmap compatible = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
			using (Graphics g = Graphics.FromImage(compatible))
			{
				g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
				g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
				g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
				g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;
				g.DrawImage(source, 0, 0, source.Width, source.Height);
			}
			return compatible;
		}

		public static void FastCopyTo(this Bitmap source, Bitmap target)
		{
			if (source == null || target == null) return;
			if (source.Width != target.Width || source.Height != target.Height) return;

			try
			{
				BitmapData sourceData = null, targetData = null;
				try
				{
					sourceData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, source.PixelFormat);
					targetData = target.LockBits(new Rectangle(0, 0, target.Width, target.Height), ImageLockMode.WriteOnly, target.PixelFormat);

					if (sourceData.PixelFormat == targetData.PixelFormat)
					{
						int srcStride = Math.Abs(sourceData.Stride);
						int dstStride = Math.Abs(targetData.Stride);
						int minStride = Math.Min(srcStride, dstStride);

						for (int y = 0; y < source.Height; y++)
						{
							IntPtr srcPtr = new IntPtr(sourceData.Scan0.ToInt64() + y * srcStride);
							IntPtr dstPtr = new IntPtr(targetData.Scan0.ToInt64() + y * dstStride);
							CopyMemory(dstPtr, srcPtr, minStride);
						}
					}
					else
					{
						using (Graphics g = Graphics.FromImage(target))
						{
							g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
							g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
							g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
							g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
							g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;
							g.DrawImage(source, 0, 0, source.Width, source.Height);
						}
					}
				}
				finally
				{
					if (sourceData != null) source.UnlockBits(sourceData);
					if (targetData != null) target.UnlockBits(targetData);
				}
			}
			catch (Exception ex) { FastLogger.Instance.Error($"FastCopyTo失败: {ex.Message}"); }
		}
	}
	#endregion

	#region TextBlock排序
	public static class TextBlockSorter
	{
		public enum SortDirection { LeftToRight, RightToLeft }

		public static double GetCenterX(this TextBlock block)
		{
			if (block.Polygon == null || !block.Polygon.Any()) return 0;
			double sum = 0; int count = 0;
			foreach (var point in block.Polygon) { sum += point.X; count++; }
			return count > 0 ? sum / count : 0;
		}

		public static List<TextBlock> SortByCenterX(List<TextBlock> blocks, SortDirection direction = SortDirection.LeftToRight)
		{
			if (blocks == null) throw new ArgumentNullException(nameof(blocks));
			if (blocks.Count <= 1) return new List<TextBlock>(blocks);

			var withCenterX = blocks.Select((block, index) => new { Block = block, CenterX = block.GetCenterX(), OriginalIndex = index }).ToList();

			if (direction == SortDirection.LeftToRight)
				return withCenterX.OrderBy(x => x.CenterX).ThenBy(x => x.OriginalIndex).Select(x => x.Block).ToList();
			else
				return withCenterX.OrderByDescending(x => x.CenterX).ThenBy(x => x.OriginalIndex).Select(x => x.Block).ToList();
		}

		public static List<TextBlock> ParallelSortByCenterX(List<TextBlock> blocks, SortDirection direction = SortDirection.LeftToRight, int parallelThreshold = 5000)
		{
			if (blocks == null) throw new ArgumentNullException(nameof(blocks));
			if (blocks.Count <= 1) return new List<TextBlock>(blocks);
			if (blocks.Count < parallelThreshold) return SortByCenterX(blocks, direction);

			var centerXs = new double[blocks.Count];
			Parallel.For(0, blocks.Count, i => { centerXs[i] = blocks[i].GetCenterX(); });

			var indices = Enumerable.Range(0, blocks.Count).ToArray();
			if (direction == SortDirection.LeftToRight)
				Array.Sort(indices, (a, b) => { int cmp = centerXs[a].CompareTo(centerXs[b]); return cmp != 0 ? cmp : a.CompareTo(b); });
			else
				Array.Sort(indices, (a, b) => { int cmp = centerXs[b].CompareTo(centerXs[a]); return cmp != 0 ? cmp : a.CompareTo(b); });

			var result = new TextBlock[blocks.Count];
			for (int i = 0; i < indices.Length; i++) result[i] = blocks[indices[i]];
			return result.ToList();
		}
	}
	#endregion
}
