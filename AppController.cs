using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using MouseCursorTrap.Interop;
using MouseCursorTrap.Models;
using MouseCursorTrap.Services;
using MouseCursorTrap.Views;

namespace MouseCursorTrap
{
    /// <summary>
    /// 애플리케이션 수명 주기 조정자.
    /// 모든 서비스를 소유하고 이벤트를 연결한다. 서비스끼리는 서로를 알지 못하고
    /// 필요한 연결은 전부 여기서 중개한다.
    /// </summary>
    internal sealed class AppController : IDisposable
    {
        private const double RecenterDebounceMilliseconds = 300;

        private readonly Dispatcher _dispatcher;
        private readonly SingleInstanceGuard _guard;
        private readonly SettingsStore _store;
        private readonly StartupRegistrar _startup;
        private readonly DispatcherTimer _recenterTimer;

        private AppSettings _settings;
        private MessageWindow _messageWindow;
        private CursorClipService _clip;
        private HotkeyManager _hotkeys;
        private TrayIconHost _tray;
        private SettingsWindow _window;
        private HotkeyApplyResult _lastHotkeyResult;

        private bool _shuttingDown;
        private bool _disposed;

        public AppController(SingleInstanceGuard guard)
        {
            _guard = guard;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _store = new SettingsStore();
            _startup = new StartupRegistrar();

            _recenterTimer = new DispatcherTimer(DispatcherPriority.Normal);
            _recenterTimer.Interval = TimeSpan.FromMilliseconds(RecenterDebounceMilliseconds);
            _recenterTimer.Tick += OnRecenterTick;
        }

        public void Start()
        {
            // 1) 설정을 먼저 읽는다. 이후 단계가 모두 이 값을 쓴다.
            _settings = _store.Load();

            // 2) 자동 실행은 레지스트리가 진실이므로 설정을 그쪽에 맞춘다.
            if (_startup.Reconcile(_settings))
            {
                _store.MarkDirty();
            }

            // 3) 단축키 등록 대상이 될 숨김 창.
            _messageWindow = new MessageWindow();

            // 4) 커서 제한 서비스.
            _clip = new CursorClipService(_messageWindow);
            _clip.StateChanged += OnClipStateChanged;
            _clip.DisplayConfigurationChanged += OnDisplayConfigurationChanged;

            // 5) 트레이 아이콘. 이후 단계가 실패해도 아이콘은 떠 있어야 한다.
            _tray = new TrayIconHost(IsClipOn, DescribeRegion, ToggleClip, ShowSettings, ExitApplication);
            _tray.Show();

            // 6) 전역 단축키.
            _hotkeys = new HotkeyManager(_messageWindow);
            _hotkeys.EnableRequested += OnEnableHotkeyPressed;
            _hotkeys.DisableRequested += OnDisableHotkeyPressed;
            _lastHotkeyResult = ApplyHotkeys();

            // 7) 두 번째 인스턴스의 요청 수신.
            _guard.ShowRequested += OnShowRequestedFromOtherInstance;
            _guard.StartListening();

            // 8) 시작 시 제한 켜기.
            if (_settings.ClipOnStart)
            {
                _clip.Enable();
            }

            // 9) 시작 시 설정 창 표시.
            if (!_settings.SkipSettingsWindowOnStart)
            {
                ShowSettings();
            }
        }

        private bool IsClipOn()
        {
            return _clip != null && _clip.IsOn;
        }

        private string DescribeRegion()
        {
            return _clip == null ? CursorClipService.UnknownRegionText : _clip.DescribeRegion();
        }

        private void ToggleClip()
        {
            if (_clip == null)
            {
                return;
            }

            if (_clip.IsOn)
            {
                _clip.Disable();
            }
            else
            {
                _clip.Enable();
            }
        }

        /// <summary>현재 설정의 단축키를 다시 등록한다.</summary>
        public HotkeyApplyResult ApplyHotkeys()
        {
            _lastHotkeyResult = _hotkeys.Apply(_settings.GetEnableBinding(), _settings.GetDisableBinding());
            return _lastHotkeyResult;
        }

        // ------------------------------------------------------------------
        // 이벤트
        // ------------------------------------------------------------------

        private void OnClipStateChanged(object sender, EventArgs e)
        {
            if (_tray != null)
            {
                _tray.RefreshState();
            }
        }

        private void OnDisplayConfigurationChanged(object sender, EventArgs e)
        {
            if (_window == null || !_window.IsVisible)
            {
                return;
            }

            // 디스플레이 재구성 중에는 이벤트가 여러 번 오고 중간 시점의 모니터 정보는
            // 확정값이 아니다. 잠시 기다렸다가 새 주 디스플레이 기준으로 허용 영역 문구를
            // 갱신하고 창을 다시 배치한다.
            _recenterTimer.Stop();
            _recenterTimer.Start();
        }

        private void OnRecenterTick(object sender, EventArgs e)
        {
            _recenterTimer.Stop();

            if (_window != null && _window.IsVisible)
            {
                _window.RefreshClipState();
                WindowPlacement.CenterOnPrimary(_window);
            }
        }

        private void OnEnableHotkeyPressed(object sender, EventArgs e)
        {
            _clip.Enable();
        }

        private void OnDisableHotkeyPressed(object sender, EventArgs e)
        {
            _clip.Disable();
        }

        private void OnShowRequestedFromOtherInstance(object sender, EventArgs e)
        {
            // 스레드 풀 스레드에서 도착하므로 UI 스레드로 넘긴다.
            _dispatcher.BeginInvoke(new Action(ShowSettings));
        }

        // ------------------------------------------------------------------
        // 설정 창
        // ------------------------------------------------------------------

        public void ShowSettings()
        {
            if (_shuttingDown)
            {
                return;
            }

            if (_window == null)
            {
                _window = new SettingsWindow(this, _clip, _hotkeys, _store, _startup, _settings);
                _window.Closing += OnSettingsWindowClosing;
            }

            if (!_window.IsVisible)
            {
                _window.SyncFromModel();
                _window.Show();
            }

            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.ShowHotkeyResult(_lastHotkeyResult);

            WindowPlacement.CenterOnPrimary(_window);
            _window.Activate();
            WindowPlacement.ForceForeground(_window);
        }

        private void OnSettingsWindowClosing(object sender, CancelEventArgs e)
        {
            if (_shuttingDown)
            {
                // 종료 중에는 진짜로 닫는다.
                return;
            }

            // "설정 창을 닫으면 트레이 아이콘만 남는다."
            e.Cancel = true;
            _window.Hide();
            _window.OnHidden();
        }

        // ------------------------------------------------------------------
        // 종료
        // ------------------------------------------------------------------

        public void ExitApplication()
        {
            if (_shuttingDown)
            {
                return;
            }

            _shuttingDown = true;
            Application.Current.Shutdown();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _shuttingDown = true;

            // 어느 한 단계가 실패해도 커서 제한 해제까지는 반드시 도달해야 한다.
            TryRun(delegate
            {
                _recenterTimer.Stop();
                _recenterTimer.Tick -= OnRecenterTick;
            });

            TryRun(delegate
            {
                _guard.ShowRequested -= OnShowRequestedFromOtherInstance;
                _guard.StopListening();
            });

            TryRun(delegate
            {
                if (_hotkeys != null)
                {
                    _hotkeys.EnableRequested -= OnEnableHotkeyPressed;
                    _hotkeys.DisableRequested -= OnDisableHotkeyPressed;
                    _hotkeys.Dispose();
                    _hotkeys = null;
                }
            });

            TryRun(delegate
            {
                if (_clip != null)
                {
                    _clip.StateChanged -= OnClipStateChanged;
                    _clip.DisplayConfigurationChanged -= OnDisplayConfigurationChanged;
                    _clip.Dispose();
                    _clip = null;
                }
            });

            TryRun(delegate
            {
                if (_tray != null)
                {
                    _tray.Dispose();
                    _tray = null;
                }
            });

            TryRun(delegate
            {
                _store.FlushPendingSave();
                _store.Dispose();
            });

            TryRun(delegate
            {
                if (_window != null)
                {
                    _window.Closing -= OnSettingsWindowClosing;
                    _window.DetachEvents();
                    _window.Close();
                    _window = null;
                }
            });

            TryRun(delegate
            {
                if (_messageWindow != null)
                {
                    _messageWindow.Dispose();
                    _messageWindow = null;
                }
            });

            // 어떤 경로로 왔든 커서가 갇힌 채 남지 않게 한다.
            TryRun(delegate { NativeMethods.ClipCursorRelease(IntPtr.Zero); });
        }

        private static void TryRun(Action action)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // 종료 경로에서는 어떤 실패도 다음 단계를 막아서는 안 된다.
            }
        }
    }
}
