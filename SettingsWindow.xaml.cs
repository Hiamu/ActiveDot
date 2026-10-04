using System;
using System.Windows;
using System.Windows.Controls;

namespace ActiveDot
{
    /// <summary>
    /// 设置控制面板：实时调整心跳窗口的各项参数，所有更改立即生效并自动保存。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        /// <summary>拖动模式结束后触发，参数为 (物理X, 物理Y, 百分比X, 百分比Y)。</summary>
        public static Action<(double X, double Y, double PX, double PY)> NotifyDragEnded;

        private AppConfig _config;
        private HeartbeatWindow _heartbeat;
        private bool _loading = true; // 构造期间抑制事件

        public SettingsWindow(AppConfig config, HeartbeatWindow heartbeat)
        {
            InitializeComponent();
            _config = config;
            _heartbeat = heartbeat;

            // 标题栏与任务栏图标
            Icon = TrayIcon.CreateAppImageSource();

            LoadFromConfig();
            _loading = false;

            // 拖动结束后同步位置
            NotifyDragEnded += pos =>
            {
                if (!IsVisible) return;
                _config.PosX = (int)pos.X;
                _config.PosY = (int)pos.Y; // 窗口左上角
                _config.UsePercentPosition = false;
                Dispatcher.BeginInvoke(new Action(() => { LoadFromConfig(); }));
                Save();
            };
        }

        public void ShowPanel()
        {
            LoadFromConfig();
            if (IsVisible) { Activate(); return; }
            Show();
        }

        private void LoadFromConfig()
        {
            ckEnabled.IsChecked = _config.Enabled;
            txtWidth.Text = _config.Width.ToString();
            txtHeight.Text = _config.Height.ToString();
            txtX.Text = _config.PosX.ToString();
            txtY.Text = _config.PosY.ToString();
            ckDrag.IsChecked = _config.DragMode;
            sldDelta.Value = _config.BrightnessDelta;
            lblDelta.Text = _config.BrightnessDelta.ToString();
            txtHz.Text = _config.HeartbeatHz.ToString();
            ckAutoStart.IsChecked = StartUpUtility.IsEnabled();
        }

        private void Save()
        {
            SettingsStore.Save(_config);
        }

        #region 事件处理

        private void CkEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool en = ckEnabled.IsChecked == true;
            _config.Enabled = en;
            _heartbeat.SetEnabled(en);
            Save();
        }

        private void SldDelta_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            int v = (int)sldDelta.Value;
            lblDelta.Text = v.ToString();
            _config.BrightnessDelta = v;
            _heartbeat.ApplyOverlayIntensity(v); // 实时调整黑白闪烁的明显程度
            Save();
        }

        private void TxtHz_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            if (int.TryParse(txtHz.Text, out int hz))
            {
                hz = Math.Clamp(hz, 1, 100);
                _config.HeartbeatHz = hz;
                _heartbeat.ApplyHeartbeatRate(hz);
                Save();
            }
        }

        private void CkDrag_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _config.DragMode = ckDrag.IsChecked == true;
            _heartbeat.ApplyDragMode(_config.DragMode);
            Save();
        }

        /// <summary>点击位置输入框时自动退出可拖动模式，便于手动输入坐标。</summary>
        private void TxtPos_GotFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (_loading) return;
            if (_config.DragMode)
            {
                _config.DragMode = false;
                ckDrag.IsChecked = false;
                _heartbeat.ApplyDragMode(false);
                Save();
            }
        }

        private void CkAutoStart_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _config.AutoStart = ckAutoStart.IsChecked == true;
            if (_config.AutoStart) StartUpUtility.Enable(); else StartUpUtility.Disable();
            Save();
        }

        /// <summary>保存并应用窗口尺寸与位置（尺寸/位置变化后重采样背景）。</summary>
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // 尺寸
            int.TryParse(txtWidth.Text, out int w);
            int.TryParse(txtHeight.Text, out int h);
            w = Math.Clamp(w, 1, 64);
            h = Math.Clamp(h, 1, 64);
            _config.Width = w;
            _config.Height = h;
            _heartbeat.ApplySize();

            // 位置（仅非拖动模式）
            if (ckDrag.IsChecked != true)
            {
                int.TryParse(txtX.Text, out int x);
                int.TryParse(txtY.Text, out int y);
                _config.PosX = x;
                _config.PosY = y;
                _config.UsePercentPosition = false;
                _heartbeat.ApplyPosition();
            }

            // 亮度增量也同步应用一次（保证保存后滑块与实时闪烁强度一致）
            _heartbeat.ApplyOverlayIntensity(_config.BrightnessDelta);

            Save();
            LoadFromConfig();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 关闭设置面板不退出程序，仅隐藏
            e.Cancel = true;
            Hide();
        }

        #endregion
    }
}