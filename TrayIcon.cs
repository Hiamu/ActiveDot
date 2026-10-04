using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ActiveDot
{
    /// <summary>系统托盘集成：右键菜单 + 双击打开设置。</summary>
    public class TrayIcon : IDisposable
    {
        private NotifyIcon _icon;
        private AppConfig _config;
        private HeartbeatWindow _heartbeat;
        private SettingsWindow _settings;
        private Action _onExit;
        private ContextMenuStrip _menu;

        public TrayIcon(AppConfig config, HeartbeatWindow heartbeat, SettingsWindow settings, Action onExit)
        {
            _config = config;
            _heartbeat = heartbeat;
            _settings = settings;
            _onExit = onExit;

            _menu = new ContextMenuStrip();
            _menu.Items.Add("显示设置", null, (s, e) => _settings.ShowPanel());
            _menu.Items.Add("启用闪烁", null, OnToggleEnabled);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("退出", null, (s, e) => _onExit?.Invoke());

            _icon = new NotifyIcon
            {
                Icon = CreateIcon(),
                Text = "ActiveDot",
                ContextMenuStrip = _menu,
                Visible = true
            };
            _icon.DoubleClick += (s, e) => _settings.ShowPanel();

            RefreshMenuText();
        }

        private void OnToggleEnabled(object sender, EventArgs e)
        {
            _config.Enabled = !_config.Enabled;
            _heartbeat.SetEnabled(_config.Enabled);
            SettingsStore.Save(_config);
            RefreshMenuText();
        }

        private void RefreshMenuText()
        {
            _menu.Items[1].Text = _config.Enabled ? "禁用闪烁" : "启用闪烁";
        }

        /// <summary>程序退出时清理托盘图标。</summary>
        public void Dispose()
        {
            if (_icon != null)
            {
                _icon.Visible = false;
                _icon.Dispose();
                _icon = null;
            }
            _menu?.Dispose();
            _menu = null;
        }

        /// <summary>程序图标：优先加载 exe 同目录的 ActiveDot.ico（可自行替换），否则用内置绘制图标。</summary>
        public static Icon CreateIcon()
        {
            try
            {
                var ico = Path.Combine(AppContext.BaseDirectory, "ActiveDot.ico");
                if (File.Exists(ico))
                    return new Icon(ico);
            }
            catch
            {
                // 图标文件损坏时回退到内置
            }
            return DrawDefaultIcon();
        }

        private static Icon DrawDefaultIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);          // 黑色背景
                    using (var red = new SolidBrush(Color.FromArgb(255, 220, 40, 40)))
                    {
                        // 画一个"心跳"曲线点
                        g.FillEllipse(red, 4, 4, 24, 24);
                    }
                    using (var white = new SolidBrush(Color.White))
                    {
                        // 心电脉冲折线
                        PointF[] pts =
                        {
                            new PointF(6,16), new PointF(12,16), new PointF(14,12),
                            new PointF(16,21), new PointF(18,9), new PointF(20,16), new PointF(26,16)
                        };
                        using (var pen = new Pen(Color.White, 2.2f))
                        {
                            g.DrawLines(pen, pts);
                        }
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        /// <summary>生成 WPF 可用的图标源（供设置窗口标题栏/任务栏使用）。</summary>
        public static System.Windows.Media.ImageSource CreateAppImageSource()
        {
            var hico = CreateIcon().Handle;
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                hico,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        }
    }
}