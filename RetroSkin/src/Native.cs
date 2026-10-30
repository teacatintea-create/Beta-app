using System;
using System.Runtime.InteropServices;

namespace RetroSkin
{
    static class Native
    {
        const uint SPI_SETDESKWALLPAPER = 0x0014;
        const uint SPI_SETANIMATION = 0x0049;
        const uint SPI_SETCLIENTAREAANIMATION = 0x1043;
        const uint SPIF_UPDATEINIFILE = 0x01;
        const uint SPIF_SENDCHANGE = 0x02;

        const uint WM_SETTINGCHANGE = 0x001A;
        const uint SMTO_ABORTIFHUNG = 0x0002;
        static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

        const int SHCNE_ASSOCCHANGED = 0x08000000;

        [StructLayout(LayoutKind.Sequential)]
        struct ANIMATIONINFO
        {
            public uint cbSize;
            public int iMinAnimate;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SystemParametersInfo(uint action, uint param, string pvParam, uint winIni);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint action, uint param, ref ANIMATIONINFO pvParam, uint winIni);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint action, uint param, IntPtr pvParam, uint winIni);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
            uint flags, uint timeout, out UIntPtr result);

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);

        /// <summary>" " отключает визуальный стиль окна (классический вид), null возвращает стандартный.</summary>
        public static void SetClassicLook(IntPtr hWnd, bool classic)
        {
            try { SetWindowTheme(hWnd, classic ? " " : null, classic ? " " : null); }
            catch (DllNotFoundException) { /* без uxtheme просто остаётся системный вид */ }
        }

        public static bool SetWallpaper(string path) =>
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path ?? "", SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

        public static void SetAnimations(bool enabled)
        {
            var info = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>(), iMinAnimate = enabled ? 1 : 0 };
            SystemParametersInfo(SPI_SETANIMATION, info.cbSize, ref info, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, new IntPtr(enabled ? 1 : 0), SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }

        /// <summary>Сообщает всем окнам, что настройки изменились (цвета, панель задач, значки).</summary>
        public static void BroadcastSettingsChange()
        {
            foreach (var area in new[] { "ImmersiveColorSet", "WindowsThemeElement", "Environment", null })
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, area, SMTO_ABORTIFHUNG, 1000, out _);
            SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
