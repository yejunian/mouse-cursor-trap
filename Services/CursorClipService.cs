using System;
using System.Globalization;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using MouseCursorTrap.Interop;
using MouseCursorTrap.Models;

namespace MouseCursorTrap.Services
{
    /// <summary>
    /// 커서 이동 범위를 주 디스플레이로 제한하고, 그 상태를 유지한다.
    ///
    /// Windows는 포그라운드 전환, Win+L, UAC, 세션 전환 등에서 ClipCursor를 조용히 해제한다.
    /// 그래서 감시 타이머와 포그라운드 이벤트 훅으로 해제를 감지해 다시 건다.
    /// 다만 다른 프로그램이 자기 영역을 설정한 경우에는 덮어쓰지 않고 제한을 끈다.
    /// </summary>
    internal sealed class CursorClipService : IDisposable
    {
        private const double WatchdogIntervalMilliseconds = 250;
        private const double ForegroundRecheckMilliseconds = 60;

        private readonly Dispatcher _dispatcher;
        private readonly MessageWindow _messageWindow;
        private readonly DispatcherTimer _watchdogTimer;
        private readonly DispatcherTimer _foregroundRecheckTimer;
        private readonly HwndSourceHook _messageHook;
        private readonly EventHandler _displaySettingsChangedHandler;

        // 훅이 살아 있는 동안 델리게이트가 수거되면 CallbackOnCollectedDelegate로 죽는다.
        // 반드시 필드로 붙잡아 둔다.
        private readonly NativeMethods.WinEventProc _winEventProc;
        private IntPtr _winEventHook;

        private ClipState _state;
        private ClipOffReason _offReason;
        private NativeMethods.RECT _desired;
        private bool _disposed;

        /// <summary>상태나 제한 영역이 실제로 바뀌었을 때만 발생한다.</summary>
        public event EventHandler StateChanged;

        public CursorClipService(MessageWindow messageWindow)
        {
            _messageWindow = messageWindow;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _state = ClipState.Off;
            _offReason = ClipOffReason.None;

            // 기본 우선순위가 Background면 UI 부하가 걸릴 때 굶는데,
            // 하필 그때가 포그라운드 전환이 잦은 시점이다.
            _watchdogTimer = new DispatcherTimer(DispatcherPriority.Normal);
            _watchdogTimer.Interval = TimeSpan.FromMilliseconds(WatchdogIntervalMilliseconds);
            _watchdogTimer.Tick += OnWatchdogTick;

            _foregroundRecheckTimer = new DispatcherTimer(DispatcherPriority.Normal);
            _foregroundRecheckTimer.Interval = TimeSpan.FromMilliseconds(ForegroundRecheckMilliseconds);
            _foregroundRecheckTimer.Tick += OnForegroundRecheckTick;

            _messageHook = OnWindowMessage;
            if (_messageWindow != null)
            {
                _messageWindow.AddHook(_messageHook);
            }

            _displaySettingsChangedHandler = OnDisplaySettingsChanged;
            SystemEvents.DisplaySettingsChanged += _displaySettingsChangedHandler;

            _winEventProc = OnForegroundChanged;
            _winEventHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _winEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        }

        public ClipState State { get { return _state; } }

        public ClipOffReason OffReason { get { return _offReason; } }

        public bool IsOn { get { return _state == ClipState.On; } }

        /// <summary>디스플레이 구성이 바뀌어 제한이 자동 해제되었을 때 발생한다.</summary>
        public event EventHandler DisplayConfigurationChanged;

        /// <summary>
        /// 트레이 메뉴와 설정 창이 함께 쓰는 상태 문구.
        /// 두 곳의 표기가 어긋나지 않게 하려고 한 군데에서만 만든다.
        /// </summary>
        public string DescribeRegion()
        {
            if (_state == ClipState.Off)
            {
                return "영역 제한: 없음";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "영역 제한: ({0}, {1}) {2}×{3}",
                _desired.Left, _desired.Top, _desired.Width, _desired.Height);
        }

        // ------------------------------------------------------------------
        // 켜기 / 끄기
        // ------------------------------------------------------------------

        public void Enable()
        {
            if (_disposed)
            {
                return;
            }

            // 해상도가 바뀌었을 수 있으므로 켤 때마다 새로 계산한다.
            NativeMethods.RECT rect = NativeMethods.GetPrimaryMonitorRect();
            if (rect.IsEmptyArea)
            {
                return;
            }

            _desired = rect;
            _state = ClipState.On;
            _offReason = ClipOffReason.None;

            ApplyClip();
            _watchdogTimer.Start();
            RaiseStateChanged();
        }

        public void Disable()
        {
            DisableCore(ClipOffReason.None, true);
        }

        private void DisableCore(ClipOffReason reason, bool releaseOwnedClip)
        {
            if (_state == ClipState.Off && _offReason == reason)
            {
                return;
            }

            _watchdogTimer.Stop();
            _foregroundRecheckTimer.Stop();

            if (releaseOwnedClip)
            {
                ReleaseOwnedClip();
            }

            _state = ClipState.Off;
            _offReason = reason;
            RaiseStateChanged();
        }

        /// <summary>우리가 건 제한만 해제한다. 다른 프로그램의 영역은 건드리지 않는다.</summary>
        private void ReleaseOwnedClip()
        {
            NativeMethods.RECT current;
            if (NativeMethods.GetClipCursor(out current)
                && NativeMethods.RECT.AreEqual(current, _desired))
            {
                NativeMethods.ClipCursorRelease(IntPtr.Zero);
            }
        }

        private void ApplyClip()
        {
            NativeMethods.RECT rect = _desired;
            NativeMethods.ClipCursor(ref rect);
        }

        // ------------------------------------------------------------------
        // 감시
        // ------------------------------------------------------------------

        private void OnWatchdogTick(object sender, EventArgs e)
        {
            CheckAndReapply();
        }

        private void OnForegroundRecheckTick(object sender, EventArgs e)
        {
            _foregroundRecheckTimer.Stop();
            CheckAndReapply();
        }

        private void CheckAndReapply()
        {
            if (_disposed || _state == ClipState.Off)
            {
                return;
            }

            NativeMethods.RECT current;
            if (!NativeMethods.GetClipCursor(out current))
            {
                // 일시적 실패. 다음 틱에 다시 본다.
                return;
            }

            if (NativeMethods.RECT.AreEqual(current, _desired))
            {
                return;
            }

            if (NativeMethods.RECT.AreEqual(current, NativeMethods.GetVirtualScreenRect()))
            {
                // Windows가 조용히 풀었다. 다시 건다.
                // (모니터가 하나뿐이면 이 영역과 우리 영역이 같아 구분되지 않지만,
                //  그때는 두 상태의 실제 효과가 같으므로 문제가 되지 않는다.)
                ApplyClip();
                return;
            }

            // 그 밖의 영역은 다른 프로그램이 의도적으로 설정한 것이다.
            // 덮어쓰면 서로 재적용을 반복해 커서가 떨리므로 자리를 비켜 준다.
            DisableCore(ClipOffReason.ForeignClip, false);
        }

        private void OnForegroundChanged(
            IntPtr hookHandle, uint eventType, IntPtr hwnd,
            int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (_disposed || _state == ClipState.Off)
            {
                return;
            }

            CheckAndReapply();

            // WinEvent 통지와 OS 내부의 제한 해제 사이에 순서 보장이 없다.
            // 즉시 확인만으로는 놓칠 수 있어 한 번 더 본다.
            _foregroundRecheckTimer.Stop();
            _foregroundRecheckTimer.Start();
        }

        // ------------------------------------------------------------------
        // 디스플레이 구성 변경
        // ------------------------------------------------------------------

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            // SystemEvents는 자기 전용 스레드에서 이벤트를 낸다.
            // DispatcherTimer나 UI 상태를 그 스레드에서 만지면 조용히 깨진다.
            _dispatcher.BeginInvoke(new Action(HandleDisplayConfigurationChanged));
        }

        private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_DISPLAYCHANGE || msg == NativeMethods.WM_DPICHANGED)
            {
                // WM_DPICHANGED는 SystemEvents가 놓칠 수 있는 배율 변경을 메워 준다.
                // 핸들러는 멱등이라 중복 통지는 무해하다.
                HandleDisplayConfigurationChanged();
            }

            return IntPtr.Zero;
        }

        private void HandleDisplayConfigurationChanged()
        {
            if (_disposed)
            {
                return;
            }

            // 요구사항: 디스플레이 설정이 바뀌면 제한을 비활성화한다.
            if (_state != ClipState.Off)
            {
                DisableCore(ClipOffReason.DisplayChanged, true);
            }

            // 다음 Enable에서 영역을 다시 계산하게 한다.
            _desired = default(NativeMethods.RECT);

            EventHandler handler = DisplayConfigurationChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void RaiseStateChanged()
        {
            EventHandler handler = StateChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        // ------------------------------------------------------------------
        // 정리
        // ------------------------------------------------------------------

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _watchdogTimer.Stop();
            _watchdogTimer.Tick -= OnWatchdogTick;
            _foregroundRecheckTimer.Stop();
            _foregroundRecheckTimer.Tick -= OnForegroundRecheckTick;

            if (_winEventHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }

            // SystemEvents는 정적 델리게이트 목록을 갖는다. 해제하지 않으면 이 객체가
            // 영구히 살아남고, 종료 뒤에도 콜백이 도착해 파괴된 Dispatcher를 건드린다.
            SystemEvents.DisplaySettingsChanged -= _displaySettingsChangedHandler;

            if (_messageWindow != null)
            {
                _messageWindow.RemoveHook(_messageHook);
            }

            if (_state != ClipState.Off)
            {
                ReleaseOwnedClip();
                _state = ClipState.Off;
            }
        }
    }
}
