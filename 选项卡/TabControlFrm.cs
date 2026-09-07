using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TabControl
{
	public partial class TabControlFrm : Form
	{
		public TabControlFrm()
		{
			InitializeComponent();
		}

		private void uiPanel5_Click(object sender, EventArgs e)
		{
			// 【修复】相机设置必须从主界面入口打开（TabFrm：传入运控卡句柄、PLC对象、5个相机实例，
			// 并设置 cameraDebug 让主程序在设置期间不处理帧）。本启动器无参构造会拿到空相机/空PLC，
			// 实时取像时 Cam1_OnImage 对空 daHuaSDK 无效、触发拍照空引用，属无功能入口。
			MessageBox.Show("相机设置请从主界面的入口打开（主界面已加载相机与PLC上下文），本启动器不再提供该功能。",
				"系统提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
	}
}
