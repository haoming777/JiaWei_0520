using Sunny.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using static CommonLib.Class_Config;
using VisionMeasure;
using CommonLib;
using PLC监控;
using XL.Tool;
using SetProduct;
namespace VisionMeasure.From
{
	public partial class TabFrm : Form
	{
		public TabFrm()
		{
			InitializeComponent();
		}
		MainFrm mainFrm = new MainFrm();
		XLToolClass toolClass = new XLToolClass();

		// RecordsFrm 独立 UI 线程 —— 避免和 MainFrm 争抢 UI 线程
		private static RecordsFrm _recordsFrmInstance;
		private static Thread _recordsFrmThread;


		public TabFrm(Point point, Form form)
		{
			InitializeComponent();
			this.Top = point.Y + 20;
			this.Left = point.X + 20;

			this.mainFrm = (MainFrm)form;
		}
		private void TabFrm_Load(object sender, EventArgs e)
		{

		}


		private void closeBtn_Click(object sender, EventArgs e)
		{
			this.Close();
		}
		private void TabFrm_Deactivate(object sender, EventArgs e)
		{
			this.Hide();
		}

		private void switchTabBtn_Click(object sender, EventArgs e)
		{
			UIPanel label = (UIPanel)sender;
			SetUser.LoginFrm loginFrm = new SetUser.LoginFrm();
			loginFrm.Text = label.Text;
			//loginFrm.ShowDialog();
			_Config.test = true;
			if (_Config.test)
			{
				_Config.test = false;
				switch (label.Text)
				{
					case "用户设置":
						SetUser.MainFrm SetUserMainFrm = new SetUser.MainFrm();
						SetUserMainFrm.ShowDialog();
						break;
					case "相机设置":
						if (mainFrm.g_handle == IntPtr.Zero || mainFrm.modbusClass == null)
						{
							MessageBox.Show("条件未满足");
							return;
						}

						SetCamera.MainFrm SetCameraMainFrm = new SetCamera.MainFrm(mainFrm.g_handle, mainFrm.modbusClass);
						//if (mainFrm.camera1SDK != null && mainFrm.camera2SDK != null)
						//{
						SetCameraMainFrm.cam1 = mainFrm.camera1SDK;
						SetCameraMainFrm.cam2 = mainFrm.camera2SDK;
						SetCameraMainFrm.cam3 = mainFrm.camera3SDK;
						SetCameraMainFrm.cam4 = mainFrm.camera4SDK;
						SetCameraMainFrm.cam5 = mainFrm.camera5SDK;
						//}
						//else
						//{
						//	MessageBox.Show("相机状态异常");
						//	return;
						//}
						_Config.cameraDebug = 1;
						SetCameraMainFrm.ShowDialog();
						_Config.cameraDebug = 0;
						break;
					case "产品设置":
						SetProduct.MainFrm SetProductMainFrm = new SetProduct.MainFrm();
						SetProductMainFrm.vision = mainFrm.vision;
						//SetProductMainFrm.user = loginFrm.user;
						SetProductMainFrm.EventChangeMode += Vision_ChangeMode_Tab;
						SetProductMainFrm.ShowDialog();
						SetProductMainFrm.EventChangeMode -= Vision_ChangeMode_Tab;
						break;
					case "算法调试":
						SetVision.MainFrm SetVisionMainFrm = new SetVision.MainFrm();
						SetVisionMainFrm.vision = mainFrm.vision;
						SetVisionMainFrm.ShowDialog();
						break;
					case "系统设置":
						SetSystem.MainFrm SetSystemMainFrm = new SetSystem.MainFrm();
						SetSystemMainFrm.ShowDialog();
						break;
					case "手动调试":
						PLC监控.ControlFrm controlFrm = new ControlFrm(mainFrm.g_handle);
						toolClass.SaveLog($"ControlFrm传入的句柄为：{mainFrm.g_handle}");
						controlFrm.ShowDialog();
						//controlFrm.Show();
						break;
					case "生产记录":
						// 【关键修改】检查是否已经打开了记录窗体，避免无限制弹窗
						RecordsFrm existingFrm = Application.OpenForms.OfType<RecordsFrm>().FirstOrDefault();
						if (existingFrm != null)
						{
							// 如果已经打开，直接把它激活并带到最前面
							existingFrm.Show();
							existingFrm.WindowState = FormWindowState.Normal;
							existingFrm.Activate();
						}
						else
						{
							// 如果没打开，才新建并显示
							RecordsFrm recordsFrm = new RecordsFrm();
							recordsFrm.Show();
						}
						break;
					default:
						break;
				}
			}
		}


				private static void ShowRecordsFrm()
		{
			// RecordsFrm 独立 UI 线程：与 MainFrm 完全隔离，互不抢占
			if (_recordsFrmInstance != null && _recordsFrmInstance.IsHandleCreated && !_recordsFrmInstance.IsDisposed)
			{
				// 已存在 → 跨线程激活
				_recordsFrmInstance.BeginInvoke(new Action(() =>
				{
					if (!_recordsFrmInstance.Visible)
						_recordsFrmInstance.Show();
					_recordsFrmInstance.WindowState = FormWindowState.Normal;
					_recordsFrmInstance.Activate();
				}));
				return;
			}

			// 首次创建 → 启动独立 STA 线程
			_recordsFrmThread = new Thread(() =>
			{
				_recordsFrmInstance = new RecordsFrm();
				_recordsFrmInstance.FormClosed += (s, e) => _recordsFrmInstance = null;
				Application.Run(_recordsFrmInstance);
			});
			_recordsFrmThread.SetApartmentState(ApartmentState.STA);
			_recordsFrmThread.IsBackground = true;
			_recordsFrmThread.Start();
		}


		private void Vision_ChangeMode_Tab(string Model)
		{
			mainFrm.Vision_ChangeMode(Model);
		}
	}
}
