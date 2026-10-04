using System;
using System.Runtime.InteropServices;

namespace ActiveDot
{
    /// <summary>
    /// 获取当前主显示器刷新率。
    /// 首选 User32.EnumDisplaySettings（零依赖、稳定），
    /// 并支持从逻辑刷新率推导整数值。
    /// </summary>
    public static class DisplayHelper
    {
        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int ENUM_REGISTRY_SETTINGS = -2;

        /// <summary>获取当前主显示器的刷新率（单位 Hz，向上取整）。失败返回 0。</summary>
        public static int GetCurrentRefreshRate()
        {
            var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };

            // 优先读取注册表设置（恒定模式下的刷新率）
            if (EnumDisplaySettings(null, ENUM_REGISTRY_SETTINGS, ref mode))
            {
                int rate = ToIntHertz(mode.dmDisplayFrequency);
                if (rate > 0) return rate;
            }

            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref mode))
            {
                int rate = ToIntHertz(mode.dmDisplayFrequency);
                if (rate > 0) return rate;
            }

            return 0;
        }

        /// <summary>从 displayFrequency 推导整数 Hz。真实值可能为 59、59.94、60 等。</summary>
        private static int ToIntHertz(int frequency)
        {
            if (frequency <= 0) return 0;

            // 某些多显示器下返回 1，表示"无输出"，忽略
            if (frequency == 1) return 0;

            // 处理 59.94 -> 60，119.88 -> 120 等非整数值（直接向上取整即可）
            var rounded = (int)Math.Round(frequency / 60.0) * 60;
            // 如果接近标准倍数则采用，否则用原始值
            if (Math.Abs(rounded - frequency) <= 1)
                return rounded;

            return (int)Math.Round((double)frequency);
        }

        /// <summary>获取整个显示器区域（含任务栏覆盖区），用于定位心跳窗口。</summary>
        public static System.Windows.Rect GetPrimaryScreenArea()
        {
            var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            return new System.Windows.Rect(b.X, b.Y, b.Width, b.Height);
        }

        /// <summary>
        /// 采样屏幕上某矩形区域的平均颜色（物理像素坐标）。
        /// 用于把"半透明色块"合成到真实背景色上，从而让心跳窗口本身不透明、走硬件合成。
        /// </summary>
        public static System.Drawing.Color SampleRegion(int x, int y, int w, int h)
        {
            w = Math.Max(1, w);
            h = Math.Max(1, h);
            try
            {
                using (var bmp = new System.Drawing.Bitmap(w, h))
                {
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(x, y, 0, 0, bmp.Size,
                            System.Drawing.CopyPixelOperation.SourceCopy);
                    }
                    long r = 0, gv = 0, b = 0;
                    int n = 0;
                    for (int j = 0; j < h; j++)
                        for (int i = 0; i < w; i++)
                        {
                            var c = bmp.GetPixel(i, j);
                            r += c.R; gv += c.G; b += c.B; n++;
                        }
                    if (n > 0)
                        return System.Drawing.Color.FromArgb((int)(r / n), (int)(gv / n), (int)(b / n));
                }
            }
            catch
            {
                // 采样失败回退到中性灰，不影响心跳运行
            }
            return System.Drawing.Color.FromArgb(160, 160, 160);
        }

        /// <summary>获取工作区（不含任务栏）的可用区域，用于定位心跳窗口到屏幕左下角。</summary>
        public static System.Windows.Rect GetPrimaryWorkArea()
        {
            var rect = new RECT();
            SystemParametersHelper.GetWorkArea(ref rect);
            return new System.Windows.Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        #region Win32

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        // 使用内联类避免与 SystemParameters 命名冲突
        private static class SystemParametersHelper
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            public static extern bool SystemParametersInfo(int uiAction, int uiParam, ref RECT pvParam, int fWinIni);
            private const int SPI_GETWORKAREA = 0x0030;
            public static void GetWorkArea(ref RECT rect)
            {
                rect = new RECT();
                SystemParametersInfo(SPI_GETWORKAREA, 0, ref rect, 0);
            }
        }

        #endregion
    }
}