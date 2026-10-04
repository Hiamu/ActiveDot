using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ActiveDot
{
    /// <summary>
    /// 开机自启：通过注册表 HKCU Run 键实现，无需管理员权限。
    /// </summary>
    public static class StartUpUtility
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "ActiveDot";

        /// <summary>当前是否为已设为开机自启。</summary>
        public static bool IsEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    return key?.GetValue(ValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void Enable()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath, true))
                {
                    key?.SetValue(ValueName, GetLaunchCommand());
                }
            }
            catch { }
        }

        public static void Disable()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    key?.DeleteValue(ValueName, false);
                }
            }
            catch { }
        }

        private static string GetLaunchCommand()
        {
            // 使用当前可执行文件的带引号完整路径（同时处理便携运行情况）
            var exePath = ProcessPath();
            return $"\"{exePath}\"";
        }

        private static string ProcessPath()
        {
            try
            {
                var path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path))
                    return Path.GetFullPath(path);
            }
            catch { }
            return System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";
        }
    }
}