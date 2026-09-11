using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CameraSwitchTool.Models;
using CameraSwitchTool.Services;

namespace CameraSwitchTool.ViewModels
{
    /// <summary>
    /// 主窗口视图模型：管理两组相机开关（工位启用 ActiveCam / AI 推理 IFRunCamera）、
    /// 脏标记、校验（至少启用一个工位，与主程序 MainFrm 防呆一致）、保存/放弃/重启。
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        /// <summary>5 台相机的检测项目（与 AsyncDatabaseRecorder 缺陷归类一致）</summary>
        private static readonly string[] CameraPurposes =
        {
            "管内异物",
            "管盖有无",
            "管口圆度",
            "正面工号缺失",
            "细分缺陷（背面工号 / P-Code / 色标对中 / 爆管 / 斜口 / 未剪断）"
        };

        public ObservableCollection<CameraItem> StationItems { get; } = new ObservableCollection<CameraItem>();
        public ObservableCollection<CameraItem> InferenceItems { get; } = new ObservableCollection<CameraItem>();

        public RelayCommand SaveCommand { get; }
        public RelayCommand DiscardCommand { get; }
        public RelayCommand RestartCommand { get; }

        /// <summary>确认对话框回调，由主窗口注入（ViewModel 不直接依赖 UI）</summary>
        public Func<string, string, string, string, bool> ConfirmHandler { get; set; }

        /// <summary>保存成功后触发，主窗口据此提示是否重启主程序</summary>
        public event EventHandler SaveSucceeded;

        /// <summary>操作失败时触发（参数为错误消息）</summary>
        public event EventHandler<string> ErrorOccurred;

        /// <summary>运行中加密狗被拔出时触发（主窗口弹出警示）</summary>
        public event EventHandler DogRemoved;

        /// <summary>加密狗重新插入时触发</summary>
        public event EventHandler DogRestored;

        private bool _loading;

        private bool _isDirty;
        public bool IsDirty
        {
            get { return _isDirty; }
            private set
            {
                if (_isDirty == value) return;
                _isDirty = value;
                RaisePropertyChanged(nameof(IsDirty));
                SaveCommand.RaiseCanExecuteChanged();
                DiscardCommand.RaiseCanExecuteChanged();
            }
        }

        private bool _isValid = true;
        public bool IsValid
        {
            get { return _isValid; }
            private set
            {
                if (_isValid == value) return;
                _isValid = value;
                RaisePropertyChanged(nameof(IsValid));
                SaveCommand.RaiseCanExecuteChanged();
            }
        }

        private bool _mainProcessRunning;
        public bool MainProcessRunning
        {
            get { return _mainProcessRunning; }
            set
            {
                if (_mainProcessRunning == value) return;
                _mainProcessRunning = value;
                RaisePropertyChanged(nameof(MainProcessRunning));
            }
        }

        private bool _donglePresent;
        private bool _dogInitialized;

        /// <summary>加密狗是否在线。拔狗后锁定保存/重启，插回自动恢复。</summary>
        public bool DonglePresent
        {
            get { return _donglePresent; }
            set
            {
                if (_donglePresent == value) return;
                bool firstTime = !_dogInitialized;
                _dogInitialized = true;
                _donglePresent = value;
                RaisePropertyChanged(nameof(DonglePresent));
                SaveCommand.RaiseCanExecuteChanged();
                RestartCommand.RaiseCanExecuteChanged();
                if (!firstTime)
                {
                    if (value)
                    {
                        OperationLog.Info("加密狗已连接，保存/重启功能已恢复");
                        DogRestored?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        OperationLog.Warn("检测到加密狗已拔出，保存/重启功能已锁定");
                        DogRemoved?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        private string _iniPath = "";
        public string IniPath
        {
            get { return _iniPath; }
            private set
            {
                _iniPath = value;
                RaisePropertyChanged(nameof(IniPath));
            }
        }

        private string _lastSavedText = "";
        public string LastSavedText
        {
            get { return _lastSavedText; }
            private set
            {
                _lastSavedText = value;
                RaisePropertyChanged(nameof(LastSavedText));
            }
        }

        public MainViewModel()
        {
            SaveCommand = new RelayCommand(ExecuteSave, () => IsDirty && IsValid && DonglePresent);
            DiscardCommand = new RelayCommand(ExecuteDiscard, () => IsDirty);
            RestartCommand = new RelayCommand(ExecuteRestart, () => DonglePresent);
        }

        /// <summary>
        /// 加载 setup.ini 中的 10 个开关键。找不到配置返回 false。
        /// </summary>
        public bool Load()
        {
            string path = IniConfigService.LocateIniPath();
            if (path == null) return false;

            IniPath = path;
            OperationLog.Init(Path.GetDirectoryName(path));

            _loading = true;
            try
            {
                StationItems.Clear();
                InferenceItems.Clear();
                for (int i = 1; i <= 5; i++)
                {
                    StationItems.Add(CreateItem(i, "ActiveCam" + i, "工位启用"));
                    InferenceItems.Add(CreateItem(i, "IFRunCamera" + i, "推理开关"));
                }
            }
            finally
            {
                _loading = false;
            }

            IsDirty = false;
            IsValid = StationItems.Any(x => x.IsEnabled);
            RefreshRuntimeState();

            OperationLog.Info(string.Format("工具启动 v{0}，配置文件：{1}，主程序{2}，加密狗{3}",
                typeof(App).Assembly.GetName().Version,
                IniPath,
                MainProcessRunning ? "运行中" : "未运行",
                DonglePresent ? "已连接" : "未检测到"));
            return true;
        }

        private CameraItem CreateItem(int index, string iniKey, string groupCaption)
        {
            bool? raw = IniConfigService.ReadBool(IniPath, iniKey);
            bool enabled = raw ?? true; // 键缺失或无法解析时默认 True，与主程序 GetCachedValue 一致
            var item = new CameraItem(index, iniKey, groupCaption, CameraPurposes[index - 1], enabled);
            item.Changed += OnItemChanged;
            return item;
        }

        private void OnItemChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            IsValid = StationItems.Any(x => x.IsEnabled);
            IsDirty = true;
        }

        private void ExecuteSave()
        {
            try
            {
                if (!IsValid || !IsDirty || !DonglePresent) return;

                // 记录变更明细，先备份
                var all = StationItems.Concat(InferenceItems).ToList();
                IniConfigService.BackupIni(IniPath);
                OperationLog.Info(string.Format("保存设置：已备份 {0}", IniPath + ".camswitch.bak"));

                foreach (var item in all)
                {
                    IniConfigService.WriteBool(IniPath, item.IniKey, item.IsEnabled);
                }

                // 回读校验，防止静默写失败
                foreach (var item in all)
                {
                    if (IniConfigService.ReadBool(IniPath, item.IniKey) != item.IsEnabled)
                    {
                        throw new InvalidOperationException("写入后回读校验失败，请确认 setup.ini 未被占用或只读");
                    }
                }

                LastSavedText = "最后保存：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                IsDirty = false;
                OperationLog.Info("保存设置成功：" + DescribeCurrentState());
                SaveSucceeded?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                OperationLog.Error("保存设置失败：" + ex.Message);
                ErrorOccurred?.Invoke(this, ex.Message);
            }
        }

        private void ExecuteDiscard()
        {
            if (ConfirmHandler != null)
            {
                bool ok = ConfirmHandler("放弃修改", "确定放弃本次未保存的修改，并重新读取 setup.ini 吗？", "放弃修改", "取消");
                if (!ok) return;
            }
            Load();
            OperationLog.Info("放弃修改：已重新读取 setup.ini");
        }

        private void ExecuteRestart()
        {
            try
            {
                bool running = MainProcessController.IsRunning();
                if (running)
                {
                    if (ConfirmHandler != null)
                    {
                        bool ok = ConfirmHandler("重启主程序",
                            "主程序将立即重启，检测流程会中断约半分钟。\n确定继续吗？",
                            "立即重启", "取消");
                        if (!ok) return;
                    }
                    MainProcessController.RestartMainProcess();
                    OperationLog.Info("已重启主程序 VisionMeasure.exe");
                }
                else
                {
                    if (ConfirmHandler != null)
                    {
                        bool ok = ConfirmHandler("启动主程序", "主程序当前未运行，是否立即启动？", "立即启动", "取消");
                        if (!ok) return;
                    }
                    MainProcessController.StartMainProcess();
                    OperationLog.Info("已启动主程序 VisionMeasure.exe");
                }
                RefreshRuntimeState();
            }
            catch (Exception ex)
            {
                OperationLog.Error("主程序操作失败：" + ex.Message);
                ErrorOccurred?.Invoke(this, ex.Message);
            }
        }

        /// <summary>刷新主程序运行状态与加密狗在线状态（由主窗口定时器调用）</summary>
        public void RefreshRuntimeState()
        {
            try { MainProcessRunning = MainProcessController.IsRunning(); }
            catch { }
            try { DonglePresent = DogGuardService.IsDogPresent(); }
            catch { }
        }

        /// <summary>生成当前 10 项开关的摘要，用于日志</summary>
        private string DescribeCurrentState()
        {
            var parts = StationItems.Select(x => string.Format("{0}={1}", x.IniKey, x.IsEnabled))
                .Concat(InferenceItems.Select(x => string.Format("{0}={1}", x.IniKey, x.IsEnabled)));
            return string.Join("，", parts);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
