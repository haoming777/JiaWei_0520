using System;
using System.ComponentModel;

namespace CameraSwitchTool.Models
{
    /// <summary>
    /// 单个相机开关项（对应 setup.ini [system] 节中的一个布尔键）。
    /// </summary>
    public class CameraItem : INotifyPropertyChanged
    {
        public int Index { get; }

        /// <summary>INI 键名，如 ActiveCam1 / IFRunCamera1</summary>
        public string IniKey { get; }

        /// <summary>行标题，如 "相机1 · 管内异物"</summary>
        public string Title { get; }

        /// <summary>行说明，如 "ActiveCam1 · 工位启用"</summary>
        public string Caption { get; }

        private bool _isEnabled;

        public bool IsEnabled
        {
            get { return _isEnabled; }
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>用户切换开关时触发（用于脏标记与校验）</summary>
        public event EventHandler Changed;

        public event PropertyChangedEventHandler PropertyChanged;

        public CameraItem(int index, string iniKey, string groupCaption, string purpose, bool enabled)
        {
            Index = index;
            IniKey = iniKey;
            Title = string.Format("相机{0} · {1}", index, purpose);
            Caption = string.Format("{0} · {1}", iniKey, groupCaption);
            _isEnabled = enabled;
        }
    }
}
