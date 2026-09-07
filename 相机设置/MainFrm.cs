using CommonLib;
using Littleluck.Class;
using MT.Camera.SDK;
using PLC调试.Class;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using XL.Tool;
using static CommonLib.Class_Config;


namespace SetCamera
{
	public partial class MainFrm : Form
	{
		public MainFrm()
		{
			InitializeComponent();
		}

		public MainFrm(IntPtr handle, HCModbusClass modbusClass1)
		{
			InitializeComponent();
			g_handle = handle;
			_modbusHc = modbusClass1;
			_modbusType = 1;
			_plc = null; // HCModbusClass 不实现 IPlcCommunication，直连构造无互锁（产线走接口构造）
		}
		public MainFrm(IntPtr handle, S7_1200Class s7Class)
		{
			InitializeComponent();
			g_handle = handle;
			_modbusS7 = s7Class;
			_modbusType = 2;
			_plc = s7Class;
		}
		/// <summary>统一接口构造函数（兼容 IPlcCommunication）</summary>
		public MainFrm(IntPtr handle, IPlcCommunication plc)
		{
			InitializeComponent();
			g_handle = handle;
			_plc = plc;
			if (plc is S7_1200Class s7)
			{
				_modbusS7 = s7;
				_modbusType = 2;
			}
			else if (plc is HCModbusAdapter adapter)
			{
				_modbusHc = adapter.Inner;
				_modbusType = 1;
			}
			else
			{
				// 兜底：默认 HCModbus
				_modbusType = 1;
			}
		}
		static IntPtr g_handle;
		takephotoVm myZmcaux = new takephotoVm();
		private int axis = 0;           // 轴号
	private int _axisDir = 1;      // 轴方向：1=正向, -1=反向（从INI读取）

		public DaHuaSDK cam1, cam2, cam3, cam4, cam5;
		XLToolClass toolClass = new XLToolClass();
		HCModbusClass _modbusHc;
		S7_1200Class _modbusS7;
		int _modbusType = 0; // 0=none, 1=HCModbus, 2=S7_1200
		IPlcCommunication _plc;                     // 统一接口引用（互锁 + 自动关闭事件订阅）
		private volatile int _realtimeBitIdx = -1;  // 当前实时回报位索引: 0=DBX72.0 1=DBX72.1 -1=未回报

		// 工位启用状态（与 MainFrm 联动）
		private bool[] _cameraEnabled = { true, true, true, true, true };
		private int _lastValidCamIndex = 0;

		// 实时显示最新帧槽（见 Cam1_OnImage）：SDK 回调线程克隆入槽，UI 线程 BeginInvoke 消费，
		// 取代旧实现"回调线程同步 Invoke 换图"——既阻塞 20Hz 取流回调，又可能在关窗时撞句柄销毁
		private Bitmap _latestFrame;
		private int _consumePending;          // 0/1：消费委托在途标志（BeginInvoke 队列最多积压一条）
		private long _droppedFrameCount;      // 消费不及时被覆盖丢弃的帧数（每100帧记一次日志，诊断用）
		private Task _triggerLoopTask;        // 实时/单张触发循环任务（停止时等待收尾，防后台收尾写竞争）

		/// <summary>
		/// 指向当前选中得相机
		/// </summary>
		DaHuaSDK daHuaSDK = new DaHuaSDK();

		Thread updeteThread;

		//int cam1TriggerPath = 4;
		//int cam2TriggerPath = 5;
		//int cam3TriggerPath = 5;
		//int cam4TriggerPath = 5;
		//int cam5TriggerPath = 5;
		//int tempTriggerPath = 0;

		string cam1TriggerPath = "MX7080.4";
		string cam2TriggerPath = "MX7080.4";
		string cam3TriggerPath = "MX7080.4";
		string cam4TriggerPath = "MX7080.4";
		string cam5TriggerPath = "MX7080.4";
		string tempTriggerPath = "MX7080.4";

		#region 页面事件
		private void MainFrm_Load(object sender, EventArgs e)
		{
			try
			{
				// 订阅设备模式变化（自动模式下自动停止实时并关闭本窗体）
				this.FormClosed += MainFrm_FormClosed;
				if (_plc != null) _plc.EventDeviceMode += OnDeviceModeFromPlc;

				// 读取工位启用状态（与 MainFrm 联动）
				_cameraEnabled[0] = _Config.ActiveCam1;
				_cameraEnabled[1] = _Config.ActiveCam2;
				_cameraEnabled[2] = _Config.ActiveCam3;
				_cameraEnabled[3] = _Config.ActiveCam4;
				_cameraEnabled[4] = _Config.ActiveCam5;

				updeteThread = new Thread(UpdateLocation);
				updeteThread.IsBackground = true;
				updeteThread.Start();

				leftLb.Text = _Config.zhengPosition.ToString("F2");
				rightLb.Text = _Config.fanPosition.ToString("F2");
				roundLb.Text = _Config.roundPosition.ToString("F2");

				xlPictureBox1.ISRealTimeDisplay = true;
				//cam1 = GlobalVar.CameraSdk1;
				//cam1.OnImage += Cam1_OnImage;
				//cam2.OnImage += Cam1_OnImage;
				//cam3.OnImage += Cam1_OnImage;
				//cam4.OnImage += Cam1_OnImage;
				//cam5.OnImage += Cam1_OnImage;

				// 默认选中第一个启用的相机
				int firstEnabled = 0;
				for (int i = 0; i < 5; i++) { if (_cameraEnabled[i]) { firstEnabled = i; break; } }
				_lastValidCamIndex = firstEnabled;
				uiComboBox_cam.SelectedIndex = firstEnabled;
				uiComboBox_axis.SelectedIndex = 0;
				axis = 1; // Combo第一项=轴1(正面拍照位)，必须在ReadAxisDirection前手动设置
				// 强制读取初始轴方向（SelectedIndex=0时SelectedIndexChanged可能不触发）
				ReadAxisDirection();

				cam1TriggerPath = _Config.Output_Camera1;
				cam2TriggerPath = _Config.Output_Camera2;
				cam3TriggerPath = _Config.Output_Camera3;
				cam4TriggerPath = _Config.Output_Camera4;
				cam5TriggerPath = _Config.Output_Camera5;



				uiComboBox1_SelectedIndexChanged(null, null);
				uiComboBox_axis_SelectedIndexChanged(null, null);

			}
			catch (Exception ex)
			{
				FastLogger.Instance.Info($"加载相机信息错误！！！ \r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}


		private void MainFrm_FormClosing(object sender, FormClosingEventArgs e)
		{
			try
			{
				if (uiButton4.Text == "停止实时")
				{
					MessageBox.Show("请先停止实时取像模式！", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Stop);
					e.Cancel = true;
					return;   // 实时进行中：拒绝关窗且绝不擅自停循环（停止只走用户按钮/自动模式路径）
				}
				// 兜底：关窗前确保触发循环（单张/收尾中）已停止收尾（复位触发位/回报位），
				// 防止关窗后后台收尾写与回报位复位竞争、回报位残留 TRUE
				StopTriggerLoopAndWait();
				if (cam1 != null) if(cam1!=null) cam1.OnImage -= Cam1_OnImage;
				if (cam2 != null) if(cam2!=null) cam2.OnImage -= Cam1_OnImage;
				if (cam3 != null) if(cam3!=null) cam3.OnImage -= Cam1_OnImage;
				if (cam4 != null) if(cam4!=null) cam4.OnImage -= Cam1_OnImage;
				if (cam5 != null) if(cam5!=null) cam5.OnImage -= Cam1_OnImage;
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Info($"程序错误！！！ \r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}
		/// <summary>
		/// 单张取像
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void uiButton3_Click(object sender, EventArgs e)
		{
			try
			{
				TriggerCameraMethod(true);
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return;
			}
		}

		/// <summary>
		/// 实时取像
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void uiButton4_Click(object sender, EventArgs e)
		{
			if (uiButton4.Text.Equals("实时取像"))
			{
				// 【互锁】自动模式下禁止打开实时取像
				if (_plc != null && _plc.IsAutoMode)
				{
					MessageBox.Show("设备处于自动模式，禁止实时取像！", "系统提示",
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
					try { FastLogger.Instance.Info("【设备状态】自动模式下点击实时取像被拦截"); } catch { }
					return;
				}
				uiButton4.Text = "停止实时";

				uiButton3.Enabled = false;
				//xlPictureBox1.ISRealTimeDisplay = true;
				TriggerCameraMethod(false);
				//daHuaSDK.SetTriggerMode(0);
				uiComboBox_cam.Enabled = false;
				uiComboBox_axis.Enabled = false;
			}
			else
			{
				uiButton4.Text = "实时取像";

				uiButton3.Enabled = true;
				//daHuaSDK.SetTriggerMode(1);
				//xlPictureBox1.ISRealTimeDisplay = false;
				StopTriggerLoopAndWait();   // 停止循环并等收尾（触发位复位+回报位复位完成后再放行）
				uiComboBox_cam.Enabled = true;
				uiComboBox_axis.Enabled = true;
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void saveImageBtn_Click(object sender, EventArgs e)
		{
			try
			{
				if (this.xlPictureBox1.Image == null)
				{
					return;
				}
				SaveFileDialog dlg = new SaveFileDialog();
				dlg.Filter = "Bmp 图片|*.bmp";
				dlg.FilterIndex = 0;
				dlg.RestoreDirectory = true;//保存对话框是否记忆上次打开的目录
				dlg.CheckPathExists = true;//
				if (dlg.ShowDialog() == DialogResult.OK)
					this.xlPictureBox1.Image.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Bmp);
				MessageBox.Show("保存图片完成", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{

				FastLogger.Instance.Info($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}
	/// <summary>从 setup.ini [motion] 节读取当前轴的方向: axis{N}_dir, 1=正向 -1=反向</summary>
	private void ReadAxisDirection()
	{
		try
		{
			// 使用与主程序 Class_Config 一致的路径，避免 StartupPath 与工作目录不一致
			string iniPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "setup.ini");
			if (!System.IO.File.Exists(iniPath))
			{
				_axisDir = 1;
				FastLogger.Instance.Info($"[轴方向] setup.ini 不存在({iniPath})，轴{axis} 方向使用默认值: 正向");
				return;
			}

			string key = "axis" + axis + "_dir";
			bool found = false;
			foreach (var line in System.IO.File.ReadAllLines(iniPath))
			{
				string t = line.Trim();
				if (t.StartsWith("[") || !t.Contains("=")) continue;
				int eq = t.IndexOf('=');
				if (t.Substring(0, eq).Trim() == key)
				{
					// 去除行尾分号注释，避免 "; 轴1方向" 干扰 int.TryParse
					string rawVal = t.Substring(eq + 1).Trim();
					int semiIdx = rawVal.IndexOf(';');
					if (semiIdx >= 0) rawVal = rawVal.Substring(0, semiIdx).Trim();
					if (!int.TryParse(rawVal, out _axisDir))
						_axisDir = 1;
					found = true;
					break;
				}
			}
			string dirText = _axisDir == 1 ? "正向" : "反向";
			if (found)
				FastLogger.Instance.Info($"[轴方向] 轴{axis} 方向配置已加载: {key}={_axisDir} ({dirText})");
			else
				FastLogger.Instance.Info($"[轴方向] 轴{axis} 方向未配置({key})，使用默认值: 正向");
		}
		catch { _axisDir = 1; }
		if (_axisDir != 1 && _axisDir != -1) _axisDir = 1;
	}


		private void gainTxt_KeyDown(object sender, KeyEventArgs e)
		{
			try
			{
				if (e.KeyCode == Keys.Enter)
				{
					daHuaSDK.SetGainRaw(double.Parse(gainTxt.Text));
					uiComboBox1_SelectedIndexChanged(null, null);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				FastLogger.Instance.Info($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return;
			}
		}


		private void exposureTxt_KeyDown(object sender, KeyEventArgs e)
		{
			try
			{
				if (e.KeyCode == Keys.Enter)
				{
					daHuaSDK.SetExposureTime(double.Parse(exposureTxt.Text));
					uiComboBox1_SelectedIndexChanged(null, null);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				FastLogger.Instance.Info($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return;
			}
		}


		#endregion

		#region 相机回调事件
		private void Cam1_OnImage(Bitmap bitmap, string cameraName, string cameraKey)
		{
			// 只显示当前选中相机的图片
			if (daHuaSDK == null || daHuaSDK.curCameraKey != cameraKey)
				return;
			if (bitmap == null) return;

			try
			{
				// 【修复】SDK回调的bitmap与主检测流水线共用图像缓冲，必须克隆后显示，
				// 绝不直接引用（自动模式下会被流水线回收/复用，控件拿失效GDI+句柄抛ArgumentException）。
				// 克隆在回调线程完成，不再同步Invoke阻塞取流线程。
				Bitmap clone;
				try { clone = new Bitmap(bitmap); }
				catch { return; }   // 源图已失效时跳过本帧，不污染控件

				// 入槽：换出的旧克隆（UI线程还没消费）直接丢弃——~20Hz下只显最新帧
				var replaced = Interlocked.Exchange(ref _latestFrame, clone);
				if (replaced != null)
				{
					_droppedFrameCount++;
					if (_droppedFrameCount % 100 == 0)
						try { FastLogger.Instance.Info($"[实时显示] 已丢帧 {_droppedFrameCount} 帧（只显示最新帧，属正常现象）"); } catch { }
					try { replaced.Dispose(); } catch { }
				}

				// 0/1标志：消费委托最多在途一条，避免BeginInvoke队列被20Hz打爆
				if (Interlocked.Exchange(ref _consumePending, 1) == 0)
				{
					try
					{
						if (this.IsDisposed || !this.IsHandleCreated) { _consumePending = 0; return; }
						this.BeginInvoke(new Action(ConsumeLatestFrame));
					}
					catch
					{
						Interlocked.Exchange(ref _consumePending, 0);   // 窗体已关闭：复位标志，不再投递
					}
				}
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Info($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		/// <summary>
		/// UI线程消费最新帧：换入控件并释放控件上一帧（只释放自己的克隆，绝不Dispose SDK传入的图）。
		/// 先复位在途标志再取帧——取帧与下次投递之间的新帧会再触发一轮消费，队列最多积压一条。
		/// </summary>
		private void ConsumeLatestFrame()
		{
			_consumePending = 0;
			if (this.IsDisposed) return;
			try
			{
				var frame = Interlocked.Exchange(ref _latestFrame, null);
				if (frame == null) return;
				var old = this.xlPictureBox1.Image;
				this.xlPictureBox1.Image = frame;
				if (old != null)
				{ try { old.Dispose(); } catch { } }
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Info($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		#endregion

		private void uiComboBox_axis_SelectedIndexChanged(object sender, EventArgs e)
		{
			try
			{
				switch (uiComboBox_axis.SelectedIndex + 1)
				{
					case 1:
						axis = 1;
						uiComboBox_cam.SelectedIndex = 3;
						break;
					case 2:
						axis = 0;
						uiComboBox_cam.SelectedIndex = 4;
						break;
					case 3:
						axis = 2;
						uiComboBox_cam.SelectedIndex = 2;
						break;
					default:
						break;
				}
				ReadAxisDirection();
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Info($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}


		private void uiComboBox1_SelectedIndexChanged(object sender, EventArgs e)
		{
			try
			{
				int selectedIndex = uiComboBox_cam.SelectedIndex;
				// 禁用工位不可选中
				if (selectedIndex >= 0 && selectedIndex < 5 && !_cameraEnabled[selectedIndex])
				{
					MessageBox.Show($"相机{selectedIndex + 1}工位未启用（ActiveCam{selectedIndex + 1}=False），无法选中！",
						"工位已禁用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					uiComboBox_cam.SelectedIndex = _lastValidCamIndex;
					return;
				}
				_lastValidCamIndex = selectedIndex;

				switch (selectedIndex + 1)
				{
					case 1:
						tempTriggerPath = cam1TriggerPath;
						UnsubscribeAllCameras();   // 幂等：先退订全部（含自身），防重复订阅叠加
						if (cam1 == null) { FastLogger.Instance.Warn("相机一未初始化，无法订阅图像"); return; }
						cam1.OnImage += Cam1_OnImage;
						daHuaSDK = cam1;
						FastLogger.Instance.Info($"切换为相机一：tempTriggerPath: {tempTriggerPath} ------------------------------------------------------------------------");



						break;
					case 2:
						tempTriggerPath = cam2TriggerPath;
						UnsubscribeAllCameras();
						if (cam2 == null) { FastLogger.Instance.Warn("相机二未初始化，无法订阅图像"); return; }
						cam2.OnImage += Cam1_OnImage;
						daHuaSDK = cam2;
						FastLogger.Instance.Info($"切换为相机二：tempTriggerPath: {tempTriggerPath}	------------------------------------------------------------------------");
						break;
					case 3:
						tempTriggerPath = cam3TriggerPath;
						UnsubscribeAllCameras();
						if (cam3 == null) { FastLogger.Instance.Warn("相机三未初始化，无法订阅图像"); return; }
						cam3.OnImage += Cam1_OnImage;
						daHuaSDK = cam3;

						uiComboBox_axis.SelectedIndex = 2;

						FastLogger.Instance.Info($"切换为相机三：tempTriggerPath: {tempTriggerPath} ------------------------------------------------------------------------");
						break;
					case 4:
						tempTriggerPath = cam4TriggerPath;
						UnsubscribeAllCameras();
						if (cam4 == null) { FastLogger.Instance.Warn("相机四未初始化，无法订阅图像"); return; }
						cam4.OnImage += Cam1_OnImage;
						daHuaSDK = cam4;
						uiComboBox_axis.SelectedIndex = 0;
						FastLogger.Instance.Info($"切换为相机四：tempTriggerPath: {tempTriggerPath}------------------------------------------------------------------------");
						break;
					case 5:
						tempTriggerPath = cam5TriggerPath;
						UnsubscribeAllCameras();
						if (cam5 == null) { FastLogger.Instance.Warn("相机五未初始化，无法订阅图像"); return; }
						cam5.OnImage += Cam1_OnImage;
						daHuaSDK = cam5;
						uiComboBox_axis.SelectedIndex = 1;
						FastLogger.Instance.Info($"切换为相机五：tempTriggerPath: {tempTriggerPath}------------------------------------------------------------------------");
						break;
					default:
						break;

				}
				cameraSNLb.Text = daHuaSDK.curCameraKey;
				exposureLb.Text = daHuaSDK.GetExposureTime().ToString();
				gainLb.Text = daHuaSDK.GetGainRaw().ToString();
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Info($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}

		}


		/// <summary>
		/// 幂等退订全部相机（含自身）。订阅入口被 Load、切轴回调、增益/曝光回车等多处重复触发，
		/// 先退订自身再订阅可杜绝重复订阅叠加（同一帧被回调多次 → 重复克隆、显示异常）。
		/// </summary>
		private void UnsubscribeAllCameras()
		{
			if (cam1 != null) cam1.OnImage -= Cam1_OnImage;
			if (cam2 != null) cam2.OnImage -= Cam1_OnImage;
			if (cam3 != null) cam3.OnImage -= Cam1_OnImage;
			if (cam4 != null) cam4.OnImage -= Cam1_OnImage;
			if (cam5 != null) cam5.OnImage -= Cam1_OnImage;
		}

		#region 运动控制部分
		private void goBtn_Click(object sender, EventArgs e)
		{
			try
			{
				Task.Run(() =>
				{
					FastLogger.Instance.Info("goBtn，point：" + point_Txt.Text);
					float x = Convert.ToSingle(point_Txt.Text);

					this.Invoke(new Action(() =>
					{
						goBtn.Enabled = false;
						leftBtn.Enabled = false;
						rightBtn.Enabled = false;

						myZmcaux.GoPosition(g_handle, axis, x);

						goBtn.Enabled = true;
						leftBtn.Enabled = true;
						rightBtn.Enabled = true;
					}));
					FastLogger.Instance.Info("goBtn，完成：" + myZmcaux.GetLocation(g_handle, axis));

				});

			}
			catch (Exception ex)
			{
				FastLogger.Instance.Info($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void stopBtn_Click(object sender, EventArgs e)
		{
			try
			{
				//myZmcaux.StopMethod(g_handle);
				myZmcaux.StopMove(g_handle, axis);
				FastLogger.Instance.Info($"停止成功");
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Info($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void leftBtn_MouseDown(object sender, MouseEventArgs e)
		{
			try
			{
				myZmcaux.Vmove(g_handle, axis, -1 * _axisDir);
				//FastLogger.Instance.Info($"Vmove -1 axis：{axis}");
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Info($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}



		private void rightBtn_MouseDown(object sender, MouseEventArgs e)
		{
			try
			{
				myZmcaux.Vmove(g_handle, axis, 1 * _axisDir);
				//FastLogger.Instance.Info($"Vmove 1 axis：{axis}");
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Info($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void leftBtn_MouseUp(object sender, MouseEventArgs e)
		{
			try
			{
				rightBtn_MouseUp(null, null);
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Info($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}
		private void rightBtn_MouseUp(object sender, MouseEventArgs e)
		{
			try
			{
				myZmcaux.StopMove(g_handle, axis);
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Info($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void GetParam(int axis)
		{
			try
			{
				ControlParms parms = myZmcaux.GetParms(g_handle, axis, out int res);
				if (parms == null)
					return;
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Info($"获取数据时异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void leftTxt_KeyDown(object sender, KeyEventArgs e)
		{

			try
			{
				if (e.KeyCode == Keys.Enter)
				{
					_Config.zhengPosition = Convert.ToDouble(leftTxt.Text);
					Thread.Sleep(10);
					leftTxt.Text = _Config.zhengPosition.ToString("F2");
					leftLb.Text = _Config.zhengPosition.ToString("F2");
					roundLb.Text = _Config.roundPosition.ToString("F2");

				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return;
			}

		}

		private void rightTxt_KeyDown(object sender, KeyEventArgs e)
		{
			try
			{
				if (e.KeyCode == Keys.Enter)
				{
					_Config.fanPosition = Convert.ToDouble(rightTxt.Text);
					Thread.Sleep(10);
					rightTxt.Text = _Config.fanPosition.ToString("F2");
					rightLb.Text = _Config.fanPosition.ToString("F2");
					roundLb.Text = _Config.roundPosition.ToString("F2");
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return;
			}
		}

		private void uiLabel8_DoubleClick(object sender, EventArgs e)
		{
			try
			{
				leftTxt.Text = location2_Txt.Text;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return;
			}
		}

		private void uiLabel9_DoubleClick(object sender, EventArgs e)
		{
			try
			{
				rightTxt.Text = location1_Txt.Text;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return;
			}
		}

		private void leftLb_DoubleClick(object sender, EventArgs e)
		{
			try
			{
				point_Txt.Text = leftLb.Text;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return;
			}
		}

		private void rightLb_DoubleClick(object sender, EventArgs e)
		{
			try
			{
				point_Txt.Text = rightLb.Text;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return;
			}
		}

		/// <summary>
		/// 轴位置刷新线程：运控卡读取在工作线程，UI 赋值经 BeginInvoke 封送（0/1 标志防队列积压）。
		/// 旧代码在后台线程直接给 TextBox.Text 赋值，窗体关闭瞬间会抛"创建窗口句柄时出错"，
		/// 与 2026-09-03 时间线 19:07:23 关闭设置窗体时的错误日志吻合，属跨线程访问 UI 控件。
		/// </summary>
		private void UpdateLocation()
		{
			int locationUpdatePending = 0;
			while (!this.IsDisposed)
			{
				try
				{
					Thread.Sleep(10);
					if (this.IsDisposed || !this.IsHandleCreated) break;
					double loc0 = myZmcaux.GetLocation(g_handle, 0);
					double loc1 = myZmcaux.GetLocation(g_handle, 1);
					double loc2 = myZmcaux.GetLocation(g_handle, 2);
					// 0/1标志：一条更新委托在途时跳过本轮，最多积压一条（10ms 周期远快于 UI 消费）
					if (Interlocked.Exchange(ref locationUpdatePending, 1) == 0)
					{
						this.BeginInvoke(new Action(() =>
						{
							locationUpdatePending = 0;
							if (this.IsDisposed) return;
							location1_Txt.Text = loc0.ToString("F2");
							location2_Txt.Text = loc1.ToString("F2");
							location3_Txt.Text = loc2.ToString("F2");
						}));
					}
				}
				catch (Exception ex)
				{
					// 窗体销毁/句柄失效或运控卡读异常：线程静默退出（IsBackground），不弹窗不刷屏
					try { FastLogger.Instance.Info($"位置刷新线程退出: {ex.Message}"); } catch { }
					break;
				}
			}
		}

		volatile bool TriggerFlag = true;
		/// <summary>
		/// 触发拍照
		/// </summary>
		/// <param name="type">true: 单次； false：连续</param>
		public void TriggerCameraMethod(bool type)
		{
			try
			{
				int camIdx = uiComboBox_cam.SelectedIndex;
				if (camIdx >= 0 && camIdx < 5 && !_cameraEnabled[camIdx])
				{
					MessageBox.Show($"相机{camIdx + 1}工位未启用，无法触发取像！", "工位已禁用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				// 【兜底拦截】实时取像在自动模式下禁止（单张取像保留）
				if (!type && _plc != null && _plc.IsAutoMode)
				{
					try { FastLogger.Instance.Info("【设备状态】自动模式下禁止实时取像（TriggerCameraMethod兜底拦截）"); } catch { }
					return;
				}

				TriggerFlag = true;

				if (_modbusType == 1 && !_modbusHc.modbusState) { MessageBox.Show("HCM连接已断开"); return; }
				if (_modbusType == 2 && !_modbusS7.modbusState) { MessageBox.Show("S7-1200连接已断开"); return; }

				// 【实时回报位】仅实时模式、仅 S7、仅当前相机是正/反面时置位
				if (!type)
				{
					_realtimeBitIdx = GetRealtimeBitIndex(camIdx + 1);
					if (_realtimeBitIdx < 0)
						try { FastLogger.Instance.Info($"【实时回报】相机{camIdx + 1}不在正/反面配置(frontCamNo/backCamNo)中，跳过回报位"); } catch { }
					else
						SetRealtimeReportBit(true);
				}

				_triggerLoopTask = Task.Run(() =>
				{
					while (TriggerFlag)
					{
						// 【循环内自动停止】实时进行中切自动 → 停止循环（不依赖事件的第二道防线）
						if (!type && _plc != null && _plc.IsAutoMode)
						{
							TriggerFlag = false;
							try { FastLogger.Instance.Info("【设备状态】自动模式切入，实时取像循环自动停止"); } catch { }
							break;
						}
						//myZmcaux.SetOut(g_handle, tempTriggerPath, 1);
						//Thread.Sleep(10);
						//myZmcaux.SetOut(g_handle, tempTriggerPath, 0);

						//FastLogger.Instance.Info(tempTriggerPath+"");
						if (_modbusType == 1) _modbusHc.modbusTcp.Write(tempTriggerPath, true);
					else if (_modbusType == 2) _modbusS7.WriteRegister(tempTriggerPath, (short)1);
						Thread.Sleep(100);
						if (type)
						{
							TriggerFlag = false;
							if (_modbusType == 1) _modbusHc.modbusTcp.Write(tempTriggerPath, false);
					else if (_modbusType == 2) _modbusS7.WriteRegister(tempTriggerPath, (short)0);
							return;
						}
					}
					if (_modbusType == 1) _modbusHc.modbusTcp.Write(tempTriggerPath, false);
					else if (_modbusType == 2) _modbusS7.WriteRegister(tempTriggerPath, (short)0);

					// 【停止复位回报位】（用户停/自动停两条路径都汇聚到这里）
					if (!type && _realtimeBitIdx >= 0)
					{
						SetRealtimeReportBit(false);
						_realtimeBitIdx = -1;
						// UI 状态幂等恢复（用户手动停时按钮文本已是"实时取像"，此处不动作）
						try { if (!this.IsDisposed && this.IsHandleCreated) this.BeginInvoke(new Action(() =>
						{
							if (!IsDisposed && uiButton4.Text == "停止实时")
							{ uiButton4.Text = "实时取像"; uiButton3.Enabled = true; uiComboBox_cam.Enabled = true; uiComboBox_axis.Enabled = true; }
						})); } catch { }
					}
				});
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Info($"手动调试时...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		/// <summary>
		/// camNo(1-5)→实时回报位索引: -1=非正反面, 0=DB1000.DBX72.0, 1=DB1000.DBX72.1
		/// （realtimeBitFront=0 时正面=72.0 反面=72.1；=1 时互换。运行期改 setup.ini 立即生效）
		/// </summary>
		private int GetRealtimeBitIndex(int camNo)
		{
			bool isFront = camNo == _Config.FrontCamNo;
			bool isBack = camNo == _Config.BackCamNo;
			if (!isFront && !isBack) return -1;
			int frontBit = _Config.RealtimeBitFront == 1 ? 1 : 0;
			return isFront ? frontBit : 1 - frontBit;
		}

		/// <summary>写入 S7 实时取像回报位（TRUE=打开实时显示 FALSE=关闭）。仅西门子有此回报位</summary>
		private void SetRealtimeReportBit(bool value)
		{
			if (_modbusType != 2 || _modbusS7 == null || _realtimeBitIdx < 0) return;
			try
			{
				string addr = _realtimeBitIdx == 1 ? "DB1000.DBX72.1" : "DB1000.DBX72.0";
				_modbusS7.WriteRegister(addr, value);
				try { FastLogger.Instance.Info($"【实时回报】{addr}={(value ? "TRUE" : "FALSE")}"); } catch { }
			}
			catch (Exception ex)
			{
				try { FastLogger.Instance.Warn($"【实时回报】写入{(_realtimeBitIdx == 1 ? "DB1000.DBX72.1" : "DB1000.DBX72.0")}失败: {ex.Message}"); } catch { }
			}
		}

		/// <summary>
		/// 停止实时/单张触发循环并等待其收尾（复位触发位、复位回报位）最多 1 秒。
		/// 旧代码置 TriggerFlag=false 后立即继续，循环收尾的 PLC 写与回报位复位仍在后台飞行，
		/// 关窗/切自动时与收尾写竞争，可能残留回报位 TRUE。幂等：重复调用只多等一次（任务已结束则立即返回）。
		/// </summary>
		private void StopTriggerLoopAndWait()
		{
			TriggerFlag = false;
			var t = _triggerLoopTask;
			if (t == null) return;
			try
			{
				if (!t.Wait(1000))
					FastLogger.Instance.Warn("[实时触发] 停止等待循环收尾超时(1s)，收尾PLC写将在后台完成");
			}
			catch (Exception ex)
			{
				FastLogger.Instance.Warn($"[实时触发] 停止等待异常: {ex.Message}");
			}
		}

		/// <summary>设备模式变化（PLC后台线程触发）：切自动时先停实时、提示并自动关闭本窗体</summary>
		private void OnDeviceModeFromPlc(bool isAuto, short rawValue)
		{
			if (!isAuto) return;   // 只处理切自动
			try
			{
				if (this.IsDisposed || !this.IsHandleCreated) return;
				this.BeginInvoke(new Action(() =>
				{
					try
					{
						if (this.IsDisposed || !this.Visible) return;
						StopRealtimeByAutoMode();   // ① 必须先停实时（否则 FormClosing 会拦截 Close）
						MessageBox.Show(this, "设备已切换到自动模式，相机设置将自动关闭！", "系统提示",
							MessageBoxButtons.OK, MessageBoxIcon.Warning);   // ② 提示
						this.Close();                // ③ ShowDialog 内 Close 合法，返回后主程序 cameraDebug 复位
					}
					catch (Exception ex) { try { FastLogger.Instance.Info($"自动模式关闭相机设置异常...\r\n {ex.Message}"); } catch { } }
				}));
			}
			catch (Exception ex) { try { FastLogger.Instance.Error("OnDeviceModeFromPlc 异常", ex); } catch { } }
		}

		/// <summary>自动模式切入时停止实时取像（幂等：已停止时直接返回），回报位复位由触发循环收尾统一做</summary>
		private void StopRealtimeByAutoMode()
		{
			if (uiButton4.Text != "停止实时") return;
			StopTriggerLoopAndWait();   // 停止循环并等收尾（触发位复位+回报位复位完成后再放行）
			uiButton4.Text = "实时取像";
			uiButton3.Enabled = true;
			uiComboBox_cam.Enabled = true;
			uiComboBox_axis.Enabled = true;
			try { FastLogger.Instance.Info("【设备状态】自动模式切入，已自动停止实时取像"); } catch { }
		}

		/// <summary>窗体关闭兜底：丢弃未消费的最新帧 + 退订事件 + 复位回报位</summary>
		private void MainFrm_FormClosed(object sender, FormClosedEventArgs e)
		{
			// 回调线程可能刚入槽、UI 消费尚未处理：取走并释放，防 GDI+ 句柄泄漏
			var pendingFrame = Interlocked.Exchange(ref _latestFrame, null);
			if (pendingFrame != null) { try { pendingFrame.Dispose(); } catch { } }
			try { if (_plc != null) _plc.EventDeviceMode -= OnDeviceModeFromPlc; } catch { }
			if (_realtimeBitIdx >= 0)
			{
				try { SetRealtimeReportBit(false); } catch { }
				_realtimeBitIdx = -1;
			}
		}

		#region Low
		/// <summary>
		/// 触发拍照
		/// </summary>
		/// <param name="type">true: 单次； false：连续</param>
		//public void TriggerCameraMethod(bool type)
		//{
		//	try
		//	{
		//		TriggerFlag = true;

		//		Task.Run(() =>
		//		{
		//			while (TriggerFlag)
		//			{
		//				myZmcaux.SetOut(g_handle, tempTriggerPath, 1);
		//				//Thread.Sleep(10);
		//				myZmcaux.SetOut(g_handle, tempTriggerPath, 0);

		//				//FastLogger.Instance.Info(tempTriggerPath+"");
		//				if (type)
		//				{
		//					TriggerFlag = false;
		//					return;
		//				}
		//			}
		//		});
		//	}
		//	catch (Exception ex)
		//	{
		//		FastLogger.Instance.Info($"手动调试时...\r\n {ex.Message} \r\n {ex.StackTrace}");
		//	}
		//}
		#endregion

		#endregion
	}
}
