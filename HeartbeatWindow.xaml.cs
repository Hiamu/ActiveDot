using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ActiveDot
{
    /// <summary>
    /// 心跳窗口：一个极小的置顶半透明窗口。
    /// 逐帧在两种状态间交替：
    ///   - 亮帧：半透明白色叠加（真实 alpha，透出下方内容）
    ///   - 暗帧：半透明黑色叠加
    /// 两帧亮度差一大截，静态检测很容易捕捉；平均值贴近背景，利用视觉暂留看起来不黑也不白。
    /// 透明度由"亮度增量"滑块控制：值越大，黑白闪烁越明显；值越小，亮度变化越轻微。
    /// 点击穿透通过 WM_NCHITTEST 返回 HTTRANSPARENT 实现（仅拖动模式时临时关闭）。
    /// </summary>
    public partial class HeartbeatWindow : Window
    {
        private AppConfig _config;
        private IntPtr _hwnd = IntPtr.Zero;
        private HwndSource _hwndSource;

        // 半透明叠加层的 alpha（由亮度增量 1-10 换算而来）
        private byte _overlayAlpha = 32;

        private bool _lit = true; // 上一帧亮/暗状态
        private bool _running = false;

        // 复用同一个画刷，只改其 Color，避免每帧分配新画刷引发 GC 压力
        private readonly SolidColorBrush _brush = new SolidColorBrush();

        // 节流：按 HeartbeatHz 限频翻转，避免 DWM 每帧合成
        private long _nextFlipTick = 0;
        private long _tickInterval = 0;
        // 周期置顶节流
        private long _nextPinTick = 0;

        // 手工拖动（限制在显示器四边内）
        private bool _dragging = false;
        private Point _dragStartScreen;
        private Point _dragStartPos;

        public HeartbeatWindow(AppConfig config)
        {
            InitializeComponent();
            _config = config;

            // 只把背景指向复用的画刷一次，之后每帧仅改颜色
            HeartFill.Background = _brush;

            // 设定初始大小与位置
            ApplySize();
            ApplyPosition();

            // 窗口 Handle 创建后进行原生样式设置 + 点穿 Hook
            SourceInitialized += OnSourceInitialized;

            // 渲染循环
            CompositionTarget.Rendering += OnRendering;

            // 应用叠加透明度并初始化首帧颜色
            _overlayAlpha = DeltaToAlpha(_config.BrightnessDelta);
            UpdateFill();
        }

        #region 原生窗口样式与点穿

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            _hwnd = hwnd;
            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource.AddHook(WndProc);
            ApplyExtendedStyle(hwnd);
            ApplyClickThrough(); // 按初始拖动状态设置 WS_EX_TRANSPARENT
            ForceTopmost(hwnd);
        }

        /// <summary>点击穿透兜底：WM_NCHITTEST 返回 HTTRANSPARENT（拖动模式外应已由 WS_EX_TRANSPARENT 拦截）。</summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST && !_config.DragMode)
            {
                handled = true;
                return (IntPtr)HTTRANSPARENT;
            }
            return IntPtr.Zero;
        }

        private void ApplyExtendedStyle(IntPtr hwnd)
        {
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            // 工具窗口（不显示在 Alt+Tab），不激活窗口。
            exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        /// <summary>
        /// 切换 WS_EX_TRANSPARENT：非拖动模式下让鼠标事件直接透到下层窗口（最可靠的点穿方式）；
        /// 拖动模式下清除该样式，使窗口能接收鼠标用于拖拽。
        /// </summary>
        public void ApplyClickThrough()
        {
            if (_hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            if (_config.DragMode)
                ex &= ~WS_EX_TRANSPARENT;
            else
                ex |= WS_EX_TRANSPARENT;
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        /// <summary>把窗口提升到 topmost 层的顶端，确保不被任务栏等其他置顶窗口盖住。</summary>
        private void ForceTopmost(IntPtr hwnd)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        #endregion

        #region 渲染循环

        private void OnRendering(object sender, EventArgs e)
        {
            if (!_running) return;

            long now = Stopwatch.GetTimestamp();

            // 周期置顶：对抗任务栏/其它置顶窗口的遮挡（约每秒一次，开销可忽略）
            if (now >= _nextPinTick && _hwnd != IntPtr.Zero && IsVisible)
            {
                _nextPinTick = now + Stopwatch.Frequency;
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            }

            // 限频：仅在到达心跳间隔时才翻转亮度，其它 vsync 直接返回，DWM 不会合成
            if (now < _nextFlipTick) return;
            if (_tickInterval > 0)
            {
                long skip = (now - _nextFlipTick) / _tickInterval + 1;
                _nextFlipTick += _tickInterval * skip;
            }
            else
            {
                _nextFlipTick = now;
            }

            _lit = !_lit; // 逐帧在亮/暗两种状态间交替
            UpdateFill();
        }

        private void RefreshColorSeed()
        {
            _lit = true;

            // 按心跳频率计算节流间隔（Stopwatch 刻度）
            int hz = Math.Clamp(_config.HeartbeatHz, 1, 100);
            _tickInterval = Stopwatch.Frequency / (long)hz;
            _nextFlipTick = Stopwatch.GetTimestamp();
        }

        /// <summary>当前窗口所在显示的 DPI 缩放系数（物理像素 / DIP）。</summary>
        private double GetScale()
        {
            var ps = PresentationSource.FromVisual(this);
            if (ps?.CompositionTarget != null)
            {
                double m = ps.CompositionTarget.TransformToDevice.M11;
                if (m > 0) return m;
            }
            return 1.0;
        }

        /// <summary>主显器区域（DIP 单位），用于把窗口完整约束在显示器内。</summary>
        private Rect GetScreenAreaDip()
        {
            var a = DisplayHelper.GetPrimaryScreenArea();
            double s = GetScale();
            return new Rect(a.Left / s, a.Top / s, a.Width / s, a.Height / s);
        }

        /// <summary>把亮度增量 1-10 换算成叠加层透明度 alpha。越大越明显，越小越轻微。整体保持在较低水平，避免耀眼。</summary>
        private static byte DeltaToAlpha(int delta)
        {
            return (byte)Math.Clamp(delta * 6, 4, 80);
        }

        private void UpdateFill()
        {
            _brush.Color = _lit
                ? Color.FromArgb(_overlayAlpha, 255, 255, 255) // 亮帧：半透明白
                : Color.FromArgb(_overlayAlpha, 0, 0, 0);       // 暗帧：半透明黑
        }

        #endregion

        #region 对外控制

        /// <summary>启用/禁用心跳。启用时若窗口已隐藏则重新显示并置顶。</summary>
        public void SetEnabled(bool enabled)
        {
            _config.Enabled = enabled;
            _running = enabled;
            if (_running)
            {
                RefreshColorSeed();
                UpdateFill();
            }
            if (enabled && !IsVisible)
            {
                Show();
                if (_hwnd != IntPtr.Zero) ForceTopmost(_hwnd);
            }
            if (!enabled && IsVisible) Hide();
        }

        /// <summary>动态调整叠加透明度（亮度增量），使黑白闪烁更明显或更轻微，实时生效。</summary>
        public void ApplyOverlayIntensity(int delta)
        {
            _overlayAlpha = DeltaToAlpha(delta);
            UpdateFill();
        }

        public bool IsActive_ => _running;

        /// <summary>动态调整心跳频率（实时生效）。</summary>
        public void ApplyHeartbeatRate(int hz)
        {
            hz = Math.Clamp(hz, 1, 100);
            _tickInterval = Stopwatch.Frequency / (long)hz;
            _nextFlipTick = Stopwatch.GetTimestamp();
        }

        /// <summary>应用尺寸变化，实时生效。</summary>
        public void ApplySize()
        {
            Width = _config.Width;
            Height = _config.Height;
            RootGrid.Width = _config.Width;
            RootGrid.Height = _config.Height;
        }

        /// <summary>应用位置：拖动模式下保留当前位置；否则按百分比/绝对坐标定位，并限位在显示器内。</summary>
        public void ApplyPosition()
        {
            if (_config.DragMode) return; // 拖动模式由用户直接拖拽改变位置

            var area = GetScreenAreaDip();
            double x, y;
            if (_config.UsePercentPosition)
            {
                x = area.Left + area.Width * _config.PosXPercent / 100.0;
                y = area.Top + area.Height * _config.PosYPercent / 100.0;
            }
            else
            {
                x = area.Left + _config.PosX;
                y = area.Top + _config.PosY;
            }
            // 使坐标表示窗口左上角，并限位保证窗口完整落在屏幕内
            MoveBlockClamped(x, y - _config.Height);
            if (_hwnd != IntPtr.Zero) ForceTopmost(_hwnd);
        }

        public bool IsDragMode => _config.DragMode;

        /// <summary>切换拖动模式：仅影响鼠标拖拽事件，同时开关点穿（拖动时临时关闭点穿以接收鼠标）。</summary>
        public void ApplyDragMode(bool enabled)
        {
            _config.DragMode = enabled;
            if (enabled)
                PreviewMouseLeftButtonDown += Heartbeat_MouseDown;
            else
                PreviewMouseLeftButtonDown -= Heartbeat_MouseDown;
            ApplyClickThrough();
        }

        private void Heartbeat_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_config.DragMode || e.LeftButton != MouseButtonState.Pressed || _dragging) return;
            e.Handled = true;
            _dragging = true;
            _dragStartPos = new Point(Left, Top);
            _dragStartScreen = PointToScreen(e.GetPosition(this));
            CaptureMouse();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            var cur = PointToScreen(e.GetPosition(this));
            MoveBlockClamped(
                _dragStartPos.X + (cur.X - _dragStartScreen.X),
                _dragStartPos.Y + (cur.Y - _dragStartScreen.Y));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();

            // 保存新位置（百分比，兼容不同分辨率）
            var p = GetPhysicalPosition();
            _config.UsePercentPosition = true;
            _config.PosXPercent = p.PX;
            _config.PosYPercent = p.PY;
            _config.PosX = (int)p.X;
            _config.PosY = (int)p.Y;
            SettingsStore.Save(_config);

            // 通知设置面板同步新位置
            SettingsWindow.NotifyDragEnded?.Invoke(p);
        }

        /// <summary>把窗口左上角移动到(left,top)，并钳制使其完整落在显示器四边内。</summary>
        private void MoveBlockClamped(double left, double top)
        {
            var a = GetScreenAreaDip();
            double minX = a.Left;
            double minY = a.Top;
            double maxX = Math.Max(minX, a.Left + a.Width - Width);
            double maxY = Math.Max(minY, a.Top + a.Height - Height);
            Left = Math.Round(Math.Clamp(left, minX, maxX));
            Top = Math.Round(Math.Clamp(top, minY, maxY));
        }

        /// <summary>在拖动结束后读取当前位置（供设置面板同步）。</summary>
        public (double X, double Y, double PX, double PY) GetPhysicalPosition()
        {
            var area = GetScreenAreaDip();
            double px = area.Width > 0 ? (Left - area.Left) / area.Width * 100.0 : 0;
            double py = area.Height > 0 ? ((Top + Height) - area.Top) / area.Height * 100.0 : 0;
            px = Math.Clamp(px, 0, 100);
            py = Math.Clamp(py, 0, 100);
            return (Left, Top, px, py);
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            if (_hwndSource != null)
                _hwndSource.RemoveHook(WndProc);
            CompositionTarget.Rendering -= OnRendering;
            base.OnClosed(e);
        }
    }
}