using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Wpf_Loading
{
    /// <summary>
    /// WPF 工业风加载窗口 — 高露洁牙膏字符检测系统启动界面
    /// </summary>
    public partial class LoadingWindow : Window
    {
        public event EventHandler LoadingCancelled;

        private readonly DispatcherTimer _elapsedTimer;
        private DateTime _startTime;

        public LoadingWindow()
        {
            InitializeComponent();
            InitializeWpfApplication();
            CreateSpinner();

            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimer.Tick += (s, e) =>
            {
                var elapsed = DateTime.Now - _startTime;
                txtElapsed.Text = "耗时 " + elapsed.Minutes.ToString("D2") + ":" + elapsed.Seconds.ToString("D2");
                txtDateTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            };
            Loaded += (s, e) =>
            {
                _startTime = DateTime.Now;
                _elapsedTimer.Start();
            };
            Unloaded += (s, e) => _elapsedTimer.Stop();
        }

        private void InitializeWpfApplication()
        {
            if (Application.Current == null)
            {
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }
        }

        /// <summary>原子轨道旋转加载动画</summary>
        private void CreateSpinner()
        {
            double cx = 50, cy = 50;
            var canvas = new Canvas { Width = 100, Height = 100 };
            var blue = Color.FromRgb(37, 99, 235);

            // 背景光晕
            var glow = new Ellipse
            {
                Width = 90, Height = 90,
                Fill = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.5, 0.5),
                    Center = new Point(0.5, 0.5),
                    GradientStops = new GradientStopCollection {
                        new GradientStop(Color.FromArgb(35, 37, 99, 235), 0),
                        new GradientStop(Color.FromArgb(0, 37, 99, 235), 1) }
                }
            };
            Canvas.SetLeft(glow, cx - 45); Canvas.SetTop(glow, cy - 45);
            canvas.Children.Add(glow);

            // 3层原子轨道 + 电子
            double[] radii = { 43, 33, 23 };
            double[] speeds = { 3.2, 2.0, 1.3 };
            double[] dotSizes = { 7, 8, 9 };
            for (int i = 0; i < 3; i++)
            {
                double r = radii[i];
                var orbit = new System.Windows.Shapes.Path
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(20, 37, 99, 235)),
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 3, 5 },
                    Data = new EllipseGeometry(new Point(cx, cy), r, r)
                };
                canvas.Children.Add(orbit);

                var dot = new Ellipse
                {
                    Width = dotSizes[i], Height = dotSizes[i],
                    Fill = new SolidColorBrush(blue)
                };
                Canvas.SetLeft(dot, cx + r - dotSizes[i] / 2.0);
                Canvas.SetTop(dot, cy - dotSizes[i] / 2.0);
                canvas.Children.Add(dot);

                var rt = new RotateTransform(0, -r, 0);
                dot.RenderTransform = rt;
                var ra = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(speeds[i]))
                { RepeatBehavior = RepeatBehavior.Forever };
                rt.BeginAnimation(RotateTransform.AngleProperty, ra);
            }

            // 脉动光环
            for (int i = 0; i < 3; i++)
            {
                var ring = new Ellipse
                {
                    Width = 80, Height = 80,
                    Stroke = new SolidColorBrush(blue), StrokeThickness = 1,
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };
                Canvas.SetLeft(ring, cx - 40); Canvas.SetTop(ring, cy - 40);
                canvas.Children.Add(ring);

                var scale = new ScaleTransform(0.1, 0.1);
                ring.RenderTransform = scale;
                var sa = new DoubleAnimation(0.1, 1.0, TimeSpan.FromSeconds(1.8))
                { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(i * 0.6) };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, sa);

                var oa = new DoubleAnimation(0.45, 0.0, TimeSpan.FromSeconds(1.8))
                { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(i * 0.6) };
                ring.BeginAnimation(UIElement.OpacityProperty, oa);
            }

            // 中心核
            var core = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(blue) };
            Canvas.SetLeft(core, cx - 5); Canvas.SetTop(core, cy - 5);
            canvas.Children.Add(core);
            var pulse = new DoubleAnimation(0.4, 1.0, TimeSpan.FromSeconds(0.7))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            core.BeginAnimation(UIElement.OpacityProperty, pulse);

            spinnerContainer.Children.Add(canvas);
        }

        /// <summary>更新加载进度和状态文字（线程安全）</summary>
        public void UpdateProgress(int percentage, string statusText)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateProgress(percentage, statusText));
                return;
            }

            percentage = Math.Max(0, Math.Min(100, percentage));

            var anim = new DoubleAnimation(percentage, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            progressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, anim,
                HandoffBehavior.SnapshotAndReplace);

            txtPercent.Text = $"{percentage}%";
            txtStepLabel.Text = statusText;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            LoadingCancelled?.Invoke(this, EventArgs.Empty);
            Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
