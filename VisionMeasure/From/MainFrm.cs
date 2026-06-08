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
using XL.Tool;
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
		// 线程安全关闭标志
		private volatile bool _isClosing = false;
		private readonly ManualResetEventSlim _closeWaitHandle = new ManualResetEventSlim(false);
		private readonly object _closeLock = new object();

		public Vision vision = new Vision();
		public HCModbusClass modbusClass = new HCModbusClass();
		XLToolClass toolClass = new XLToolClass();
		XLUsbDogClass UsbDogClass = new XLUsbDogClass();

		private AsyncDatabaseRecorder _dbRecorder;

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

		long camera1Count = 0;
		long camera2Count = 0;
		long camera3Count = 0;
		long camera4Count = 0;
		long camera5Count = 0;

		#region 构造函数和初始化
		public MainFrm()
		{
			InitializeComponent();
			// 【新增优化】开启平滑低延迟垃圾回收模式，防止高并发高密集型计算时GC引起程序突发卡顿
			System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Batch;
			// 【新增优化】提升当前进程在操作系统中的优先级，确保多线程调度更稳定
			Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
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

			// 绑定导出按钮事件
			exportBtn.Click += ExportBtn_Click;

			// 加载保存的SKU
			InitializeSavedSku();
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
					_savedSku = savedSku;
					_skuModified = false;
					if (SKU_Txt != null)
					{
						SKU_Txt.Text = savedSku;
						SKU_Txt.Style = UIStyle.Green;
					}
					toolClass.SaveLog($"已加载保存的SKU: {savedSku}");
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
				toolClass.SaveLog($"初始化SKU异常: {ex.Message}");
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

				_bufferPool1 = new ImageBufferPool(width12, height12, PixelFormat.Format8bppIndexed, 3, 10, 50 * 1024 * 1024) { PoolName = "Camera1_Pool" };
				_bufferPool2 = new ImageBufferPool(width12, height12, PixelFormat.Format8bppIndexed, 3, 10, 50 * 1024 * 1024) { PoolName = "Camera2_Pool" };
				_bufferPool3 = new ImageBufferPool(width3, height3, PixelFormat.Format8bppIndexed, 3, 10, 50 * 1024 * 1024) { PoolName = "Camera3_Pool" };
				_bufferPool4 = new ImageBufferPool(width45, height45, PixelFormat.Format8bppIndexed, 3, 10, 50 * 1024 * 1024) { PoolName = "Camera4_Pool" };
				_bufferPool5 = new ImageBufferPool(width45, height45, PixelFormat.Format8bppIndexed, 3, 10, 50 * 1024 * 1024) { PoolName = "Camera5_Pool" };

				toolClass.SaveLog("内存池初始化完成");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"初始化内存池失败: {ex.Message}");
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
				toolClass.SaveLog("异步数据库记录器初始化完成");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"初始化数据库记录器失败: {ex.Message}");
			}
		}

		private string GetCurrentSkuValue()
		{
			// 从SKU_Txt控件获取SKU值
			try
			{
				string skuText = "";
				if (this.InvokeRequired)
				{
					skuText = (string)this.Invoke(new Func<string>(() => SKU_Txt?.Text ?? ""));
				}
				else
				{
					skuText = SKU_Txt?.Text ?? "";
				}

				// 如果文本框为空但有保存的SKU，使用保存的SKU
				if (string.IsNullOrEmpty(skuText) && !string.IsNullOrEmpty(_savedSku))
				{
					return _savedSku;
				}
				return skuText;
			}
			catch
			{
				return "";
			}
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
					string currentSku = GetCurrentSkuValue();

					if (currentSku.Length != 8)
					{
						SetSkuTextBoxBorderColor(UIStyle.Red);
						MessageBox.Show($"SKU正确长度为8位 目前检测到{currentSku.Length}位！请检查！！！");
						return;
					}


					// 检查SKU是否发生变化
					if (_savedSku != currentSku)
					{
						// SKU切换：先自动保存上一个SKU的班次报表
						if (!string.IsNullOrEmpty(_savedSku) && _dbRecorder != null)
						{
							_dbRecorder.ExportFullShiftReport(_currentShiftDate, _currentShift);
							toolClass.SaveLog($"SKU切换: {_savedSku} -> {currentSku}，已自动保存{_currentShift}班次报表");
						}

						// SKU发生变化，清空统计数据
						ClearStatisticsDisplay();
						_savedSku = currentSku;
						_skuModified = false;

						// 保存到配置文件
						_Config.LastSku = currentSku;

						// 设置边框为绿色
						SetSkuTextBoxBorderColor(UIStyle.Green);

						toolClass.SaveLog($"SKU已更新: {currentSku}，统计数据已清空");
					}
				}
				catch (Exception ex)
				{
					toolClass.SaveLog($"SKU保存异常: {ex.Message}");
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
				string currentSku = GetCurrentSkuValue();

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
				_highSpeedSaver1 = new HighSpeedImageSaver("Camera1", 1, 500);
				_highSpeedSaver2 = new HighSpeedImageSaver("Camera2", 1, 500);
				_highSpeedSaver3 = new HighSpeedImageSaver("Camera3", 1, 500);
				_highSpeedSaver4 = new HighSpeedImageSaver("Camera4", 1, 500);
				_highSpeedSaver5 = new HighSpeedImageSaver("Camera5", 1, 500);

				_saveTimer = Stopwatch.StartNew();
				_totalSaveCount = 0;
				_totalSaveTimeMs = 0;

				toolClass.SaveLog("高性能图像保存器初始化完成");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"初始化高性能保存器失败: {ex.Message}");
			}
		}

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
				if (DateTime.Now.Minute % 5 == 0 && DateTime.Now.Second < 10)
				{
					ResetPerformanceStats();
				}
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"性能监控异常: {ex.Message}");
			}
		}

		private void StartMethod(CameraSelect camera) { }
		private void EndMethod(CameraSelect camera) { }

		private void InitializeImageProcessors()
		{
			try
			{
				_processor1 = new ImageProcessor("Camera1", ProcessCamera1Image, MAX_QUEUE_SIZE);
				_processor2 = new ImageProcessor("Camera2", ProcessCamera2Image, MAX_QUEUE_SIZE);
				_processor3 = new ImageProcessor("Camera3", ProcessCamera3Image, MAX_QUEUE_SIZE);
				_processor4 = new ImageProcessor("Camera4", ProcessCamera4Image, MAX_QUEUE_SIZE);
				_processor5 = new ImageProcessor("Camera5", ProcessCamera5Image, MAX_QUEUE_SIZE);

				var processors = new[] { _processor1, _processor2, _processor3, _processor4, _processor5 };
				_resultMatcher = new ResultMatcher(processors, OnResultsMatched);

				toolClass.SaveLog("图像处理器初始化完成");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"初始化图像处理器失败: {ex.Message}");
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
				toolClass.SaveLog("系统开始初始化");
				Loading.ShowLoadingScreen();

				if (!UsbDogClass.FindUsbDog())
				{
					toolClass.SaveLog("初始化时，未找到加密狗");
					throw new Exception("初始化时，未找到加密狗");
				}
				_Config.cameraDebug = 0;

				LoadConfiguration();
				InitData();
				InitializeAIModels();

				modbusClass.EventConnectState += ModbusConnectState;
				modbusClass.EventCount += PLCCountMethod;

				if (modbusClass.ConnectModbus())
				{
					WriteResultThread = new Thread(WriteResultMethod);
					WriteResultThread.IsBackground = true;
					WriteResultThread.Start();
					toolClass.SaveLog("Modbus连接完成");
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

				toolClass.SaveLog("系统初始化完成");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"初始化时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
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
				toolClass.SaveLog($"VerifyMethod出现异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
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
					toolClass.SaveLog("过期图像清理完成");
				}
				catch (Exception ex)
				{
					toolClass.SaveLog($"清理过期图像失败: {ex.Message}");
				}
			});
		}

		private void InitCamera()
		{
			try
			{
				toolClass.SaveLog("开始初始化相机");

				camera1SDK = new DaHuaSDK();
				camera1SDK.SetCameraInterface(this);
				camera1SDK.OnImage += OnCamera1Image;
				camera1SDK.SetCameraByKey(_Config.Camera1SN);
				camera1SDK.Open();
				camera1SDK.StopStreamGrabber();
				camera1SDK.SetAcquisitionMode(0);
				camera1SDK.SetTriggerMode(1);
				camera1SDK.setTriggerSource(1);
				camera1SDK.StartStreamGrabber();
				GlobalVar.CameraSdk1 = camera1SDK;
				toolClass.SaveLog($"相机一加载成功：{_Config.Camera1SN}");
				Thread.Sleep(100);

				camera2SDK = new DaHuaSDK();
				camera2SDK.SetCameraInterface(this);
				camera2SDK.OnImage += OnCamera2Image;
				camera2SDK.SetCameraByKey(_Config.Camera2SN);
				camera2SDK.Open();
				camera2SDK.StopStreamGrabber();
				camera2SDK.SetAcquisitionMode(0);
				camera2SDK.SetTriggerMode(1);
				camera2SDK.setTriggerSource(1);
				camera2SDK.StartStreamGrabber();
				GlobalVar.CameraSdk2 = camera2SDK;
				toolClass.SaveLog($"相机二加载成功：{_Config.Camera2SN}");
				Thread.Sleep(100);

				camera3SDK = new DaHuaSDK();
				camera3SDK.SetCameraInterface(this);
				camera3SDK.OnImage += OnCamera3Image;
				camera3SDK.SetCameraByKey(_Config.Camera3SN);
				camera3SDK.Open();
				camera3SDK.StopStreamGrabber();
				camera3SDK.SetAcquisitionMode(0);
				camera3SDK.SetTriggerMode(1);
				camera3SDK.setTriggerSource(1);
				camera3SDK.StartStreamGrabber();
				GlobalVar.CameraSdk3 = camera3SDK;
				toolClass.SaveLog($"相机三加载成功：{_Config.Camera3SN}");
				Thread.Sleep(100);

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
				toolClass.SaveLog($"相机四加载成功：{_Config.Camera4SN}");
				Thread.Sleep(100);

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
				toolClass.SaveLog($"相机五加载成功：{_Config.Camera5SN}");

				toolClass.SaveLog("相机初始化完成");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"连接相机错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
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
				toolClass.SaveLog($"更新PLC计数异常: {ex.Message}");
			}
		}

		private void ModbusConnectState(bool state, string error)
		{
			toolClass.SaveLog($"ModbusConnectState: {error}");
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
				toolClass.SaveLog("控制器IP：" + ip);
				if (!myZmcaux.Connect(ref g_handle, ip))
					return false;

				MotionState.State = UILightState.On;
				myZmcaux.Init(g_handle);
				toolClass.SaveLog($"运控卡连接成功，句柄为：{g_handle}");
				return true;
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"连接运控卡出现异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
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

				modelpath_cam1 = _Config.ModelPath_Cam1;
				UseGpu_cam1 = _Config.UseGpu_Cam1;
				deviceid_cam1 = _Config.DeviceId_Cam1;
				modelId_char_cam1 = _Config.ModelId_Segmentation_Cam1;

				modelpath_cam2 = _Config.ModelPath_Cam2;
				UseGpu_cam2 = _Config.UseGpu_Cam2;
				deviceid_cam2 = _Config.DeviceId_Cam2;
				modelId_class_cam2 = _Config.ModelId_Class_Cam2;

				modelpath_cam4 = _Config.ModelPath_Cam4;
				UseGpu_cam4 = _Config.UseGpu_Cam4;
				deviceid_cam4 = _Config.DeviceId_Cam4;
				modelId_char_cam4 = _Config.ModelId_Char_Cam4;
				modelId_segmentation_cam4 = _Config.ModelId_Segmentation_Cam4;

				modelpath_cam5 = _Config.ModelPath_Cam5;
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

				toolClass.SaveLog($"配置加载完成: cam1偏移{offset_cam1}, cam2偏移{offset_cam2}, cam3偏移{offset_cam3}, cam4偏移{offset_cam4}, cam5偏移{offset_cam5}, 发送偏移{offset_send}");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"加载配置失败: {ex.Message}");
			}
		}

		private void InitializeAIModels()
		{
			try
			{
				if (_Config.IFRunCamera1)
				{
					Model_Segmentation_Cam1.Init(modelpath_cam1, UseGpu_cam1, deviceid_cam1, modelId_char_cam1);
				}

				if (_Config.IFRunCamera2)
				{
					Model_Class_Cam2.Init(modelpath_cam2, UseGpu_cam2, deviceid_cam2, modelId_class_cam2);
				}

				if (_Config.IFRunCamera4)
				{
					Model_Segmentation_Cam4.Init(modelpath_cam4, UseGpu_cam4, deviceid_cam4, modelId_segmentation_cam4);
					Model_Char_Cam4.Init(modelpath_cam4, UseGpu_cam4, deviceid_cam4, modelId_char_cam4);
				}

				if (_Config.IFRunCamera5)
				{
					Model_Char_Cam5.Init(modelpath_cam5, UseGpu_cam5, deviceid_cam5, modelId_char_cam5);
					Model_Char_PCode_Cam5.Init(modelpath_cam5, UseGpu_cam5, deviceid_cam5, modelId_char_PCode_cam5);
					Model_Color_Cam5.Init(modelpath_cam5, UseGpu_cam5, deviceid_cam5, modelId_color_cam5);
					Model_Rests_Cam5.Init(modelpath_cam5, UseGpu_cam5, deviceid_cam5, modelId_rests_cam5);
					Model_Segmentation_Cam5.Init(modelpath_cam5, UseGpu_cam5, deviceid_cam5, modelId_segmentation_cam5);
				}
				toolClass.SaveLog("AI模型初始化完成");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"AI模型初始化失败: {ex.Message}");
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
				}

				updateThread = new Thread(UpdateMethod);
				updateThread.IsBackground = true;
				updateThread.Start();
				toolClass.SaveLog("IO线程已启动");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"启动IO线程失败: {ex.Message}");
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
					toolClass.SaveLog($"读IO信号异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		#region 实现相机接口
		public void OnCameraClose(string cameraName, string cameraKey)
		{
			toolClass.SaveLog(string.Format("相机【{0}】关闭连接", cameraKey));
			if (camera1SDK.curCameraKey.Equals(cameraKey))
				camera1State.State = Sunny.UI.UILightState.Off;
			else if (camera2SDK.curCameraKey.Equals(cameraKey))
				camera2State.State = Sunny.UI.UILightState.Off;
			else if (camera3SDK.curCameraKey.Equals(cameraKey))
				camera3State.State = Sunny.UI.UILightState.Off;
			else if (camera4SDK.curCameraKey.Equals(cameraKey))
				camera4State.State = Sunny.UI.UILightState.Off;
			else if (camera5SDK.curCameraKey.Equals(cameraKey))
				camera5State.State = Sunny.UI.UILightState.Off;
		}

		public void OnCameraOpen(string cameraName, string cameraKey)
		{
			toolClass.SaveLog(string.Format("相机【{0}】连接", cameraKey));
			if (camera1SDK.curCameraKey.Equals(cameraKey))
				camera1State.State = Sunny.UI.UILightState.On;
			else if (camera2SDK.curCameraKey.Equals(cameraKey))
				camera2State.State = Sunny.UI.UILightState.On;
			else if (camera3SDK.curCameraKey.Equals(cameraKey))
				camera3State.State = Sunny.UI.UILightState.On;
			else if (camera4SDK.curCameraKey.Equals(cameraKey))
				camera4State.State = Sunny.UI.UILightState.On;
			else if (camera5SDK.curCameraKey.Equals(cameraKey))
				camera5State.State = Sunny.UI.UILightState.On;
		}

		public void OnCameraConnectLoss(string cameraName, string cameraKey)
		{
			toolClass.SaveLog(string.Format("相机【{0}】丢失连接", cameraKey));
			if (camera1SDK.curCameraKey.Equals(cameraKey))
			{
				camera1State.State = Sunny.UI.UILightState.Off;
				Task.Factory.StartNew(() =>
				{
					camera1SDK.Close();
					while (!camera1SDK.IsOpen() && !_isClosing)
					{
						try
						{
							toolClass.SaveLog(string.Format("相机{0}【{1}】重连", 1, cameraKey));
							camera1SDK.SetCameraByKey(_Config.Camera1SN);
							camera1SDK.Open();
						}
						catch { }
						Thread.Sleep(1000);
					}
				});
			}
			else if (camera2SDK.curCameraKey.Equals(cameraKey))
			{
				camera2State.State = Sunny.UI.UILightState.Off;
				Task.Factory.StartNew(() =>
				{
					camera2SDK.Close();
					while (!camera2SDK.IsOpen() && !_isClosing)
					{
						try
						{
							toolClass.SaveLog(string.Format("相机{0}【{1}】重连", 2, cameraKey));
							camera2SDK.SetCameraByKey(_Config.Camera2SN);
							camera2SDK.Open();
						}
						catch { }
						Thread.Sleep(1000);
					}
				});
			}
			else if (camera3SDK.curCameraKey.Equals(cameraKey))
			{
				camera3State.State = Sunny.UI.UILightState.Off;
				Task.Factory.StartNew(() =>
				{
					camera3SDK.Close();
					while (!camera3SDK.IsOpen() && !_isClosing)
					{
						try
						{
							toolClass.SaveLog(string.Format("相机{0}【{1}】重连", 3, cameraKey));
							camera3SDK.SetCameraByKey(_Config.Camera3SN);
							camera3SDK.Open();
						}
						catch { }
						Thread.Sleep(1000);
					}
				});
			}
			else if (camera4SDK.curCameraKey.Equals(cameraKey))
			{
				camera4State.State = Sunny.UI.UILightState.Off;
				Task.Factory.StartNew(() =>
				{
					camera4SDK.Close();
					while (!camera4SDK.IsOpen() && !_isClosing)
					{
						try
						{
							toolClass.SaveLog(string.Format("相机{0}【{1}】重连", 4, cameraKey));
							camera4SDK.SetCameraByKey(_Config.Camera4SN);
							camera4SDK.Open();
						}
						catch { }
						Thread.Sleep(1000);
					}
				});
			}
			else if (camera5SDK.curCameraKey.Equals(cameraKey))
			{
				camera5State.State = Sunny.UI.UILightState.Off;
				Task.Factory.StartNew(() =>
				{
					camera5SDK.Close();
					while (!camera5SDK.IsOpen() && !_isClosing)
					{
						try
						{
							toolClass.SaveLog(string.Format("相机{0}【{1}】重连", 5, cameraKey));
							camera5SDK.SetCameraByKey(_Config.Camera5SN);
							camera5SDK.Open();
						}
						catch { }
						Thread.Sleep(1000);
					}
				});
			}
		}
		#endregion

		#region 相机图像处理方法（优化版）
		private void OnCamera1Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			if (_isClosing || _Config.cameraDebug != 0 || bitmap == null) return;
			try
			{
				long sequenceId = Interlocked.Increment(ref camera1Count);
				_processor1.AddImage(sequenceId, bitmap, offset_cam1, CameraSelect.Camera1);
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"相机一图像处理失败: {ex.Message}");
			}
		}

		private void OnCamera2Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			if (_isClosing || _Config.cameraDebug != 0 || bitmap == null) return;
			try
			{
				long sequenceId = Interlocked.Increment(ref camera2Count);
				_processor2.AddImage(sequenceId, bitmap, offset_cam2, CameraSelect.Camera2);
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"相机二图像处理失败: {ex.Message}");
			}
		}

		private void OnCamera3Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			if (_isClosing || _Config.cameraDebug != 0 || bitmap == null) return;
			try
			{
				long sequenceId = Interlocked.Increment(ref camera3Count);
				_processor3.AddImage(sequenceId, bitmap, offset_cam3, CameraSelect.Camera3);
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"相机三图像处理失败: {ex.Message}");
			}
		}

		private void OnCamera4Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			if (_isClosing || _Config.cameraDebug != 0 || bitmap == null) return;
			try
			{
				long sequenceId = Interlocked.Increment(ref camera4Count);
				_processor4.AddImage(sequenceId, bitmap, offset_cam4, CameraSelect.Camera4);
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"相机四图像处理失败: {ex.Message}");
			}
		}

		private void OnCamera5Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			if (_isClosing || _Config.cameraDebug != 0 || bitmap == null) return;
			try
			{
				long sequenceId = Interlocked.Increment(ref camera5Count);
				_processor5.AddImage(sequenceId, bitmap, offset_cam5, CameraSelect.Camera5);
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"相机五图像处理失败: {ex.Message}");
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
					toolClass.SaveLog($"time[{DateTime.Now:HH:mm:ss:fff}]相机一开始处理，ID: {id}");

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

				if (_Config.IFRunCamera1.ToBool())
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
										toolClass.SaveLog($"相机一分割结果 Label：{kv.Key}, Area: {area}");
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
					toolClass.SaveLog($"相机一分割模型出错：rsp_segmentation == null");
				}

				stageTimer.Stop();
				context.StageTimes["结果处理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();
				#endregion

				result = result_Segmentation;
				context.ProcessResult = result;

				if (!_isClosing)
				{

					DrawGdiCam1(labelImage1, result, result_Segmentation, totalArea, totalArea_Camera1, id);
					CacheImageForDefectSave("Camera1", labelImage, labelImage1, id);

					// 仅仅是限制 PictureBox 控件界面的刷新频率
					if (id % 3 == 0)
					{
						displayBitmap = labelImage1.ToBitmap();
						UpdatePictureBox1(displayBitmap);
					}
				}

				stageTimer.Stop();
				context.StageTimes["存储图像"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				context.ProcessResult = result;
				Interlocked.Increment(ref resultCount1);
				_resultMatcher?.SignalNewResult();
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"[Camera1] ID:{id} 处理异常: {ex.Message}\n{ex.StackTrace}");
				// ProcessResult已在上方设置，异常时保留原值
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
					toolClass.SaveLog($"time[{DateTime.Now:HH:mm:ss:fff}]相机二开始处理，ID: {id}");

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

				if (_Config.IFRunCamera2.ToBool())
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
					toolClass.SaveLog($"分类模型出错：rsp_class == null");
				}

				stageTimer.Stop();
				context.StageTimes["结果处理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();
				#endregion

				result = result_flaw;
				context.ProcessResult = result;

				if (!_isClosing)
				{

					DrawGdiCam2(labelImage1, result, result_class, id);
					CacheImageForDefectSave("Camera2", labelImage, labelImage1, id);

					// 降频刷新UI
					if (id % 3 == 0)
					{
						displayBitmap = labelImage1.ToBitmap();
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
				toolClass.SaveLog($"[Camera2] ID:{id} 处理异常: {ex.Message}\n{ex.StackTrace}");
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
					toolClass.SaveLog($"time[{DateTime.Now:HH:mm:ss:fff}]相机三开始处理，ID: {id}");

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
					toolClass.SaveLog($"相机三图像异常..");
					resultImage = labelImage.Clone();
				}
				else
				{
					DetectionResultV3 detectionResult = null;
					if (_Config.IFRunCamera3)
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
							toolClass.SaveLog($"[Camera3] ID:{id} 圆度检测 - LongEdge:{longEdge:F3}, PipeDiameter:{PipeDiameter}, Roundness:{roundness:F3}, Up:{_Config.Camera3RoundnessUp}, Down:{_Config.Camera3RoundnessDown}, Result:{result}");
						}
					}
					else
					{
						toolClass.SaveLog($"[Camera3] ID:{id} 圆度检测失败，设置为OK");
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


					DrawGdiCam3(resultImage, result, longEdge, PipeDiameter, roundness, id);
					CacheImageForDefectSave("Camera3", labelImage, resultImage, id);

					// 降频刷新 UI
					if (id % 3 == 0)
					{
						stageTimer.Stop();
						context.StageTimes["图像绘制"] = stageTimer.ElapsedMilliseconds;
						stageTimer.Restart();

						displayBitmap = resultImage.ToBitmap();
						UpdatePictureBox3(displayBitmap);

						stageTimer.Stop();
						context.StageTimes["显示更新"] = stageTimer.ElapsedMilliseconds;
						stageTimer.Restart();
					}
				}

				stageTimer.Stop();
				context.StageTimes["存储图像"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				context.ProcessResult = result;
				if (IFSaveLog)
				{
					toolClass.SaveLog($"[Camera3] ID:{id} 最终结果 - ProcessResult:{result}, SequenceId:{context.SequenceId}, Offset:{context.Offset}");
				}

				Interlocked.Increment(ref resultCount3);
				_resultMatcher?.SignalNewResult();
				#endregion
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"[Camera3] ID:{context.SequenceId} 处理异常: {ex.Message}\n{ex.StackTrace}");
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
					toolClass.SaveLog($"time[{DateTime.Now:HH:mm:ss:fff}]相机四开始处理，ID: {id}");

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

				if (_Config.IFRunCamera4.ToBool())
				{
					Task taskSeg = Task.Run(() => Model_Segmentation_Cam4.Run(labelImage, out rsp_segmentation));
					Task taskChar = Task.Run(() => Model_Char_Cam4.Run(labelImage, out rsp_ocr));
					Task.WaitAll(taskSeg, taskChar);
				}

				stageTimer.Stop();
				context.StageTimes["AI模型推理"] = stageTimer.ElapsedMilliseconds;
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
							toolClass.SaveLog($"相机四OCR模型出错：rsp_ocr == null");
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

						result_char = Convert.ToInt16(result_Char_str, 2) == 0;
						toolClass.SaveLog($"[Camera4] ID:{id} 字符检测 - IndexOCR:{index_ocr}, StandChar:{camera4StandChar}, ResultStr:{result_Char_str}, ResultChar:{result_char}");
						if (!result_char && rsp_ocr != null) foreach (var item in rsp_ocr) { var blocks = item.Item2?.Blocks; if (blocks != null) foreach (var block in blocks) Cv2.Rectangle(labelImage1, Cv2.BoundingRect(block.Polygon), new Scalar(0, 0, 255), 2); }
					}
					else
					{
						order_ocr = $"Char Count: Workpiece Not Found";
						result_char = true;
						toolClass.SaveLog($"[Camera4] ID:{id} 工件未找到");
					}
				}
				else
				{
					if (rsp_segmentation == null)
					{
						toolClass.SaveLog($"[Camera4] ID:{id} rsp_segmentation == null");
						order_ocr = $"Cam4 rsp_segmentation == null;";
					}
					else if (rsp_ocr == null)
					{
						order_ocr = $"Cam4 result_char == null;";
						toolClass.SaveLog($"[Camera4] ID:{id} result_char == null");
					}
					result_char = true;
				}
				result = result_char;
				context.ProcessResult = result;
				#endregion

				stageTimer.Stop();
				context.StageTimes["结果处理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				if (!_isClosing)
				{

					DrawGdiCam4(labelImage1, result, result_char, order_ocr, id);
					CacheImageForDefectSave("Camera4", labelImage, labelImage1, id);

					// 降频刷新 UI
					if (id % 3 == 0)
					{
						stageTimer.Stop();
						context.StageTimes["图像绘制"] = stageTimer.ElapsedMilliseconds;
						stageTimer.Restart();

						displayBitmap = labelImage1.ToBitmap();
						UpdatePictureBox4(displayBitmap);

						stageTimer.Stop();
						context.StageTimes["显示更新"] = stageTimer.ElapsedMilliseconds;
						stageTimer.Restart();
					}
				}

				stageTimer.Stop();
				context.StageTimes["存储图像"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				toolClass.SaveLog($"[Camera4] ID:{id} 处理完成 - result_char:{result_char}, result:{result}");
				Interlocked.Increment(ref resultCount4);
				_resultMatcher?.SignalNewResult();
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"[Camera4] ID:{id} 处理异常: {ex.Message}\n{ex.StackTrace}");
				// ProcessResult已在上方设置，异常时保留原值
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
					toolClass.SaveLog($"time[{DateTime.Now:HH:mm:ss:fff}]相机五开始处理，ID: {id}");

				StartMethod(CameraSelect.Camera5);

				Bitmap bitmap = context.OriginalBitmap;
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

				if (_Config.IFRunCamera5.ToBool())
				{
					var tasks = new Task[5];
					tasks[0] = Task.Run(() => Model_Segmentation_Cam5.Run(labelImage, out rsp_segmentation));
					tasks[1] = Task.Run(() => Model_Char_Cam5.Run(labelImage, out rsp_ocr));
					tasks[2] = Task.Run(() => Model_Color_Cam5.Run(labelImage, out rsp_color));
					tasks[3] = Task.Run(() => Model_Char_PCode_Cam5.Run(labelImage, out rsp_PCode_ocr));
					tasks[4] = Task.Run(() => Model_Rests_Cam5.Run(labelImage, out rsp_rests));
					Task.WaitAll(tasks);
				}

				stageTimer.Stop();
				context.StageTimes["AI模型推理"] = stageTimer.ElapsedMilliseconds;
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
					toolClass.SaveLog($"OCR模型出错：rsp_ocr == null");
				}

				if (_Config.Camera5IFOcr)
				{
					result_char = Convert.ToInt16(result_Char_str, 2) == 0;
				}
				else
				{
					result_char = true;
					if (!result_char && rsp_ocr != null) foreach (var item in rsp_ocr) { var blocks = item.Item2?.Blocks; if (blocks != null) foreach (var block in blocks) Cv2.Rectangle(labelImage1, Cv2.BoundingRect(block.Polygon), new Scalar(0, 0, 255), 2); }
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
					toolClass.SaveLog($"OCR模型出错：rsp_ocr == null");
				}

				if (_Config.Camera5IFPCode)
				{
					result_PCode_char = Convert.ToInt16(result_Char_PCode_str, 2) == 0;
				}
				else
				{
					result_PCode_char = true;
				}
				if (!result_PCode_char && rsp_PCode_ocr != null) foreach (var item in rsp_PCode_ocr) { var blocks = item.Item2?.Blocks; if (blocks != null) foreach (var block in blocks) Cv2.Rectangle(labelImage1, Cv2.BoundingRect(block.Polygon), new Scalar(0, 0, 255), 2); }
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

				result_flaw = Convert.ToInt16(result_Class_str, 2) == 0;
				#endregion

				if (label_str.Contains("空杯"))
				{
					result_char = true;
					result_PCode_char = true;
					toolClass.SaveLog($"[Camera5] ID:{id} 空杯产品");
				}
				result = result_char && result_PCode_char && result_flaw && result_Segmentation;
				context.ProcessResult = result;
				toolClass.SaveLog($"[Camera5] ID:{id} 最终结果 - result_char:{result_char}, result_PCode_char:{result_PCode_char}, result_flaw:{result_flaw}, result_Segmentation:{result_Segmentation}, FinalResult:{result}");

				stageTimer.Stop();
				context.StageTimes["结果处理"] = stageTimer.ElapsedMilliseconds;
				stageTimer.Restart();

				if (!_isClosing)
				{
					int queueDepth = _processor5?.ImageQueueCount ?? 0;
					bool needOutput = queueDepth <= 2;

					// Camera5积压时跳过显示，省50ms提速追赶
					if (needOutput)
					{
						DrawGdiCam5(labelImage1, result, result_char, result_PCode_char, result_flaw, result_Segmentation, result_class, order_ocr, pcode_ocr, projectionLength, segmentationResult, id);
						displayBitmap = labelImage1.ToBitmap();
						UpdatePictureBox5(displayBitmap);
					}
					else if (queueDepth % 10 == 0)
					{
						toolClass.SaveLog($"[Camera5] 积压{queueDepth}帧，跳过显示提效");
					}

					// 始终缓存图像用于存图（无论是否积压）
					CacheImageForDefectSave("Camera5", labelImage, labelImage1, id);

					stageTimer.Stop();
					context.StageTimes["显示更新及存储图像"] = stageTimer.ElapsedMilliseconds;
					stageTimer.Restart();
				}

				context.ProcessResult = result;

				var cam5Result = new QueueResultItem
				{
					SequenceId = context.SequenceId,
					Offset = context.Offset,
					Result = result,
					Timestamp = DateTime.Now,
					Cam5_CharResult = result_char ? 1 : 0,
					Cam5_PCodeResult = result_PCode_char ? 1 : 0,
					Cam5_SebiaoResult = result_Segmentation ? 1 : 0,
					Cam5_BaoguanResult = (_Config.Camera5IFBaoGuan && result_class.Contains("爆管")) ? 0 : 1,
					Cam5_XiekouResult = (_Config.Camera5IFXieKou && result_class.Contains("斜口")) ? 0 : 1,
					Cam5_WeijianduanResult = (_Config.Camera5IFWeiJianDuan && result_class.Contains("未剪断")) ? 0 : 1
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
				toolClass.SaveLog($"[Camera5] ID:{context.SequenceId} 处理异常: {ex.Message}\n{ex.StackTrace}");
				// ProcessResult已在上方设置，异常时保留原值
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
					toolClass.SaveLog($"高速保存平均耗时: {avgTime:F2}ms, 总次数: {_totalSaveCount}");
				}
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"{cameraName} 高速保存异常: {ex.Message}");
			}
			finally
			{
				if (timer.ElapsedMilliseconds > 10)
				{
					toolClass.SaveLog($"{cameraName} 保存耗时较高: {timer.ElapsedMilliseconds}ms");
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
					return "背面工号缺失";
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
				toolClass.SaveLog($"{cameraName} 原图保存异常: {ex.Message}");
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
				toolClass.SaveLog($"{cameraName} 结果图保存异常: {ex.Message}");
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
				toolClass.SaveLog($"缓存图像异常: {ex.Message}");
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
				// 卸载JPEG编码到线程池，不阻塞DB消费者线程
				long captureId = unifiedId;
				var captureResults = results;
				System.Threading.ThreadPool.QueueUserWorkItem(_ =>
				{
					try
					{
						SaveImagesByDefectType(captureId, captureResults);
					}
					catch (Exception ex)
					{
						toolClass.SaveLog($"[存图] UnifiedId:{captureId} 存图异常: {ex.Message}");
					}
				});
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
				string dateFolder = DateTime.Now.ToString("yyMMdd");
				string shiftFolder = _currentShift;
				string skuFolder = GetCurrentSkuValue();

				string dtFormat = DateTime.Now.ToString("yyMMddHHmmssfff");

				// 保存各相机的图像（加锁快照，防止并发修改内部字典）
				if (_imageCache.TryGetValue(sequenceId, out var originalImages) &&
					_resultImageCache.TryGetValue(sequenceId, out var resultImages))
				{
					KeyValuePair<string, Mat>[] snap;
					lock (originalImages) { snap = originalImages.ToArray(); }
					foreach (var kvp in snap)
					{
						string cameraName = kvp.Key;
						Mat original = kvp.Value;
						Mat result = null;
						lock (resultImages) { resultImages.TryGetValue(cameraName, out result); }

						// 获取当前相机的缺陷类型和OK/NG状态
						bool isCameraNg = IsCameraNg(cameraName, results);
						string defectFolder = GetDefectTypeForCamera(cameraName, results);
						bool isOk = defectFolder == "OK";

						// 如果只保存NG图片且当前相机是OK，则跳过
						if (!IFSaveOKImage && !IFSaveOKRawImage && isOk)
						{
							continue;
						}

						var saver = GetHighSpeedSaver(cameraName);
						if (saver == null) continue;

						// 构建路径：日期 -> 班次 -> SKU -> OK/NG -> 相机名 -> 缺陷类型
						string resultFolder = isOk ? "OK" : "NG";
						string basePath = Path.Combine(_Config.ImagePath, dateFolder, shiftFolder, skuFolder, resultFolder, cameraName);
						if (!isOk)
						{
							basePath = Path.Combine(basePath, defectFolder);
						}

						toolClass.SaveLog($"开始存图: SequenceId={sequenceId}, Camera={cameraName}, Defect={defectFolder}, Path={basePath}");

						// 保存原图
						if (original != null && ((isOk && IFSaveOKRawImage) || (!isOk && IFSaveNGRawImage)))
						{
							SaveOriginalImage_Fast(saver, cameraName, original, basePath, dtFormat, sequenceId, 0, isOk);
						}

						// 保存结果图
						if (result != null && ((isOk && IFSaveOKImage) || (!isOk && IFSaveNGImage)))
						{
							SaveResultImage_Fast(saver, cameraName, result, basePath, dtFormat, sequenceId, 0, isOk);
						}
					}

					// 清理缓存（释放Bitmap并移除）
					ClearImageCache(sequenceId);
				}
				else
				{
					toolClass.SaveLog($"缓存中未找到图像: SequenceId={sequenceId}");
				}
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"按缺陷类型存图异常: {ex.Message}");
				// 发生异常时也要清理缓存，避免内存泄漏
				ClearImageCache(sequenceId);
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
					return results[0]?.Result == false ? "NG" : "OK";
				case "Camera2":
					return results[1]?.Result == false ? "NG" : "OK";
				case "Camera3":
					return results[2]?.Result == false ? "NG" : "OK";
				case "Camera4":
					return results[3]?.Result == false ? "NG" : "OK";
				case "Camera5":
					return results[4]?.Result == false ? (results[4]?.Cam5_BaoguanResult == 0 ? "爆管" :
						results[4]?.Cam5_XiekouResult == 0 ? "斜口" :
						results[4]?.Cam5_WeijianduanResult == 0 ? "未剪断" :
						results[4]?.Cam5_CharResult == 0 ? "背面工号缺失" :
						results[4]?.Cam5_PCodeResult == 0 ? "P-Code" :
						results[4]?.Cam5_SebiaoResult == 0 ? "色标对中" : "NG") : "OK";
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
				toolClass.SaveLog($"清理图像缓存异常: {ex.Message}");
			}
		}
		#endregion

		#region GDI+中文绘制
		private void DrawGdiCam1(Mat img, bool result, bool result_Segmentation, double totalArea, int totalArea_Camera1, long id)
		{
			try
			{
				using (var bmp = img.ToBitmap())
				using (var g = Graphics.FromImage(bmp))
				{
					int x = bmp.Width - 500; int y = 30;
					g.DrawString("相机：底部异物检测", _processor1.DrawFontTitle, _processor1.DrawBrushGreen, x, y); y += 70;
					g.DrawString("检测时间：" + DateTime.Now.ToString("HH:mm:ss.fff"), _processor1.DrawFontText, _processor1.DrawBrushGreen, x, y); y += 70;
					g.DrawString("综合结果：" + (result ? "OK" : "NG"), _processor1.DrawFontText, result ? _processor1.DrawBrushGreen : _processor1.DrawBrushRed, x, y); y += 70;
					if (!result_Segmentation) { g.DrawString("异物面积：" + totalArea.ToString("F2") + " / " + totalArea_Camera1, _processor1.DrawFontText, _processor1.DrawBrushRed, x, y); y += 70; }
					g.DrawString("SequenceId：" + id, _processor1.DrawFontText, _processor1.DrawBrushGreen, x, y);
					using (var tmp = BitmapConverter.ToMat(bmp)) { tmp.CopyTo(img); }
				}
			}
			catch { }
		}
		private void DrawGdiCam2(Mat img, bool result, string result_class, long id)
		{
			try
			{
				using (var bmp = img.ToBitmap())
				using (var g = Graphics.FromImage(bmp))
				{
					int x = bmp.Width - 500; int y = 30;
					g.DrawString("相机：瓶盖有无检测", _processor2.DrawFontTitle, _processor2.DrawBrushGreen, x, y); y += 70;
					g.DrawString("检测时间：" + DateTime.Now.ToString("HH:mm:ss.fff"), _processor2.DrawFontText, _processor2.DrawBrushGreen, x, y); y += 70;
					g.DrawString("综合结果：" + (result ? "OK" : "NG"), _processor2.DrawFontText, result ? _processor2.DrawBrushGreen : _processor2.DrawBrushRed, x, y); y += 70;
					if (!string.IsNullOrEmpty(result_class)) { g.DrawString("缺陷标签：" + result_class, _processor2.DrawFontText, _processor2.DrawBrushRed, x, y); y += 70; }
					g.DrawString("SequenceId：" + id, _processor2.DrawFontText, _processor2.DrawBrushGreen, x, y);
					using (var tmp = BitmapConverter.ToMat(bmp)) { tmp.CopyTo(img); }
				}
			}
			catch { }
		}
		private void DrawGdiCam3(Mat img, bool result, double longEdge, double PipeDiameter, double roundness, long id)
		{
			try
			{
				using (var bmp = img.ToBitmap())
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
					using (var tmp = BitmapConverter.ToMat(bmp)) { tmp.CopyTo(img); }
				}
			}
			catch { }
		}
		private void DrawGdiCam4(Mat img, bool result, bool result_char, string order_ocr, long id)
		{
			try
			{
				using (var bmp = img.ToBitmap())
				using (var g = Graphics.FromImage(bmp))
				{
					int x = bmp.Width - 500; int y = 30;
					g.DrawString("相机：夹尾正面字符检测", _processor4.DrawFontTitle, _processor4.DrawBrushGreen, x, y); y += 70;
					g.DrawString("检测时间：" + DateTime.Now.ToString("HH:mm:ss.fff"), _processor4.DrawFontText, _processor4.DrawBrushGreen, x, y); y += 70;
					g.DrawString("综合结果：" + (result ? "OK" : "NG"), _processor4.DrawFontText, result ? _processor4.DrawBrushGreen : _processor4.DrawBrushRed, x, y); y += 70;
					g.DrawString("字符结果：" + (result_char ? "OK" : "NG"), _processor4.DrawFontText, result_char ? _processor4.DrawBrushGreen : _processor4.DrawBrushRed, x, y); y += 70;
					if (!string.IsNullOrEmpty(order_ocr)) { g.DrawString(order_ocr, _processor4.DrawFontText, result_char ? _processor4.DrawBrushGreen : _processor4.DrawBrushRed, x, y); y += 70; }
					g.DrawString("SequenceId：" + id, _processor4.DrawFontText, _processor4.DrawBrushGreen, x, y);
					using (var tmp = BitmapConverter.ToMat(bmp)) { tmp.CopyTo(img); }
				}
			}
			catch { }
		}
		private void DrawGdiCam5(Mat img, bool result, bool result_char, bool result_PCode_char, bool result_flaw, bool result_Segmentation, string result_class, string order_ocr, string pcode_ocr, double projectionLength, SegmentationResult segRst, long id)
		{
			try
			{
				using (var bmp = img.ToBitmap())
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
					using (var tmp = BitmapConverter.ToMat(bmp)) { tmp.CopyTo(img); }
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
									if (_Config.total > 0)
									{
										_Config.total -= 1;
										if (_Config.ok > 0) _Config.ok -= 1;
									}
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
				// 【调试日志】记录每个相机的匹配结果
				if (IFSaveLog)
				{
					toolClass.SaveLog($"[ResultMatch] 开始匹配 - Cam1: Seq={results[0].SequenceId}, Offset={results[0].Offset}, Result={results[0].Result}");
					toolClass.SaveLog($"[ResultMatch] 开始匹配 - Cam2: Seq={results[1].SequenceId}, Offset={results[1].Offset}, Result={results[1].Result}");
					toolClass.SaveLog($"[ResultMatch] 开始匹配 - Cam3: Seq={results[2].SequenceId}, Offset={results[2].Offset}, Result={results[2].Result}");
					toolClass.SaveLog($"[ResultMatch] 开始匹配 - Cam4: Seq={results[3].SequenceId}, Offset={results[3].Offset}, Result={results[3].Result}");
					toolClass.SaveLog($"[ResultMatch] 开始匹配 - Cam5: Seq={results[4].SequenceId}, Offset={results[4].Offset}, Result={results[4].Result}");
				}

				bool finalResult = results[0].Result && results[1].Result && results[2].Result && results[3].Result && results[4].Result;
				long unifiedId = results[0].SequenceId - results[0].Offset;

				toolClass.SaveLog($"[ResultMatch] ID:{unifiedId} 汇总 - Cam1:{results[0].Result}, Cam2:{results[1].Result}, Cam3:{results[2].Result}, Cam4:{results[3].Result}, Cam5:{results[4].Result}, FinalResult:{finalResult}");

				AddProductionRecordBuffered(results, finalResult);
				// 计数器更新必须与burstExcludeCount同步，不能放在BeginInvoke中延迟
				ResultCountMethod(results[0].Result, results[1].Result, results[2].Result, results[3].Result, results[4].Result);


				// 图像保存改为DB记录提交后触发（OnDbRecordCommitted回调），确保记录与图片一一对应
				_pendingImageSaves[unifiedId] = results;

				if (modbusClass.modbusState && !_isClosing)
				{
					lock (SendResultList)
					{
						SendResultList.Add(new QueueResultItem
						{
							SequenceId = unifiedId,
							Offset = 0,
							Result = finalResult,
							Timestamp = DateTime.Now
						});
					}
					toolClass.SaveLog($"time[{DateTime.Now:HH:mm:ss:fff}]结果匹配成功 ID: {unifiedId} Result: {finalResult}");
				}

				if (!_isClosing)
				{
					this.BeginInvoke(new Action(() =>
					{
						if (_isClosing) return;
						try
						{
							ResultShowMethod(finalResult);
							ImageIDTxt1.Text = unifiedId.ToString();
							ImageIDTxt2.Text = results[1].SequenceId.ToString();
							ImageIDTxt3.Text = results[2].SequenceId.ToString();
							ImageIDTxt4.Text = results[3].SequenceId.ToString();
							ImageIDTxt5.Text = results[4].SequenceId.ToString();
							OffsetTxt1.Text = results[0].Offset.ToString();
							OffsetTxt2.Text = results[1].Offset.ToString();
							OffsetTxt3.Text = results[2].Offset.ToString();
							OffsetTxt4.Text = results[3].Offset.ToString();
							OffsetTxt5.Text = results[4].Offset.ToString();
						}
						catch (Exception uiEx) { toolClass.SaveLog($"UI更新异常: {uiEx.Message}"); }
					}));
				}
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"结果匹配回调异常: {ex.Message}");
			}
		}

		/// <summary>
		/// 添加生产记录（异步，不阻塞）
		/// </summary>
		private void AddProductionRecord(QueueResultItem[] results, bool finalResult)
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

					// 相机结果 (1=OK, 0=NG)
					Cam1Result = results[0].Result ? 1 : 0,
					Cam2Result = results[1].Result ? 1 : 0,
					Cam3Result = results[2].Result ? 1 : 0,
					Cam4Result = results[3].Result ? 1 : 0,
					Cam5Result = results[4].Result ? 1 : 0,

					// 相机5细分结果
					Cam5_CharResult = results[4].Cam5_CharResult,
					Cam5_PCodeResult = results[4].Cam5_PCodeResult,
					Cam5_SebiaoResult = results[4].Cam5_SebiaoResult,
					Cam5_BaoguanResult = results[4].Cam5_BaoguanResult,
					Cam5_XiekouResult = results[4].Cam5_XiekouResult,
					Cam5_WeijianduanResult = results[4].Cam5_WeijianduanResult
				};

				// 先更新连续爆管状态
				if (_dbRecorder != null)
				{
					// 只要有爆管缺陷即可，不管是否有其他缺陷
					bool isBurstNg = results[4].Cam5_BaoguanResult == 0;  // 爆管检测为NG

					_dbRecorder.UpdateConsecutiveBurst(results[0].SequenceId, isBurstNg);
				}

				// 再添加记录（此时历史记录已经更新，可以正确判断连续爆管）
				_dbRecorder.AddRecord(record);

				// 检查是否被标记为连续爆管剔除，如果是则增加计数（连续3个爆管，所以增加3）
				if (record.IsExcluded && record.ExcludedReason == "连续爆管剔除")
				{
					// 连续爆管剔除会标记3条记录，所以计数增加3
					_Config.burstExcludeCount += 3;
					if (BaoGuanNGTxt != null)
					{
						this.Invoke(new Action(() =>
						{
							if (BaoGuanNGTxt != null)
								BaoGuanNGTxt.Text = _Config.burstExcludeCount.ToString();
						}));
					}
				}
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"添加生产记录异常: {ex.Message}");
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
						if (r.IsExcluded && r.ExcludedReason == "连续爆管剔除")
							_Config.burstExcludeCount += 3;
					}
					this.Invoke(new Action(() => { if (BaoGuanNGTxt != null) BaoGuanNGTxt.Text = _Config.burstExcludeCount.ToString(); }));
				}
			}
			catch (Exception ex) { toolClass.SaveLog($"批量生产记录异常: {ex.Message}"); }
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
					if (r.IsExcluded && r.ExcludedReason == "连续爆管剔除")
						_Config.burstExcludeCount += 3;
				}
				if (_Config.burstExcludeCount > 0 || flushList.Any(r => r.IsExcluded))
				{
					this.Invoke(new Action(() =>
					{
						if (BaoGuanNGTxt != null)
							BaoGuanNGTxt.Text = _Config.burstExcludeCount.ToString();
					}));
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
			catch (Exception ex) { toolClass.SaveLog($"显示结果异常: {ex.Message}"); }
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

		private void ResultCountMethod(bool result1, bool result2, bool result3, bool result4, bool result5)
		{
			try
			{
				_Config.total++;
				if (!result1) _Config.ng_cam1++;
				if (!result2) _Config.ng_cam2++;
				if (!result3) _Config.ng_cam3++;
				if (!result4) _Config.ng_cam4++;
				if (!result5) _Config.ng_cam5++;
				if (result1 && result2 && result3 && result4 && result5) _Config.ok++;

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

				InitData();
			}
			catch (Exception ex) { toolClass.SaveLog($"更新计数异常: {ex.Message}"); }
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
				toolClass.SaveLog("应用程序正在关闭...");

				// 取消所有任务
				_cts?.Cancel();

				// 停止接收新图像
				StopCameraGrab();

				// 停止性能监控
				_performanceTimer?.Stop();
				_performanceTimer?.Dispose();

				// 停止处理器
				StopProcessors();

				// 停止并等待线程
				StopAllThreads();

				// 关闭 Modbus
				modbusClass?.CloseModbus();

				// 释放保存器
				DisposeHighSpeedSavers();

				// 释放内存池
				DisposeMemoryPools();

				// 关闭相机
				DisposeCameras();

				// 停止定时器 + 刷新最后的数据库记录
				_batchFlushTimer?.Stop();
				_batchFlushTimer?.Dispose();
				FlushPendingRecords();

				// 清理其他资源
				CleanupAllResources();

				// 通知等待完成
				_closeWaitHandle.Set();

				// 释放数据库记录器
				_dbRecorder?.Dispose();

				toolClass.SaveLog("应用程序关闭完成");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"关闭时异常: {ex.Message}");
			}
			finally
			{
				_closeWaitHandle?.Dispose();
				_cts?.Dispose();
			}
		}

		private void StopCameraGrab()
		{
			try
			{
				camera1SDK?.StopStreamGrabber();
				camera2SDK?.StopStreamGrabber();
				camera3SDK?.StopStreamGrabber();
				camera4SDK?.StopStreamGrabber();
				camera5SDK?.StopStreamGrabber();
				toolClass.SaveLog("相机采图已停止");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"停止相机采图异常: {ex.Message}");
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
				toolClass.SaveLog("处理器已释放");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"释放处理器异常: {ex.Message}");
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
								toolClass.SaveLog($"线程 {thread.Name} 未在 {timeout}ms 内结束");
							}
						}
						catch (Exception ex)
						{
							toolClass.SaveLog($"等待线程结束异常: {ex.Message}");
						}
					}
				}

				toolClass.SaveLog("所有工作线程已停止");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"停止线程异常: {ex.Message}");
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
				toolClass.SaveLog("高速保存器已释放");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"释放高速保存器异常: {ex.Message}");
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
				toolClass.SaveLog("内存池已释放");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"释放内存池异常: {ex.Message}");
			}
		}

		private void DisposeCameras()
		{
			try
			{
				camera1SDK?.Close();
				camera2SDK?.Close();
				camera3SDK?.Close();
				camera4SDK?.Close();
				camera5SDK?.Close();
				toolClass.SaveLog("相机已关闭");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"关闭相机异常: {ex.Message}");
			}
		}

		private void CleanupAllResources()
		{
			try
			{
				toolClass.SaveLog("开始清理所有资源...");

				lock (SendResultList)
				{
					SendResultList?.Clear();
				}

				this.Invoke(new Action(() =>
				{
					var pictureBoxes = new[] { xlPictureBox1, xlPictureBox2, xlPictureBox3, xlPictureBox4, xlPictureBox5 };
					foreach (var pb in pictureBoxes)
					{
						if (pb.Image != null)
						{
							try { pb.Image.Dispose(); pb.Image = null; }
							catch { }
						}
					}
				}));

				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();

				toolClass.SaveLog("资源清理完成");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"清理资源异常: {ex.Message}");
			}
		}
		#endregion

		#region 数据保存
		/// <summary>
		/// 检查班次变化并自动保存（在定时器中调用）
		/// </summary>
		private void CheckShiftChangeAndAutoSave()
		{
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
					toolClass.SaveLog($"班次切换: {_currentShift} -> {newShift}, 已自动保存{_currentShift}班次报表");

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
				toolClass.SaveLog($"检查班次变化异常: {ex.Message}");
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
			// 调用AsyncDatabaseRecorder的完整导出功能，异步执行，不影响主程序
			if (_dbRecorder != null)
			{
				_dbRecorder.ExportFullShiftReport(date, shift);
			}
		}

		/// <summary>
		/// 手动保存当前班次报表
		/// </summary>
		public void ManualSaveCurrentShiftReport()
		{
			if (!string.IsNullOrEmpty(_currentShiftDate) && !string.IsNullOrEmpty(_currentShift))
			{
				toolClass.SaveLog($"开始手动保存班次报表: {_currentShiftDate} {_currentShift}");

				// 调用AsyncDatabaseRecorder的完整导出功能（异步执行，保存完成后自动打开文件夹）
				if (_dbRecorder != null)
				{
					_dbRecorder.ExportFullShiftReport(_currentShiftDate, _currentShift, true);
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
		string imagePath5 = @"D:\bin\AI\Image\camera5_2.bmp";
		string imagePath5_2 = @"D:\bin\AI\Image\Camera5_5.bmp";  // Camera5 第二张图，与 imagePath5 交替使用
		string imagePath5_3 = @"D:\bin\AI\Image\Camera5_4.bmp";  // Camera5 第二张图，与 imagePath5 交替使用

		private void uiButton4_Click(object sender, EventArgs e)
		{
			Random random = new Random();
			testflag = true;

			toolClass.SaveLog($"imagePath1: {imagePath1}");
			toolClass.SaveLog($"imagePath2: {imagePath2}");
			toolClass.SaveLog($"imagePath3: {imagePath3}");
			toolClass.SaveLog($"imagePath4: {imagePath4}");
			toolClass.SaveLog($"imagePath5: {imagePath5}");

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
				toolClass.SaveLog($"创建 Bitmap 错误: {ex.Message}");
			}
		}

		int zhmTest = 1;

		private void totalTxt_Click(object sender, EventArgs e)
		{
			zhmTest = 0;
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
		private int _simCam5Idx = 0;  // Camera5 三张图轮播
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
				clearBtn.Text = "模拟运行";
				toolClass.SaveLog("模拟运行已停止");
				return;
			}

			if (!File.Exists(imagePath1)) { MessageBox.Show($"Camera1 图片不存在:\n{imagePath1}"); return; }
			if (!File.Exists(imagePath2)) { MessageBox.Show($"Camera2 图片不存在:\n{imagePath2}"); return; }
			if (!File.Exists(imagePath3)) { MessageBox.Show($"Camera3 图片不存在:\n{imagePath3}"); return; }
			if (!File.Exists(imagePath4)) { MessageBox.Show($"Camera4 图片不存在:\n{imagePath4}"); return; }
			if (!File.Exists(imagePath5)) { MessageBox.Show($"Camera5 图片不存在:\n{imagePath5}"); return; }

			_simRunning = true;
			_simBatchNum = 0;
			clearBtn.Text = "停止模拟";
			toolClass.SaveLog("模拟运行启动，3个/组 间隔10ms，平均200ms/张");

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

							var bmp1 = new Bitmap(imagePath1);
							var bmp2 = new Bitmap(imagePath2);
							var bmp3 = new Bitmap(imagePath3);
							var bmp4 = new Bitmap(imagePath4);
							_simCam5Idx = (_simCam5Idx + 1) % 3;
							string cam5Path = _simCam5Idx == 0 ? imagePath5 : _simCam5Idx == 1 ? imagePath5_2 : imagePath5_3;
							var bmp5 = new Bitmap(cam5Path);

							OnCamera1Image(bmp1, null, null);
							OnCamera2Image(bmp2, null, null);
							OnCamera3Image(bmp3, null, null);
							OnCamera4Image(bmp4, null, null);
							OnCamera5Image(bmp5, null, null);

							sw.Stop();
							toolClass.SaveLog($"[模拟] 批次{_simBatchNum} 添加耗时{sw.ElapsedMilliseconds}ms Cam5Idx={_simCam5Idx}");

							if (g < 2)
								Thread.Sleep(100);
						}
						catch (Exception ex) { toolClass.SaveLog($"模拟运行异常: {ex.Message}"); }
					}

					// 每组3张，平均200ms/张 → 组周期600ms，减去处理时间和组内间隔
					_simGroupSw.Stop();
					long remaining = 600 - _simGroupSw.ElapsedMilliseconds;
					if (remaining > 10)
						Thread.Sleep((int)remaining);
				}
			});

			_simRunning = false;
			this.Invoke(new Action(() => clearBtn.Text = "模拟运行"));
			toolClass.SaveLog("模拟运行已停止");
		}

		private Stopwatch _sendIntervalTimer = Stopwatch.StartNew();
		private List<long> _sendIntervals = new List<long>();

		private void WriteResultMethod()
		{
			try
			{
				toolClass.SaveLog("写入结果线程启动");

				int consecutiveFailures = 0;
				const int MAX_CONSECUTIVE_FAILURES = 50;
				int startIndex = 1;
				bool result1 = false, result2 = false, result3 = false;

				while (!_isClosing)
				{
					try
					{
						Thread.Sleep(1);

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

							if (zhmTest == 0) result1 = false;

							bool writeSuccess = false;
							int retryCount = 0;
							const int MAX_RETRY = 3;

							while (!writeSuccess && retryCount < MAX_RETRY && !_isClosing)
							{
								try
								{
									writeSuccess = modbusClass.WriteResult(result1, result2, result3);
									if (writeSuccess)
									{
										long interval = _sendIntervalTimer.ElapsedMilliseconds;
										_sendIntervals.Add(interval);
										_sendIntervalTimer.Restart();

										if (_sendIntervals.Count > 10)
										{
											double avg = _sendIntervals.Average();
											toolClass.SaveLog($"平均发送间隔: {avg:F0}ms");

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
													zhmTest++;
													resultBool1.Text = result1.ToString();
													resultBool2.Text = result2.ToString();
													resultBool3.Text = result3.ToString();
													resultBool1.ForeColor = result1 ? Color.Green : Color.Red;
													resultBool2.ForeColor = result2 ? Color.Green : Color.Red;
													resultBool3.ForeColor = result3 ? Color.Green : Color.Red;
													WriteCountTxt.Text = writeNGCount.ToString();
												}
												catch (Exception uiEx) { toolClass.SaveLog($"更新UI异常: {uiEx.Message}"); }
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
									}
									else
									{
										retryCount++;
										toolClass.SaveLog($"写入PLC失败，重试 {retryCount}/{MAX_RETRY}");
										Thread.Sleep(1);
									}
								}
								catch (Exception writeEx)
								{
									retryCount++;
									toolClass.SaveLog($"写入PLC异常，重试 {retryCount}/{MAX_RETRY}: {writeEx.Message}");
									Thread.Sleep(1);
								}
							}

							if (!writeSuccess)
							{
								toolClass.SaveLog($"写入PLC失败，已达到最大重试次数 {MAX_RETRY}");
							}
						}
						else
						{
							Thread.Sleep(1);
						}
					}
					catch (Exception ex)
					{
						toolClass.SaveLog($"写入结果线程异常: {ex.Message}");
						Thread.Sleep(1000);
					}
				}
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"写入结果线程严重异常: {ex.Message}\n{ex.StackTrace}");
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
							toolClass.SaveLog($"队列积压: {totalQueue}");
						}
					}
				}
			}
			catch (Exception ex)
			{
				if (!_isClosing)
					toolClass.SaveLog($"更新线程异常: {ex.Message}");
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
				this.Close();
				System.Windows.Forms.Application.Exit();
			}
		}
		#endregion

		public void Vision_ChangeMode(string spec)
		{
			try
			{
				this.Invoke(new Action(() =>
				{
					toolClass.SaveLog("切换事件触发了 spec: " + spec);
					versionNum.Text = _Config.CurCheckSpec.ToString() != "" ? _Config.CurCheckSpec.ToString() : " - ";
					Task.Run(() =>
					{
						if (!_isClosing)
						{
							toolClass.SaveLog($"切换型号时，轴1自动移动至拍照位。Position：{_Config.zhengPosition}");
							myZmcaux.GoPosition(g_handle, 1, Convert.ToSingle(_Config.zhengPosition));
							toolClass.SaveLog($"轴1自动移动至拍照位完成，当前轴1位置：{myZmcaux.GetLocation(g_handle, 1)}\r\n");

							toolClass.SaveLog($"切换型号时，轴0自动移动至拍照位。Position：{_Config.fanPosition}");
							myZmcaux.GoPosition(g_handle, 0, Convert.ToSingle(_Config.fanPosition));
							toolClass.SaveLog($"轴0自动移动至拍照位完成，当前轴0位置：{myZmcaux.GetLocation(g_handle, 0)}");

							MessageBox.Show("切换型号完成，已将轴移动至对应拍照位！");
						}
					});
				}));
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"系统初始化时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
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
				StartMethod(CameraSelect.Camera1);
				StartMethod(CameraSelect.Camera2);
				StartMethod(CameraSelect.Camera3);
				StartMethod(CameraSelect.Camera4);
				StartMethod(CameraSelect.Camera5);

				OpenFileDialog dialog = new OpenFileDialog();
				dialog.Title = "请选择图片文件夹";
				dialog.Multiselect = true;
				dialog.Filter = "图片文件(*.jpg;*.png;*.bmp)|*.jpg;*.png;*.bmp";
				if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
				{
					if (dialog.FileNames.Length == 5)
					{
						imagePath1 = dialog.FileNames[0];
						imagePath2 = dialog.FileNames[1];
						imagePath3 = dialog.FileNames[2];
						imagePath4 = dialog.FileNames[3];
						imagePath5 = dialog.FileNames[4];

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
					else
					{
						MessageBox.Show("数量不等5");
						return;
					}
				}
				return;
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"error... {ex.Message}\r\n{ex.StackTrace}");
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
				xlPictureBox1.Invoke(new Action(() => UpdatePictureBoxInternal(xlPictureBox1, image)));
			else
				UpdatePictureBoxInternal(xlPictureBox1, image);
		}

		private void UpdatePictureBoxInternal(PictureBox pictureBox, Bitmap image)
		{
			if (_isClosing) return;
			try
			{
				var oldImage = pictureBox.Image;
				if (oldImage != null && oldImage != image) { try { oldImage.Dispose(); } catch { } }
				pictureBox.Image = (Bitmap)image.Clone();
			}
			catch { }
		}

		private void UpdatePictureBox2(Bitmap image)
		{
			if (xlPictureBox2.InvokeRequired)
				xlPictureBox2.Invoke(new Action(() => UpdatePictureBoxInternal2(xlPictureBox2, image)));
			else
				UpdatePictureBoxInternal2(xlPictureBox2, image);
		}

		private void UpdatePictureBoxInternal2(PictureBox pictureBox, Bitmap image)
		{
			if (_isClosing) return;
			try
			{
				var oldImage = pictureBox.Image;
				if (oldImage != null && oldImage != image) { try { oldImage.Dispose(); } catch { } }
				pictureBox.Image = (Bitmap)image.Clone();
			}
			catch { }
		}

		private void UpdatePictureBox3(Bitmap image)
		{
			if (xlPictureBox3.InvokeRequired)
				xlPictureBox3.Invoke(new Action(() => UpdatePictureBoxInternal3(xlPictureBox3, image)));
			else
				UpdatePictureBoxInternal3(xlPictureBox3, image);
		}

		private void UpdatePictureBoxInternal3(PictureBox pictureBox, Bitmap image)
		{
			if (_isClosing) return;
			try
			{
				var oldImage = pictureBox.Image;
				if (oldImage != null && oldImage != image) { try { oldImage.Dispose(); } catch { } }
				pictureBox.Image = (Bitmap)image.Clone();
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
				xlPictureBox4.Invoke(new Action(() => UpdatePictureBoxInternal4(xlPictureBox4, image)));
			else
				UpdatePictureBoxInternal4(xlPictureBox4, image);
		}

		private void UpdatePictureBoxInternal4(PictureBox pictureBox, Bitmap image)
		{
			if (_isClosing) return;
			try
			{
				var oldImage = pictureBox.Image;
				if (oldImage != null && oldImage != image) { try { oldImage.Dispose(); } catch { } }
				pictureBox.Image = (Bitmap)image.Clone();
			}
			catch { }
		}

		private void UpdatePictureBox5(Bitmap image)
		{
			if (xlPictureBox5.InvokeRequired)
				xlPictureBox5.Invoke(new Action(() => UpdatePictureBoxInternal5(xlPictureBox5, image)));
			else
				UpdatePictureBoxInternal5(xlPictureBox5, image);
		}

		private void UpdatePictureBoxInternal5(PictureBox pictureBox, Bitmap image)
		{
			if (_isClosing) return;
			try
			{
				var oldImage = pictureBox.Image;
				if (oldImage != null && oldImage != image) { try { oldImage.Dispose(); } catch { } }
				pictureBox.Image = (Bitmap)image.Clone();
			}
			catch { }
		}
		#endregion
	}

	#region 原有辅助类（保持兼容）
	public class QueueResultItem : IDisposable
	{
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

		// 是否纯爆管（用于连续异常判定）
		public bool IsPureBurst { get; set; }

		public QueueResultItem() { StageTimes = new Dictionary<string, long>(); }

		public void Dispose()
		{
			ImageData_Y = null;
			ImageData_R = null;
			StageTimes?.Clear();
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
		public double AverageTimeMs => ProcessCount > 0 ? (double)TotalTimeMs / ProcessCount : 0;

		public void Reset()
		{
			TotalTimeMs = 0; MaxTimeMs = long.MinValue; MinTimeMs = long.MaxValue; ProcessCount = 0; StageTimes?.Clear();
		}

		public void AddTime(long elapsedMs)
		{
			TotalTimeMs += elapsedMs; MaxTimeMs = Math.Max(MaxTimeMs, elapsedMs); MinTimeMs = Math.Min(MinTimeMs, elapsedMs); ProcessCount++;
		}

		public void AddStageTime(string stageName, long elapsedMs)
		{
			if (StageTimes == null) StageTimes = new Dictionary<string, long>();
			if (StageTimes.ContainsKey(stageName)) StageTimes[stageName] += elapsedMs;
			else StageTimes[stageName] = elapsedMs;
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
		private readonly SortedDictionary<long, T> _items = new SortedDictionary<long, T>();
		private readonly object _lock = new object();
		private readonly int _maxSize;
		private readonly string _queueName;
		private long _expectedSequence = 1;
		private bool _disposed = false;
		XLToolClass toolClass = new XLToolClass();

		public int Count { get { lock (_lock) return _items.Count; } }

		public OrderedQueueManager(int maxSize, string queueName)
		{
			_maxSize = maxSize;
			_queueName = queueName ?? "未命名队列";
		}

		public bool Enqueue(long sequenceId, T item)
		{
			if (_disposed || item == null) return false;
			lock (_lock)
			{
				if (_items.Count >= _maxSize)
				{
					var firstKey = _items.Keys.First();
					TryDisposeItem(_items[firstKey]);
					_items.Remove(firstKey);
					toolClass.SaveLog($"{_queueName} 队列达到上限，移除最旧项 SequenceId={firstKey}");
				}
				_items[sequenceId] = item;
				return true;
			}
		}

		public T DequeueExpected()
		{
			if (_disposed) return null;
			lock (_lock)
			{
				if (_items.TryGetValue(_expectedSequence, out T item))
				{
					_items.Remove(_expectedSequence);
					_expectedSequence++;
					return item;
				}
				return null;
			}
		}

		public T PeekExpected()
		{
			if (_disposed) return null;
			lock (_lock)
			{
				if (_items.TryGetValue(_expectedSequence, out T item)) return item;
				return null;
			}
		}

		public T DequeueOldest()
		{
			if (_disposed) return null;
			lock (_lock)
			{
				if (_items.Count == 0) return null;
				var firstKey = _items.Keys.First();
				var item = _items[firstKey];
				_items.Remove(firstKey);
				return item;
			}
		}

		public void Clear()
		{
			lock (_lock)
			{
				foreach (var item in _items.Values) TryDisposeItem(item);
				_items.Clear();
				_expectedSequence = 1;
			}
		}

		public void ResetExpectedSequence(long newSequence)
		{
			lock (_lock) { _expectedSequence = newSequence; }
		}

		private void TryDisposeItem(T item)
		{
			try { if (item is IDisposable disposable) disposable.Dispose(); }
			catch { }
		}

		public IEnumerable<T> GetAllItems()
		{
			lock (_lock)
			{
				return _items.Values.ToList();
			}
		}

		public void Dispose()
		{
			if (!_disposed) { _disposed = true; Clear(); }
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
		XLToolClass toolClass = new XLToolClass();
		public int ImageQueueCount => _imageQueue.Count;
		public int ResultQueueCount => _resultQueue.Count;
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
					ProcessNextImage();
				}
			}
			catch (ThreadAbortException) { }
			catch (Exception ex)
			{
				toolClass.SaveLog($"{_cameraName} 处理器异常: {ex.Message}");
			}
		}

		private void ProcessNextImage()
		{
			if (_isProcessing) return;
			_isProcessing = true;

			try
			{
				var context = _imageQueue.DequeueExpected();
				if (context == null) return;

				context.StartProcessTime = DateTime.Now;

				try
				{
					_processAction(context);
					if (context.Result != null)
						_resultQueue.Enqueue(context.SequenceId, context.Result);
					else
					{
						var queueResult = new QueueResultItem
						{
							SequenceId = context.SequenceId,
							Offset = context.Offset,
							Result = context.ProcessResult,
							Timestamp = DateTime.Now
						};
						_resultQueue.Enqueue(context.SequenceId, queueResult);
					}
				}
				catch (Exception ex)
				{
					toolClass.SaveLog($"{_cameraName} 处理异常: {ex.Message}");
					var errorResult = new QueueResultItem
					{
						SequenceId = context.SequenceId,
						Offset = context.Offset,
						Result = context.ProcessResult,
						Timestamp = DateTime.Now
					};
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
						StringBuilder stringBuilder = new StringBuilder();
						stringBuilder.Append($"\r\nCamera: {context.Camera}\r\nID:{context.SequenceId - context.Offset}\r\n总耗时: {elapsedMs}ms\r\n");
						foreach (var stage in context.StageTimes)
						{
							_performanceStats.AddStageTime(stage.Key, stage.Value);
							stringBuilder.Append($"{stage.Key}: {stage.Value}\r\n");
						}
						toolClass.SaveLog(stringBuilder.ToString());

						// 每50帧输出性能汇总
						if (_performanceStats.ProcessCount > 0 && _performanceStats.ProcessCount % 50 == 0)
						{
							toolClass.SaveLog($"[性能] {_cameraName} 累计{_performanceStats.ProcessCount}帧 Avg={_performanceStats.AverageTimeMs:F0}ms Min={_performanceStats.MinTimeMs}ms Max={_performanceStats.MaxTimeMs}ms");
						}
					}

					context.OriginalBitmap?.Dispose();
				}
			}
			finally
			{
				_isProcessing = false;
			}
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
			var allItems = _resultQueue.GetAllItems();
			foreach (var result in allItems)
			{
				long currentId = result.SequenceId - result.Offset;
				if (currentId == targetSequenceId) return result;
				else if (currentId > targetSequenceId) return null;
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
		private readonly Thread _matchingThread;
		private readonly AutoResetEvent _matchSignal;
		private readonly Action<QueueResultItem[]> _matchCallback;
		private readonly int _requiredCameras;
		private bool _disposed = false;
		XLToolClass toolClass = new XLToolClass();

		public ResultMatcher(ImageProcessor[] processors, Action<QueueResultItem[]> matchCallback)
		{
			_processors = processors ?? throw new ArgumentNullException(nameof(processors));
			_matchCallback = matchCallback ?? throw new ArgumentNullException(nameof(matchCallback));
			_requiredCameras = processors.Length;
			_matchSignal = new AutoResetEvent(false);

			_matchingThread = new Thread(MatchingWorker)
			{
				Name = "ResultMatcher",
				IsBackground = true,
				Priority = ThreadPriority.Highest
			};
			_matchingThread.Start();
		}

		private void MatchingWorker()
		{
			try
			{
				while (!_disposed)
				{
					bool allHaveData = true;
					for (int i = 0; i < _processors.Length; i++)
					{
						if (_processors[i].PeekNextResult() == null)
						{
							allHaveData = false;
							break;
						}
					}

					if (allHaveData) PerformMatching();
					else _matchSignal.WaitOne(1);
				}
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"结果匹配器异常: {ex.Message}");
			}
		}

		private void PerformMatching()
		{
			try
			{
				var camera1Result = _processors[0].PeekNextResult();
				if (camera1Result == null) return;

				long targetSequenceId = camera1Result.SequenceId - camera1Result.Offset;
				var matchedResults = new QueueResultItem[_processors.Length];
				matchedResults[0] = camera1Result;
				bool allMatch = true;

				for (int i = 1; i < _processors.Length; i++)
				{
					var matchedResult = _processors[i].FindResultById(targetSequenceId);
					if (matchedResult != null) matchedResults[i] = matchedResult;
					else { allMatch = false; break; }
				}

				if (allMatch)
				{
					for (int i = 0; i < _processors.Length; i++)
					{
						if (i == 0) _processors[i].GetNextResult();
						else
						{
							QueueResultItem current;
							while ((current = _processors[i].PeekNextResult()) != null)
							{
								long currentId = current.SequenceId - current.Offset;
								if (currentId == targetSequenceId)
								{
									_processors[i].GetNextResult();
									break;
								}
								else if (currentId < targetSequenceId) _processors[i].GetNextResult();
								else break;
							}
						}
					}

					_matchCallback?.Invoke(matchedResults);
					_matchSignal.Set();
				}
				else
				{
					// 检查是否有相机的 productId 已经超过 target（等待也没用，丢弃）
					bool anyPast = false;
					for (int i = 1; i < _processors.Length; i++)
					{
						var peek = _processors[i].PeekNextResult();
						if (peek != null && (peek.SequenceId - peek.Offset) > targetSequenceId)
						{ anyPast = true; break; }
					}

					if (anyPast)
					{
						toolClass.SaveLog($"[ResultMatch] ID:{targetSequenceId} 匹配失败，跳过Camera1");
						_processors[0].GetNextResult();
						for (int i = 1; i < _processors.Length; i++)
						{
							QueueResultItem current;
							while ((current = _processors[i].PeekNextResult()) != null)
							{
								long currentId = current.SequenceId - current.Offset;
								if (currentId < targetSequenceId)
									_processors[i].GetNextResult();
								else
									break;
							}
						}
					}
					// else: 后置相机还没追上，跳过本次匹配，等待更多结果
				}
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"匹配过程异常: {ex.Message}");
			}
		}

		public void SignalNewResult() => _matchSignal.Set();

		public void Dispose()
		{
			if (!_disposed)
			{
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
	}

	#region 高性能图像保存方案
	public class HighSpeedImageSaver : IDisposable
	{
		private readonly BlockingCollection<SaveTask> _saveQueue;
		private readonly Thread[] _workerThreads;
		private bool _disposed = false;
		private readonly string _saverName;
		XLToolClass toolClass = new XLToolClass();

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
					toolClass.SaveLog($"{_saverName} 队列已满，丢弃任务: {discardedTask.FilePath}");
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
					catch (Exception ex) { toolClass.SaveLog($"{_saverName} 保存失败: {ex.Message}"); }
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
				if (delay > 100) toolClass.SaveLog($"{_saverName} 保存延迟较高: {delay:F1}ms");
			}
			catch (Exception ex) { toolClass.SaveLog($"{_saverName} 文件写入失败: {ex.Message}"); }
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

		/// <summary>
		/// 【修复编译版】直接使用 OpenCV 原生 C++ 算头进行 Jpeg 压缩
		/// 严格适配 out 关键字，零托管内存开辟，防止高速存图时由于 MemoryStream 导致 GC 卡顿
		/// </summary>
		public static byte[] ToJpegBytesViaOpenCv(Mat mat, int quality = 85)
		{
			if (mat == null || mat.Empty()) return null;
			try
			{
				int[] encodeParams = new int[] { (int)ImwriteFlags.JpegQuality, quality };

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
		XLToolClass toolClass = new XLToolClass();

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
				catch (Exception ex) { toolClass.SaveLog($"{PoolName} 初始化失败: {ex.Message}"); }
			}
			toolClass.SaveLog($"{PoolName} 初始化完成: {_initialCapacity}个Bitmap和Mat已预分配");
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
				bitmap.Dispose();
				Interlocked.Add(ref _totalAllocatedMemory, -EstimateBitmapSize(bitmap));
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
				catch (Exception ex) { toolClass.SaveLog($"{PoolName} 监控异常: {ex.Message}"); }
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
			Clear();
			toolClass.SaveLog($"{PoolName} 已释放");
		}

		~ImageBufferPool() { Dispose(); }
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
			XLToolClass toolClass = new XLToolClass();

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
			catch (Exception ex) { toolClass.SaveLog($"FastCopyTo失败: {ex.Message}"); }
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