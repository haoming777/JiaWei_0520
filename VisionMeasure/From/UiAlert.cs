using System;
using System.Drawing;
using System.Windows.Forms;

namespace VisionMeasure
{
	/// <summary>
	/// 非阻塞错误提示条：替代全局 UI 线程异常处理里的模态 MessageBox。
	/// 模态框会卡住 UI 线程消息泵（历史挂起事件的诱因之一），此提示条 Show() 非模态，
	/// 6 秒自动消失、点击立即消失；异常连续发生时合并更新文本，不叠加窗口。
	/// </summary>
	internal static class UiAlert
	{
		private static Form _toast;
		private static Label _lbl;
		private static Timer _closeTimer;

		public static void ShowError(string message)
		{
			try
			{
				if (_toast != null && !_toast.IsDisposed)
				{
					// 已有提示条在显示：更新文本并重置自动关闭计时
					_toast.BeginInvoke(new Action(() =>
					{
						try
						{
							if (_lbl != null && !_lbl.IsDisposed) _lbl.Text = message;
							_closeTimer?.Stop();
							_closeTimer?.Start();
						}
						catch { }
					}));
					return;
				}

				_toast = new Form
				{
					FormBorderStyle = FormBorderStyle.None,
					StartPosition = FormStartPosition.Manual,
					TopMost = true,
					ShowInTaskbar = false,
					BackColor = Color.FromArgb(200, 30, 30),
					Width = 620,
					Height = 96
				};
				_lbl = new Label
				{
					Dock = DockStyle.Fill,
					Text = message,
					ForeColor = Color.White,
					BackColor = Color.Transparent,
					TextAlign = ContentAlignment.MiddleLeft,
					Padding = new Padding(14, 6, 14, 6),
					Font = new Font("微软雅黑", 10.5f),
					Cursor = Cursors.Hand
				};
				_lbl.Click += (s, ev) => CloseToast();
				_toast.Controls.Add(_lbl);

				var area = Screen.PrimaryScreen.WorkingArea;
				_toast.Location = new Point(area.Right - _toast.Width - 24, area.Bottom - _toast.Height - 24);
				_toast.Show();

				_closeTimer = new Timer { Interval = 6000 };
				_closeTimer.Tick += (s, ev) => CloseToast();
				_closeTimer.Start();
			}
			catch { }
		}

		private static void CloseToast()
		{
			try { _closeTimer?.Stop(); } catch { }
			try
			{
				var f = _toast;
				_toast = null;
				_lbl = null;
				_closeTimer = null;
				if (f != null && !f.IsDisposed) { f.Close(); f.Dispose(); }
			}
			catch { }
		}
	}
}
