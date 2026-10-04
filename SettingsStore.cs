using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ActiveDot
{
    /// <summary>配置模型，持久化到用户目录下的 JSON 文件。</summary>
    public class AppConfig
    {
        public bool Enabled { get; set; } = true;

        // 尺寸
        public int Width { get; set; } = 2;
        public int Height { get; set; } = 2;

        // 位置（% 模式存储以兼容不同分辨率）
        public bool UsePercentPosition { get; set; } = true;
        public double PosXPercent { get; set; } = 0.0;   // 距左 0%
        public double PosYPercent { get; set; } = 0.0; // 距顶 100%（即左下角）
        public int PosX { get; set; } = 0;
        public int PosY { get; set; } = 0;
        public bool DragMode { get; set; } = false;

        // 亮度增量 1~10
        public int BrightnessDelta { get; set; } = 1;

        // 心跳更新频率：每秒改变多少次亮度（可手动输入，上限 100）。
        // 静态检测只需画面有"变化"即可被欺骗；频率上限受显示器 vsync 制约。
        public int HeartbeatHz { get; set; } = 30;

        // 开机自启
        public bool AutoStart { get; set; } = false;
    }

    /// <summary>配置的加载、保存与参数校验。</summary>
    public static class SettingsStore
    {
        // 便携式：配置文件跟可执行文件放同一目录，解压即用，无需写入用户文档目录。
        public static string ConfigPath =>
            Path.Combine(AppContext.BaseDirectory, "config.json");

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (cfg != null)
                    {
                        Sanitize(cfg);
                        return cfg;
                    }
                }
            }
            catch
            {
                // 配置损坏时回退到默认
            }
            return NewDefault();
        }

        public static void Save(AppConfig cfg)
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json, Encoding.UTF8);
            }
            catch
            {
                // 写失败忽略（不影响运行时）
            }
        }

        public static AppConfig NewDefault() => new AppConfig();

        private static void Sanitize(AppConfig cfg)
        {
            cfg.Width = Clamp(cfg.Width, 1, 64);
            cfg.Height = Clamp(cfg.Height, 1, 64);
            cfg.BrightnessDelta = Clamp(cfg.BrightnessDelta, 1, 10);
            cfg.PosXPercent = Math.Clamp(cfg.PosXPercent, 0.0, 100.0);
            cfg.PosYPercent = Math.Clamp(cfg.PosYPercent, 0.0, 100.0);
            cfg.HeartbeatHz = Clamp(cfg.HeartbeatHz, 1, 100);
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}