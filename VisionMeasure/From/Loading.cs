using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VisionMeasure.From
{
    public partial class Loading : Form
    {
        public Loading()
        {
            InitializeComponent();
        }
        // 提供一个静态方法显示加载窗体
        public static void ShowLoadingScreen()
        {
            // 设置 UIWaitingBar 的初始状态
            
            var loadingForm = new Loading();
            loadingForm.StartPosition = FormStartPosition.CenterScreen; // 居中显示
            loadingForm.TopMost = true; // 置顶显示
            loadingForm.Show(); // 显示窗体
            Application.DoEvents(); // 确保窗体立即显示
        }

        // 提供一个静态方法关闭加载窗体
        public static void CloseLoadingScreen(Form loadingForm)
        {
            if (loadingForm != null && !loadingForm.IsDisposed)
            {
                loadingForm.Close();
            }
        }
    }
}
