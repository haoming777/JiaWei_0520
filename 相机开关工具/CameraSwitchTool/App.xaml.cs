using System.Threading;
using System.Windows;

namespace CameraSwitchTool
{
    /// <summary>
    /// 应用入口：单实例互斥，防止重复运行导致同时写 setup.ini。
    /// 注意：互斥体必须持有到进程退出，不可释放（与主程序 Program.cs 同规则）。
    /// </summary>
    public partial class App : Application
    {
        private Mutex _singleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "CameraSwitchTool_SingleInstance_8F2A7C31", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("相机开关配置工具已经在运行中。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(1);
                return;
            }
            base.OnStartup(e);
        }
    }
}
