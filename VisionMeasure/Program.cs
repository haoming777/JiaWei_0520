using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VisionMeasure
{
	internal static class Program
	{
		/// <summary>
		/// 应用程序的主入口点。
		/// </summary>
		[STAThread]
		static void Main()
		{

			bool isAppRunning = false;
			using (Mutex mutex = new Mutex(true, Application.ProductName, out isAppRunning))
			{
				if (isAppRunning)
				{
					Application.EnableVisualStyles();
					Application.SetCompatibleTextRenderingDefault(false);
					//处理未捕获的异常
					Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
					
					MainFrm mainFrm = new MainFrm();
					Application.Run(mainFrm);
				}
				else
				{
					MessageBox.Show("系统已经启动！", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					Environment.Exit(1);
				}
			}
		}
	}
}
