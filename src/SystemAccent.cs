using System;
using System.Drawing;
using Microsoft.Win32;

namespace MotionPhotoStudio
{
    /// <summary>读取 Windows 个性化设置里的系统强调色，供界面「跟随系统」取色使用。</summary>
    internal static class SystemAccent
    {
        internal static string Read()
        {
            try
            {
                object value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "ColorizationColor", null);
                if (value is int argb)
                {
                    Color color = Color.FromArgb(argb);
                    if (color.A == 0) color = Color.FromArgb(255, color.R, color.G, color.B);
                    int spread = Math.Abs(color.R - color.G) + Math.Abs(color.G - color.B) + Math.Abs(color.R - color.B);
                    if (spread > 20) return ToHex(color);
                }
            }
            catch { }

            try
            {
                object palette = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentPalette", null);
                var bytes = palette as byte[];
                if (bytes != null && bytes.Length >= 20)
                {
                    // AccentPalette 为 8 组 BGRA，取中间一组作为强调色
                    const int offset = 16;
                    return string.Format("#{0:X2}{1:X2}{2:X2}", bytes[offset + 2], bytes[offset + 1], bytes[offset]);
                }
            }
            catch { }

            return "#6750A4";
        }

        internal static bool IsDarkAppTheme()
        {
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", null);
                return value is int i && i == 0;
            }
            catch { return false; }
        }

        private static string ToHex(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }
    }
}
