using System;
using System.Windows;
using System.Windows.Input;
using MouseCursorTrap.Controls;
using MouseCursorTrap.Models;
using MouseCursorTrap.Services;

namespace MouseCursorTrap.Views
{
    /// <summary>
    /// 설정 창. 확인/취소가 없는 즉시 적용형이라 모든 변경이 곧바로 반영되고 저장된다.
    ///
    /// 뷰모델 없이 코드 비하인드로 둔 이유: 동적으로 바뀌는 것은 상태 문구 하나와 토글 몇
    /// 개뿐이고, "단축키로 제한이 바뀜 → 체크박스 갱신 → Checked 재발생" 되먹임을 막는 데는
    /// 명시적인 억제 플래그가 바인딩과 컨버터보다 읽기 쉽다.
    /// </summary>
    internal partial class SettingsWindow : Window
    {
        private readonly AppController _controller;
        private readonly CursorClipService _clip;
        private readonly HotkeyManager _hotkeys;
        private readonly SettingsStore _store;
        private readonly StartupRegistrar _startup;
        private readonly AppSettings _settings;

        private bool _suppressEvents;
        private bool _centeredOnce;

        public SettingsWindow(
            AppController controller,
            CursorClipService clip,
            HotkeyManager hotkeys,
            SettingsStore store,
            StartupRegistrar startup,
            AppSettings settings)
        {
            _controller = controller;
            _clip = clip;
            _hotkeys = hotkeys;
            _store = store;
            _startup = startup;
            _settings = settings;

            InitializeComponent();

            _clip.StateChanged += OnClipStateChanged;

            WireHotkeyBox(EnableHotkeyBox, true);
            WireHotkeyBox(DisableHotkeyBox, false);

            SyncFromModel();
            ContentRendered += OnContentRendered;
        }

        private void WireHotkeyBox(HotkeyBox box, bool isEnableHotkey)
        {
            // 등록된 전역 단축키는 어떤 창에도 전달되지 않는다. 캡처 중에는 잠시 풀어 둔다.
            box.GotKeyboardFocus += OnHotkeyBoxGotFocus;
            box.LostKeyboardFocus += OnHotkeyBoxLostFocus;

            if (isEnableHotkey)
            {
                box.BindingChanged += OnEnableHotkeyChanged;
            }
            else
            {
                box.BindingChanged += OnDisableHotkeyChanged;
            }

            box.HintRaised += OnHotkeyHintRaised;
        }

        private void OnContentRendered(object sender, EventArgs e)
        {
            if (_centeredOnce)
            {
                return;
            }

            // SizeToContent="Height" 때문에 SourceInitialized 시점에는 최종 크기를 모른다.
            _centeredOnce = true;
            WindowPlacement.CenterOnPrimary(this);
        }

        // ------------------------------------------------------------------
        // 표시 동기화
        // ------------------------------------------------------------------

        /// <summary>모델 상태를 컨트롤에 반영한다. 이벤트 되먹임은 억제한다.</summary>
        public void SyncFromModel()
        {
            _suppressEvents = true;
            try
            {
                ClipToggle.IsChecked = _clip.IsOn;
                StateText.Text = _clip.DescribeRegion();
                UpdateReasonNote();

                EnableHotkeyBox.SetBindingSilently(_settings.GetEnableBinding());
                DisableHotkeyBox.SetBindingSilently(_settings.GetDisableBinding());

                RunAtStartupCheck.IsChecked = _settings.RunAtStartup;
                ClipOnStartCheck.IsChecked = _settings.ClipOnStart;
                SkipWindowCheck.IsChecked = _settings.SkipSettingsWindowOnStart;
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        private void UpdateReasonNote()
        {
            string message = null;

            if (!_clip.IsOn)
            {
                switch (_clip.OffReason)
                {
                    case ClipOffReason.DisplayChanged:
                        message = "디스플레이 설정이 변경되어 커서 제한이 해제되었습니다.";
                        break;
                    case ClipOffReason.ForeignClip:
                        message = "다른 프로그램이 커서 영역을 제어하여 커서 제한이 해제되었습니다.";
                        break;
                }
            }

            ReasonNote.Text = message ?? string.Empty;
            ReasonNote.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnClipStateChanged(object sender, EventArgs e)
        {
            // 단축키나 디스플레이 변경으로 상태가 뒤집혀도 창이 즉시 따라가야 한다.
            RefreshClipState();
        }

        /// <summary>
        /// 커서 가두기 상태와 허용 영역 문구를 다시 표시한다.
        /// 꺼진 상태의 허용 영역은 디스플레이 구성을 따라 바뀌는데 StateChanged가 오지 않으므로
        /// 디스플레이 변경 뒤에는 바깥에서 직접 부른다.
        /// </summary>
        public void RefreshClipState()
        {
            _suppressEvents = true;
            try
            {
                ClipToggle.IsChecked = _clip.IsOn;
                StateText.Text = _clip.DescribeRegion();
                UpdateReasonNote();
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        /// <summary>단축키 등록 결과를 화면에 반영한다.</summary>
        public void ShowHotkeyResult(HotkeyApplyResult result)
        {
            if (result == null)
            {
                return;
            }

            SetErrorText(EnableHotkeyError, result.EnableError);
            SetErrorText(DisableHotkeyError, result.DisableError);
        }

        private static void SetErrorText(System.Windows.Controls.TextBlock target, string message)
        {
            target.Text = message ?? string.Empty;
            target.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        // ------------------------------------------------------------------
        // 커서 제한
        // ------------------------------------------------------------------

        private void ClipToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents)
            {
                return;
            }

            if (ClipToggle.IsChecked == true)
            {
                _clip.Enable();
            }
            else
            {
                _clip.Disable();
            }
        }

        // ------------------------------------------------------------------
        // 단축키
        // ------------------------------------------------------------------

        private void OnHotkeyBoxGotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _hotkeys.Suspend();
        }

        private void OnHotkeyBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _hotkeys.Resume();
        }

        private void OnEnableHotkeyChanged(object sender, EventArgs e)
        {
            SetErrorText(EnableHotkeyError, null);
            _settings.SetEnableBinding(EnableHotkeyBox.Binding);
            _store.MarkDirty();
            ApplyHotkeys();
        }

        private void OnDisableHotkeyChanged(object sender, EventArgs e)
        {
            SetErrorText(DisableHotkeyError, null);
            _settings.SetDisableBinding(DisableHotkeyBox.Binding);
            _store.MarkDirty();
            ApplyHotkeys();
        }

        private void OnHotkeyHintRaised(object sender, HotkeyHintEventArgs e)
        {
            System.Windows.Controls.TextBlock target =
                ReferenceEquals(sender, EnableHotkeyBox) ? EnableHotkeyError : DisableHotkeyError;
            SetErrorText(target, e.Message);
        }

        private void ApplyHotkeys()
        {
            // 등록에 실패해도 사용자가 고른 값은 그대로 두고 사유만 보여 준다.
            // 충돌하던 프로그램이 사라지면 다음 실행에서 다시 등록된다.
            ShowHotkeyResult(_controller.ApplyHotkeys());
        }

        // ------------------------------------------------------------------
        // 옵션
        // ------------------------------------------------------------------

        private void RunAtStartupCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents)
            {
                return;
            }

            bool requested = RunAtStartupCheck.IsChecked == true;
            _startup.SetEnabled(requested);

            if (_startup.LastOperationFailed)
            {
                SetErrorText(RunAtStartupError, "자동 실행 설정을 변경하지 못했습니다.");
            }
            else
            {
                SetErrorText(RunAtStartupError, null);
            }

            // 레지스트리가 진실이므로 실제 상태를 다시 읽어 표시를 맞춘다.
            bool actual = _startup.IsEnabled();
            _settings.RunAtStartup = actual;
            _store.MarkDirty();

            if (actual != requested)
            {
                _suppressEvents = true;
                try
                {
                    RunAtStartupCheck.IsChecked = actual;
                }
                finally
                {
                    _suppressEvents = false;
                }
            }
        }

        private void ClipOnStartCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents)
            {
                return;
            }

            _settings.ClipOnStart = ClipOnStartCheck.IsChecked == true;
            _store.MarkDirty();
        }

        private void SkipWindowCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents)
            {
                return;
            }

            _settings.SkipSettingsWindowOnStart = SkipWindowCheck.IsChecked == true;
            _store.MarkDirty();
        }

        // ------------------------------------------------------------------
        // 버튼
        // ------------------------------------------------------------------

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            _controller.ExitApplication();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>창이 숨겨질 때 캡처 중이었을 수 있으므로 단축키를 되살린다.</summary>
        public void OnHidden()
        {
            _hotkeys.Resume();
        }

        /// <summary>실제 종료 시 구독을 정리한다.</summary>
        public void DetachEvents()
        {
            _clip.StateChanged -= OnClipStateChanged;
            ContentRendered -= OnContentRendered;
        }
    }
}
