using System;
using System.Windows.Controls;
using System.Windows.Input;
using MouseCursorTrap.Interop;
using MouseCursorTrap.Models;

namespace MouseCursorTrap.Controls
{
    /// <summary>
    /// 단축키를 눌러서 지정하는 읽기 전용 입력 상자.
    /// XAML 없이 코드로만 정의하므로 별도의 Page 항목이 필요 없다.
    /// </summary>
    internal sealed class HotkeyBox : TextBox
    {
        private HotkeyBinding _binding;

        /// <summary>사용자가 새 조합을 입력했거나 지웠을 때 발생한다.</summary>
        public event EventHandler BindingChanged;

        /// <summary>수식 키 없이 눌렀을 때처럼, 안내가 필요한 입력이 있었을 때 발생한다.</summary>
        public event EventHandler<HotkeyHintEventArgs> HintRaised;

        public HotkeyBox()
        {
            IsReadOnly = true;
            IsReadOnlyCaretVisible = false;
            IsUndoEnabled = false;
            ContextMenu = null;
            Cursor = Cursors.Arrow;

            // 한국어 환경에서는 IME가 키 입력을 Key.ImeProcessed로 삼켜 버린다.
            // 이 상자에서는 IME 자체를 끈다.
            InputMethod.SetIsInputMethodEnabled(this, false);
            InputMethod.SetPreferredImeState(this, InputMethodState.Off);
        }

        public HotkeyBinding Binding
        {
            get { return _binding; }
            set
            {
                _binding = value;
                Text = value == null ? "(없음)" : value.ToDisplayString();
            }
        }

        /// <summary>이벤트를 발생시키지 않고 표시만 갱신한다. 초기화에 쓴다.</summary>
        public void SetBindingSilently(HotkeyBinding binding)
        {
            Binding = binding;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // TextBox의 기본 편집 동작을 전부 막는다.
            e.Handled = true;

            Key key = e.Key;
            if (key == Key.System)
            {
                key = e.SystemKey;
            }
            if (key == Key.ImeProcessed)
            {
                key = e.ImeProcessedKey;
            }

            if (key == Key.Escape)
            {
                Binding = null;
                RaiseBindingChanged();
                return;
            }

            if (IsModifierKey(key))
            {
                // 수식 키만 눌린 상태. 아직 확정할 것이 없다.
                return;
            }

            uint modifiers = GetCurrentModifiers();
            if (modifiers == 0)
            {
                RaiseHint("수식 키(Ctrl, Alt, Shift, Win)를 함께 눌러야 합니다.");
                return;
            }

            int virtualKey = KeyInterop.VirtualKeyFromKey(key);
            if (virtualKey == 0)
            {
                return;
            }

            Binding = new HotkeyBinding(modifiers, (uint)virtualKey);
            RaiseBindingChanged();
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            e.Handled = true;
        }

        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            e.Handled = true;
        }

        private static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl
                || key == Key.LeftShift || key == Key.RightShift
                || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LWin || key == Key.RWin
                || key == Key.System;
        }

        private static uint GetCurrentModifiers()
        {
            uint result = 0;
            ModifierKeys modifiers = Keyboard.Modifiers;

            if ((modifiers & ModifierKeys.Control) != 0) { result |= NativeMethods.MOD_CONTROL; }
            if ((modifiers & ModifierKeys.Shift) != 0) { result |= NativeMethods.MOD_SHIFT; }
            if ((modifiers & ModifierKeys.Alt) != 0) { result |= NativeMethods.MOD_ALT; }

            // WPF의 ModifierKeys에는 Windows 키 플래그가 없어 직접 조회해야 한다.
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
            {
                result |= NativeMethods.MOD_WIN;
            }

            return result;
        }

        private void RaiseBindingChanged()
        {
            EventHandler handler = BindingChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void RaiseHint(string message)
        {
            EventHandler<HotkeyHintEventArgs> handler = HintRaised;
            if (handler != null)
            {
                handler(this, new HotkeyHintEventArgs(message));
            }
        }
    }

    internal sealed class HotkeyHintEventArgs : EventArgs
    {
        private readonly string _message;

        public HotkeyHintEventArgs(string message)
        {
            _message = message;
        }

        public string Message { get { return _message; } }
    }
}
