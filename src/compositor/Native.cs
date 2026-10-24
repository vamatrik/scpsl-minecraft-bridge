using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SlBridge.Compositor
{
    internal static unsafe class Native
    {
        public const int WS_POPUP = unchecked((int)0x80000000), WS_VISIBLE = 0x10000000;
        public const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000,
                         WS_EX_NOACTIVATE = 0x8000000, WS_EX_NOREDIRECTIONBITMAP = 0x200000;
        public const uint LWA_ALPHA = 0x2;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_NOOWNERZORDER = 0x200;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_BOTTOM = new IntPtr(1);
        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        public const uint WM_DESTROY = 0x2, WM_HOTKEY = 0x312, WM_QUIT = 0x12, PM_REMOVE = 1;
        public const uint MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

        public delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; public IntPtr hIconSm;
        }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public int W => Right - Left; public int H => Bottom - Top; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [DllImport("user32")] public static extern bool GetCursorPos(out POINT p);
        [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

        [DllImport("user32", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassEx(ref WNDCLASSEX c);
        [DllImport("user32", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWindowEx(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32")] public static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32")] public static extern bool PeekMessage(out MSG m, IntPtr h, uint min, uint max, uint remove);
        [DllImport("user32")] public static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32")] public static extern IntPtr DispatchMessage(ref MSG m);
        [DllImport("user32")] public static extern void PostQuitMessage(int code);
        [DllImport("user32")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        /// <summary>3 = QUNS_RUNNING_D3D_FULL_SCREEN: на переднем плане игра в эксклюзивном полноэкранном режиме.</summary>
        [DllImport("shell32")] public static extern int SHQueryUserNotificationState(out int state);
        [DllImport("dwmapi")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("user32")] public static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32")] public static extern int SetWindowLong(IntPtr h, int idx, int value);
        [DllImport("user32")] public static extern int GetSystemMetrics(int idx);
        public const int SM_XVIRTUALSCREEN = 76, SM_CXVIRTUALSCREEN = 78;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const int WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000;
        public const uint SWP_FRAMECHANGED = 0x20;
        public static readonly IntPtr HWND_TOP = IntPtr.Zero;
        [DllImport("user32")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32")] public static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32")] public static extern IntPtr SetFocus(IntPtr h);
        [DllImport("user32")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("kernel32")] public static extern uint GetCurrentThreadId();
        [DllImport("user32")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        /// <summary>Перевести фокус на окно другого процесса (обход блокировки переднего плана Windows).</summary>
        public static void ForceForeground(IntPtr h)
        {
            if (h == IntPtr.Zero) return;
            IntPtr fg = GetForegroundWindow();
            if (fg == h) return;
            uint fgThread = GetWindowThreadProcessId(fg, out _);
            uint targetThread = GetWindowThreadProcessId(h, out _);
            uint me = GetCurrentThreadId();
            keybd_event(0x12, 0, 0, UIntPtr.Zero);      // Alt вниз — Windows разрешает смену фокуса
            keybd_event(0x12, 0, 2, UIntPtr.Zero);      // Alt вверх
            bool a1 = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            bool a2 = targetThread != 0 && targetThread != me && AttachThreadInput(me, targetThread, true);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            SetFocus(h);
            if (a1) AttachThreadInput(me, fgThread, false);
            if (a2) AttachThreadInput(me, targetThread, false);
        }
        [DllImport("dwmapi")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("user32")] public static extern bool AdjustWindowRectEx(ref RECT r, int style, bool menu, int exStyle);
        [DllImport("user32")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("kernel32", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);

        // WinRT / capture interop
        [DllImport("combase", PreserveSig = true)] public static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string s, int len, out IntPtr hstring);
        [DllImport("combase", PreserveSig = true)] public static extern int WindowsDeleteString(IntPtr hstring);
        [DllImport("combase", PreserveSig = true)] public static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
        [DllImport("d3d11", PreserveSig = true)] public static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr inspectable);

        public static string WindowText(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }
        public static string ClassName(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }

        public static string ProcessName(IntPtr h)
        {
            GetWindowThreadProcessId(h, out uint pid);
            try { using var p = Process.GetProcessById((int)pid); return p.ProcessName; } catch { return ""; }
        }

        /// <summary>Клиентская область окна в экранных координатах.</summary>
        public static RECT ClientScreenRect(IntPtr h)
        {
            GetClientRect(h, out var r);
            var p = new POINT();
            ClientToScreen(h, ref p);
            return new RECT { Left = p.X, Top = p.Y, Right = p.X + r.W, Bottom = p.Y + r.H };
        }

        public static IntPtr FindWindow(Func<IntPtr, bool> pred)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, _) =>
            {
                if (IsWindowVisible(h) && pred(h)) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }
}
