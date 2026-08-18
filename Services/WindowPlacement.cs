using System;
using System.Windows;
using System.Windows.Interop;
using MouseCursorTrap.Interop;

namespace MouseCursorTrap.Services
{
    /// <summary>창 위치와 포그라운드 전환을 다루는 도우미.</summary>
    internal static class WindowPlacement
    {
        /// <summary>
        /// 창을 주 디스플레이 정가운데로 옮긴다.
        ///
        /// 계산은 전부 물리 픽셀에서 한다. WPF DIP로 환산하면 그 배율이 "창이 지금 놓인
        /// 모니터"의 DPI라서, PerMonitorV2에서는 창을 옮기는 순간 값이 달라지는 순환이 생긴다.
        /// GetWindowRect / SetWindowPos는 GetMonitorInfo, ClipCursor와 같은 좌표계를 쓴다.
        /// </summary>
        public static void CenterOnPrimary(Window window)
        {
            if (window == null)
            {
                return;
            }

            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.RECT windowRect;
            if (!NativeMethods.GetWindowRect(handle, out windowRect))
            {
                return;
            }

            NativeMethods.RECT monitor = NativeMethods.GetPrimaryMonitorRect();

            int x = monitor.Left + ((monitor.Width - windowRect.Width) / 2);
            int y = monitor.Top + ((monitor.Height - windowRect.Height) / 2);

            NativeMethods.SetWindowPos(
                handle, IntPtr.Zero, x, y, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }

        /// <summary>
        /// 창을 확실히 앞으로 끌어올린다.
        ///
        /// Windows는 포그라운드를 소유하지 않은 프로세스의 SetForegroundWindow를 거부한다.
        /// 트레이 클릭이나 단축키, 두 번째 인스턴스 신호로 호출될 때가 정확히 그 상황이라
        /// 잠깐 입력 스레드를 붙여 제약을 우회한다.
        /// </summary>
        public static void ForceForeground(Window window)
        {
            if (window == null)
            {
                return;
            }

            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == handle)
            {
                return;
            }

            uint foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, IntPtr.Zero);
            uint currentThread = NativeMethods.GetCurrentThreadId();

            bool attached = false;
            try
            {
                if (foregroundThread != 0 && foregroundThread != currentThread)
                {
                    attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
                }

                NativeMethods.SetForegroundWindow(handle);
                NativeMethods.SetActiveWindow(handle);
            }
            finally
            {
                if (attached)
                {
                    NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
                }
            }
        }
    }
}
