using System;
using System.Runtime.InteropServices;

namespace MouseCursorTrap.Interop
{
    /// <summary>
    /// 이 애플리케이션이 사용하는 모든 P/Invoke 선언과 관련 상수, 구조체.
    /// </summary>
    internal static class NativeMethods
    {
        // ------------------------------------------------------------------
        // 구조체
        // ------------------------------------------------------------------

        /// <summary>
        /// int 4개짜리 blittable 구조체. ClipCursor/GetClipCursor/GetWindowRect가 공유한다.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }

            public bool IsEmptyArea { get { return Right <= Left || Bottom <= Top; } }

            public static bool AreEqual(RECT a, RECT b)
            {
                return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        // ------------------------------------------------------------------
        // 커서 영역 제한
        // ------------------------------------------------------------------

        // ClipCursor는 const RECT*를 받고 NULL이면 해제한다.
        // C# 7.3에서는 ref 매개변수에 null을 넘길 수 없으므로 진입점을 둘로 나눈다.
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClipCursor([In] ref RECT lpRect);

        /// <summary>커서 제한 해제 전용. 항상 IntPtr.Zero를 넘긴다.</summary>
        [DllImport("user32.dll", EntryPoint = "ClipCursor", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClipCursorRelease(IntPtr lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClipCursor(out RECT lpRect);

        // ------------------------------------------------------------------
        // 모니터 정보
        // ------------------------------------------------------------------

        internal const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int nIndex);

        internal const int SM_CXSCREEN = 0;
        internal const int SM_CYSCREEN = 1;
        internal const int SM_XVIRTUALSCREEN = 76;
        internal const int SM_YVIRTUALSCREEN = 77;
        internal const int SM_CXVIRTUALSCREEN = 78;
        internal const int SM_CYVIRTUALSCREEN = 79;

        /// <summary>주 디스플레이의 전체 영역(작업 영역이 아님)을 물리 픽셀로 돌려준다.</summary>
        internal static RECT GetPrimaryMonitorRect()
        {
            POINT origin;
            origin.X = 0;
            origin.Y = 0;

            IntPtr monitor = MonitorFromPoint(origin, MONITOR_DEFAULTTOPRIMARY);
            if (monitor != IntPtr.Zero)
            {
                MONITORINFO info = new MONITORINFO();
                // cbSize를 채우지 않으면 GetMonitorInfo는 항상 실패한다.
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(monitor, ref info))
                {
                    // 작업 표시줄까지 포함해야 하므로 rcWork가 아니라 rcMonitor를 쓴다.
                    return info.rcMonitor;
                }
            }

            // 폴백: 주 디스플레이는 항상 원점에서 시작한다.
            RECT fallback;
            fallback.Left = 0;
            fallback.Top = 0;
            fallback.Right = GetSystemMetrics(SM_CXSCREEN);
            fallback.Bottom = GetSystemMetrics(SM_CYSCREEN);
            return fallback;
        }

        /// <summary>
        /// 모든 디스플레이를 감싸는 가상 화면 영역.
        /// 커서 제한이 걸려 있지 않을 때 GetClipCursor가 돌려주는 값이기도 하다.
        /// </summary>
        internal static RECT GetVirtualScreenRect()
        {
            RECT r;
            r.Left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            r.Top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            r.Right = r.Left + GetSystemMetrics(SM_CXVIRTUALSCREEN);
            r.Bottom = r.Top + GetSystemMetrics(SM_CYVIRTUALSCREEN);
            return r;
        }

        // ------------------------------------------------------------------
        // 전역 단축키
        // ------------------------------------------------------------------

        internal const uint MOD_ALT = 0x0001;
        internal const uint MOD_CONTROL = 0x0002;
        internal const uint MOD_SHIFT = 0x0004;
        internal const uint MOD_WIN = 0x0008;
        internal const uint MOD_NOREPEAT = 0x4000;

        internal const uint VK_F12 = 0x7B;

        internal const int WM_HOTKEY = 0x0312;
        internal const int WM_DISPLAYCHANGE = 0x007E;
        internal const int WM_DPICHANGED = 0x02E0;

        internal const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // ------------------------------------------------------------------
        // WinEvent 훅 (포그라운드 전환 감지)
        // ------------------------------------------------------------------

        internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        internal delegate void WinEventProc(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        // ------------------------------------------------------------------
        // 창 위치 / 포그라운드
        // ------------------------------------------------------------------

        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();
    }
}
