using System;
using System.Windows;

namespace ActiveDot
{
    /// <summary>
    /// 应用入口。启动后不显示主窗口，而是创建托盘图标和心跳窗口。
    /// </summary>
    public partial class App : Application
    {
        private HeartbeatWindow _heartbeat;
        private SettingsWindow _settings;
        private TrayIcon _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 只允许单实例运行
            if (!SingleInstance.EnsureSingleInstance())
            {
                Shutdown();
                return;
            }

            // 加载配置
            var config = SettingsStore.Load();
            // 启动即规范化落盘：把内存中当前格式的最新配置重写回磁盘，
            // 避免旧版本遗留的陈旧配置导致"重进后变回旧值"。
            SettingsStore.Save(config);

            // 创建心跳窗口（先设置启用状态以控制显隐）
            _heartbeat = new HeartbeatWindow(config);
            _heartbeat.SetEnabled(config.Enabled);
            _heartbeat.ApplyDragMode(config.DragMode);

            // 创建设置面板（不显示，仅在托盘打开时显示）
            _settings = new SettingsWindow(config, _heartbeat);

            // 创建托盘
            _tray = new TrayIcon(config, _heartbeat, _settings, OnExit);

            // 是否开机自启（应用配置）
            if (config.AutoStart)
                StartUpUtility.Enable();
            else
                StartUpUtility.Disable();
        }

        private void OnExit()
        {
            _tray?.Dispose();
            _settings?.Close();
            _heartbeat?.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            base.OnExit(e);
        }
    }
}