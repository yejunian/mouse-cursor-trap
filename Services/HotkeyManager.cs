using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using MouseCursorTrap.Interop;
using MouseCursorTrap.Models;

namespace MouseCursorTrap.Services
{
    /// <summary>단축키 등록 결과. 실패 사유를 설정 창에 보여 주기 위해 쓴다.</summary>
    internal sealed class HotkeyApplyResult
    {
        public bool EnableRegistered { get; set; }
        public bool DisableRegistered { get; set; }
        public string EnableError { get; set; }
        public string DisableError { get; set; }
    }

    /// <summary>
    /// 커서 제한을 켜고 끄는 전역 단축키를 관리한다.
    ///
    /// 주의: F12는 디버거 전용으로 예약된 키다. Visual Studio에서 디버깅 중이면
    /// 기본 단축키(Win+Shift+F12 / Win+F12)가 전달되지 않거나 등록에 실패할 수 있다.
    /// 단축키 확인은 디버거 밖에서 해야 한다.
    /// </summary>
    internal sealed class HotkeyManager : IDisposable
    {
        private const int HotkeyIdEnable = 1;
        private const int HotkeyIdDisable = 2;

        private readonly MessageWindow _messageWindow;
        private readonly HwndSourceHook _messageHook;

        private HotkeyBinding _enableBinding;
        private HotkeyBinding _disableBinding;
        private bool _enableRegistered;
        private bool _disableRegistered;
        private bool _suspended;
        private bool _disposed;

        public event EventHandler EnableRequested;
        public event EventHandler DisableRequested;

        public HotkeyManager(MessageWindow messageWindow)
        {
            _messageWindow = messageWindow;
            _messageHook = OnWindowMessage;
            _messageWindow.AddHook(_messageHook);
        }

        /// <summary>지정한 두 단축키를 다시 등록한다.</summary>
        public HotkeyApplyResult Apply(HotkeyBinding enableBinding, HotkeyBinding disableBinding)
        {
            _enableBinding = enableBinding;
            _disableBinding = disableBinding;

            HotkeyApplyResult result = new HotkeyApplyResult();

            UnregisterAll();

            if (_suspended)
            {
                // 캡처 중에는 등록하지 않는다. 포커스가 빠질 때 Resume에서 다시 등록한다.
                result.EnableRegistered = enableBinding == null || !enableBinding.IsRegisterable;
                result.DisableRegistered = disableBinding == null || !disableBinding.IsRegisterable;
                return result;
            }

            bool duplicated = enableBinding != null && disableBinding != null
                && enableBinding.IsRegisterable && enableBinding.Equals(disableBinding);

            string enableError;
            result.EnableRegistered = TryRegister(HotkeyIdEnable, enableBinding, out enableError);
            result.EnableError = enableError;
            _enableRegistered = result.EnableRegistered && enableBinding != null && enableBinding.IsRegisterable;

            if (duplicated)
            {
                result.DisableRegistered = false;
                result.DisableError = "두 단축키는 서로 달라야 합니다.";
            }
            else
            {
                string disableError;
                result.DisableRegistered = TryRegister(HotkeyIdDisable, disableBinding, out disableError);
                result.DisableError = disableError;
                _disableRegistered = result.DisableRegistered && disableBinding != null && disableBinding.IsRegisterable;
            }

            return result;
        }

        private bool TryRegister(int id, HotkeyBinding binding, out string error)
        {
            error = null;

            if (binding == null || !binding.IsRegisterable)
            {
                // 지정되지 않은 단축키는 실패가 아니다.
                return true;
            }

            IntPtr handle = _messageWindow.Handle;
            if (handle == IntPtr.Zero)
            {
                error = "단축키를 등록할 수 없습니다.";
                return false;
            }

            // MOD_NOREPEAT은 정책이므로 설정에 저장하지 않고 등록 시점에 더한다.
            uint modifiers = binding.Modifiers | NativeMethods.MOD_NOREPEAT;

            if (NativeMethods.RegisterHotKey(handle, id, modifiers, binding.VirtualKey))
            {
                return true;
            }

            int lastError = Marshal.GetLastWin32Error();
            error = lastError == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
                ? "단축키를 등록하지 못했습니다. 다른 프로그램이 사용 중일 수 있습니다."
                : "단축키를 등록하지 못했습니다.";
            return false;
        }

        private void UnregisterAll()
        {
            IntPtr handle = _messageWindow.Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            if (_enableRegistered)
            {
                NativeMethods.UnregisterHotKey(handle, HotkeyIdEnable);
                _enableRegistered = false;
            }

            if (_disableRegistered)
            {
                NativeMethods.UnregisterHotKey(handle, HotkeyIdDisable);
                _disableRegistered = false;
            }
        }

        /// <summary>
        /// 단축키 등록을 일시 해제한다.
        ///
        /// 전역 단축키로 잡혀 있는 조합은 어떤 창에도 전달되지 않는다. 캡처 상자에
        /// 포커스가 있는 동안 풀어 두지 않으면, 사용자가 현재 조합을 다시 입력할 수 없고
        /// 입력을 시도하면 제한이 토글되어 버린다.
        /// </summary>
        public void Suspend()
        {
            if (_suspended)
            {
                return;
            }

            _suspended = true;
            UnregisterAll();
        }

        public void Resume()
        {
            if (!_suspended)
            {
                return;
            }

            _suspended = false;
            Apply(_enableBinding, _disableBinding);
        }

        private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != NativeMethods.WM_HOTKEY)
            {
                return IntPtr.Zero;
            }

            int id = wParam.ToInt32();

            if (id == HotkeyIdEnable)
            {
                handled = true;
                Raise(EnableRequested);
            }
            else if (id == HotkeyIdDisable)
            {
                handled = true;
                Raise(DisableRequested);
            }

            return IntPtr.Zero;
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            UnregisterAll();
            _messageWindow.RemoveHook(_messageHook);
        }
    }
}
