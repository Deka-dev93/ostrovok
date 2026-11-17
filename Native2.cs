using System.Runtime.InteropServices;

namespace Ostrovok;

static partial class Native
{
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);

    /// <summary>Скрыть / показать значки рабочего стола (панель задач остаётся).</summary>
    public static bool ToggleDesktopIcons()
    {
        IntPtr list = IntPtr.Zero;
        var progman = FindWindow("Progman", null);
        var view = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (view != IntPtr.Zero) list = FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
        if (list == IntPtr.Zero)
        {
            // у Windows 10/11 значки могут жить внутри WorkerW
            EnumWindows((h, _) =>
            {
                var v = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (v != IntPtr.Zero) list = FindWindowEx(v, IntPtr.Zero, "SysListView32", null);
                return list == IntPtr.Zero;
            }, IntPtr.Zero);
        }
        if (list == IntPtr.Zero) return false;
        bool visible = IsWindowVisible(list);
        ShowWindow(list, visible ? 0 : 5);
        return true;
    }
}
