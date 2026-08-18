using System;
using System.Windows.Interop;

namespace MouseCursorTrap.Interop
{
    /// <summary>
    /// 애플리케이션이 살아 있는 동안 유지되는 숨김 창.
    /// 설정 창보다 오래 살아야 하는 RegisterHotKey 대상 HWND이자,
    /// WM_DISPLAYCHANGE / WM_DPICHANGED 수신 창이다.
    ///
    /// 주의: message-only 창(HWND_MESSAGE)은 브로드캐스트 메시지를 받지 못한다.
    /// WM_DISPLAYCHANGE가 브로드캐스트이므로 반드시 실제 최상위 창이어야 한다.
    /// 화면에 나타나지 않게 하려고 WS_VISIBLE을 빼고, 화면 밖에 두고,
    /// Alt+Tab 목록에서 감추려고 WS_EX_TOOLWINDOW를 준다.
    /// </summary>
    internal sealed class MessageWindow : IDisposable
    {
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private HwndSource _source;

        /// <summary>
        /// 반드시 UI(Dispatcher) 스레드에서 생성해야 한다.
        /// WM_HOTKEY가 이 스레드의 메시지 큐로 들어와야 하기 때문이다.
        /// </summary>
        public MessageWindow()
        {
            HwndSourceParameters parameters = new HwndSourceParameters("MouseCursorTrap.MessageWindow");
            parameters.Width = 1;
            parameters.Height = 1;
            parameters.PositionX = -10000;
            parameters.PositionY = -10000;
            // 기본값에 WS_VISIBLE이 섞이지 않도록 스타일을 명시한다.
            parameters.WindowStyle = WS_POPUP;
            parameters.ExtendedWindowStyle = WS_EX_TOOLWINDOW;
            parameters.ParentWindow = IntPtr.Zero;
            parameters.UsesPerPixelOpacity = false;
            parameters.AcquireHwndFocusInMenuMode = false;

            _source = new HwndSource(parameters);
            // 렌더링할 내용이 없으므로 RootVisual은 null로 둔다.
            _source.RootVisual = null;
        }

        public IntPtr Handle
        {
            get { return _source == null ? IntPtr.Zero : _source.Handle; }
        }

        public void AddHook(HwndSourceHook hook)
        {
            if (_source != null)
            {
                _source.AddHook(hook);
            }
        }

        public void RemoveHook(HwndSourceHook hook)
        {
            if (_source != null)
            {
                _source.RemoveHook(hook);
            }
        }

        public void Dispose()
        {
            if (_source != null)
            {
                _source.Dispose();
                _source = null;
            }
        }
    }
}
