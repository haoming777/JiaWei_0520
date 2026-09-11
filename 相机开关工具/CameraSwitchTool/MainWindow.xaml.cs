using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CameraSwitchTool.Services;
using CameraSwitchTool.ViewModels;

namespace CameraSwitchTool
{
    /// <summary>
    /// 主窗口：加载配置、监听开关变更、保存后提示重启、定期刷新主程序运行状态。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly DispatcherTimer _statusTimer;

        public MainWindow()
        {
            InitializeComponent();

            _vm = new MainViewModel();
            DataContext = _vm;
            _vm.ConfirmHandler = ShowConfirm;
            _vm.SaveSucceeded += OnSaveSucceeded;
            _vm.ErrorOccurred += OnErrorOccurred;
            _vm.DogRemoved += OnDogRemoved;

            // 每 2 秒刷新主程序运行状态与加密狗在线状态
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _statusTimer.Tick += (s, e) => _vm.RefreshRuntimeState();

            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 小屏幕（1366x768）自适应：高度超出工作区时收缩，内容滚动
            double maxHeight = SystemParameters.WorkArea.Height - 24;
            if (Height > maxHeight)
            {
                Height = maxHeight;
                Top = (SystemParameters.WorkArea.Height - Height) / 2;
            }

            if (!_vm.Load())
            {
                DialogWindow.Show(this, DialogWindow.DialogIconKind.Error, "未找到配置文件",
                    "未找到 setup.ini。\n\n请将本工具（CameraSwitchTool.exe）复制到主程序 VisionMeasure.exe 所在目录后运行。",
                    null, "确定", null);
                Close();
                return;
            }

            // 启动必须插狗：未检测到则循环等待，直到插入或用户退出
            if (!EnsureDongleAtStartup())
            {
                OperationLog.Warn("未检测到加密狗，用户选择退出");
                Close();
                return;
            }

            _statusTimer.Start();
        }

        /// <summary>
        /// 启动加密狗校验：未检测到狗时循环弹窗，支持插入后点"重新检测"继续。
        /// 返回 false 表示用户选择退出。
        /// </summary>
        private bool EnsureDongleAtStartup()
        {
            while (!_vm.DonglePresent)
            {
                bool retry = DialogWindow.Show(this, DialogWindow.DialogIconKind.Error, "未检测到加密狗",
                    "本工具需要插入加密狗才能使用。\n\n请插入加密狗后点击“重新检测”。", null, "重新检测", "退出");
                if (!retry) return false;
                _vm.RefreshRuntimeState();
            }
            return true;
        }

        /// <summary>运行中拔狗 → 弹窗警示（保存/重启已由 VM 自动锁定）</summary>
        private void OnDogRemoved(object sender, EventArgs e)
        {
            DialogWindow.Show(this, DialogWindow.DialogIconKind.Warning, "检测到加密狗已拔出",
                "保存与重启功能已锁定，设置修改将无法保存。\n\n请重新插入加密狗以恢复。", null, "确定", null);
        }

        /// <summary>保存成功 → 提示是否重启/启动主程序使其生效</summary>
        private void OnSaveSucceeded(object sender, EventArgs e)
        {
            bool running = MainProcessController.IsRunning();
            bool doRestart = DialogWindow.Show(this, DialogWindow.DialogIconKind.Question, "保存成功",
                running
                    ? "设置已保存到 setup.ini。\n\n这些设置需重启主程序后才会生效。是否立即重启主程序？"
                    : "设置已保存到 setup.ini。\n\n主程序当前未运行。是否立即启动主程序？",
                _vm.IniPath,
                running ? "立即重启" : "立即启动", "稍后再说");

            if (doRestart)
            {
                try
                {
                    if (running)
                    {
                        MainProcessController.RestartMainProcess();
                        OperationLog.Info("保存后已重启主程序 VisionMeasure.exe");
                    }
                    else
                    {
                        MainProcessController.StartMainProcess();
                        OperationLog.Info("保存后已启动主程序 VisionMeasure.exe");
                    }
                }
                catch (Exception ex)
                {
                    OperationLog.Error("保存后主程序操作失败：" + ex.Message);
                    DialogWindow.Show(this, DialogWindow.DialogIconKind.Error, "操作失败", ex.Message, null, "确定", null);
                }
            }
            _vm.RefreshRuntimeState();
        }

        private void OnErrorOccurred(object sender, string message)
        {
            DialogWindow.Show(this, DialogWindow.DialogIconKind.Error, "操作失败", message, null, "确定", null);
        }

        private bool ShowConfirm(string title, string message, string okText, string cancelText)
        {
            return DialogWindow.Show(this, DialogWindow.DialogIconKind.Question, title, message, null, okText, cancelText);
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_vm.IsDirty)
            {
                bool quit = DialogWindow.Show(this, DialogWindow.DialogIconKind.Warning, "有未保存的修改",
                    "当前有未保存的修改，退出将丢弃这些修改。\n确定要退出吗？", null, "退出", "返回");
                if (!quit)
                {
                    e.Cancel = true;
                    return;
                }
            }
            _statusTimer.Stop();
            OperationLog.Info("工具退出");
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
