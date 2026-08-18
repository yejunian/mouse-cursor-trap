using System;
using System.Text;
using System.Windows.Input;
using MouseCursorTrap.Interop;

namespace MouseCursorTrap.Models
{
    /// <summary>
    /// 전역 단축키 한 벌을 나타내는 불변 값. 수식 키 조합과 가상 키 코드로 이루어진다.
    /// </summary>
    internal sealed class HotkeyBinding : IEquatable<HotkeyBinding>
    {
        private readonly uint _modifiers;
        private readonly uint _virtualKey;

        public HotkeyBinding(uint modifiers, uint virtualKey)
        {
            _modifiers = modifiers;
            _virtualKey = virtualKey;
        }

        /// <summary>MOD_ALT / MOD_CONTROL / MOD_SHIFT / MOD_WIN 조합.</summary>
        public uint Modifiers { get { return _modifiers; } }

        /// <summary>가상 키 코드.</summary>
        public uint VirtualKey { get { return _virtualKey; } }

        /// <summary>수식 키가 하나 이상 있고 주 키가 지정되어야 등록할 수 있다.</summary>
        public bool IsRegisterable
        {
            get { return _modifiers != 0 && _virtualKey != 0; }
        }

        public static HotkeyBinding DefaultEnable
        {
            get { return new HotkeyBinding(NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT, NativeMethods.VK_F12); }
        }

        public static HotkeyBinding DefaultDisable
        {
            get { return new HotkeyBinding(NativeMethods.MOD_WIN, NativeMethods.VK_F12); }
        }

        /// <summary>
        /// 사람이 읽는 표기. 수식 키 순서는 Win, Ctrl, Alt, Shift로 고정한다.
        /// 기본값이 각각 "Win+Shift+F12", "Win+F12"로 나온다.
        /// </summary>
        public string ToDisplayString()
        {
            StringBuilder builder = new StringBuilder();

            if ((_modifiers & NativeMethods.MOD_WIN) != 0) { builder.Append("Win+"); }
            if ((_modifiers & NativeMethods.MOD_CONTROL) != 0) { builder.Append("Ctrl+"); }
            if ((_modifiers & NativeMethods.MOD_ALT) != 0) { builder.Append("Alt+"); }
            if ((_modifiers & NativeMethods.MOD_SHIFT) != 0) { builder.Append("Shift+"); }

            builder.Append(GetKeyName(_virtualKey));
            return builder.ToString();
        }

        private static string GetKeyName(uint virtualKey)
        {
            Key key = KeyInterop.KeyFromVirtualKey((int)virtualKey);

            if (key >= Key.D0 && key <= Key.D9)
            {
                return ((char)('0' + (key - Key.D0))).ToString();
            }

            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                return "Num " + ((char)('0' + (key - Key.NumPad0)));
            }

            switch (key)
            {
                case Key.OemTilde: return "`";
                case Key.OemMinus: return "-";
                case Key.OemPlus: return "=";
                case Key.OemOpenBrackets: return "[";
                case Key.OemCloseBrackets: return "]";
                case Key.OemPipe: return "\\";
                case Key.OemSemicolon: return ";";
                case Key.OemQuotes: return "'";
                case Key.OemComma: return ",";
                case Key.OemPeriod: return ".";
                case Key.OemQuestion: return "/";
                case Key.Multiply: return "Num *";
                case Key.Add: return "Num +";
                case Key.Subtract: return "Num -";
                case Key.Decimal: return "Num .";
                case Key.Divide: return "Num /";
                case Key.Next: return "PageDown";
                case Key.Prior: return "PageUp";
                case Key.Snapshot: return "PrintScreen";
                case Key.None: return "?";
                default: return key.ToString();
            }
        }

        public bool Equals(HotkeyBinding other)
        {
            if (ReferenceEquals(other, null)) { return false; }
            return _modifiers == other._modifiers && _virtualKey == other._virtualKey;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as HotkeyBinding);
        }

        public override int GetHashCode()
        {
            return (int)((_modifiers * 397) ^ _virtualKey);
        }

        public override string ToString()
        {
            return ToDisplayString();
        }
    }
}
