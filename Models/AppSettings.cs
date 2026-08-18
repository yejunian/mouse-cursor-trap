using System.Runtime.Serialization;

namespace MouseCursorTrap.Models
{
    /// <summary>%AppData%\MouseCursorTrap\settings.json 에 저장되는 형상.</summary>
    [DataContract(Name = "Settings", Namespace = "")]
    internal sealed class AppSettings
    {
        [DataMember(Name = "schemaVersion", Order = 0)]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "clipOnStart", Order = 1)]
        public bool ClipOnStart { get; set; }

        [DataMember(Name = "skipSettingsWindowOnStart", Order = 2)]
        public bool SkipSettingsWindowOnStart { get; set; }

        [DataMember(Name = "runAtStartup", Order = 3)]
        public bool RunAtStartup { get; set; }

        [DataMember(Name = "enableHotkey", Order = 4)]
        public HotkeySetting EnableHotkey { get; set; }

        [DataMember(Name = "disableHotkey", Order = 5)]
        public HotkeySetting DisableHotkey { get; set; }

        public AppSettings()
        {
            ApplyDefaults();
        }

        /// <summary>
        /// DataContractJsonSerializer는 FormatterServices.GetUninitializedObject로 인스턴스를 만들기 때문에
        /// 생성자도 필드 이니셜라이저도 실행되지 않는다. 구버전 파일에 없는 멤버가 null로 남아
        /// 단축키 등록에서 터지는 것을 막으려면 반드시 여기서 기본값을 채워야 한다.
        /// </summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            ApplyDefaults();
        }

        private void ApplyDefaults()
        {
            SchemaVersion = 1;
            ClipOnStart = false;
            SkipSettingsWindowOnStart = false;
            RunAtStartup = false;
            EnableHotkey = HotkeySetting.FromBinding(HotkeyBinding.DefaultEnable);
            DisableHotkey = HotkeySetting.FromBinding(HotkeyBinding.DefaultDisable);
        }

        /// <summary>손상되었거나 등록 불가능한 값을 정리한다.</summary>
        public void Normalize()
        {
            if (SchemaVersion <= 0)
            {
                SchemaVersion = 1;
            }

            EnableHotkey = NormalizeHotkey(EnableHotkey);
            DisableHotkey = NormalizeHotkey(DisableHotkey);
        }

        private static HotkeySetting NormalizeHotkey(HotkeySetting setting)
        {
            if (setting == null)
            {
                return null;
            }

            // 수식 키가 없거나 주 키가 없으면 RegisterHotKey에 넘길 수 없다.
            if (setting.Modifiers == 0 || setting.VirtualKey == 0)
            {
                return null;
            }

            return setting;
        }

        public HotkeyBinding GetEnableBinding()
        {
            return EnableHotkey == null ? null : EnableHotkey.ToBinding();
        }

        public HotkeyBinding GetDisableBinding()
        {
            return DisableHotkey == null ? null : DisableHotkey.ToBinding();
        }

        public void SetEnableBinding(HotkeyBinding binding)
        {
            EnableHotkey = HotkeySetting.FromBinding(binding);
        }

        public void SetDisableBinding(HotkeyBinding binding)
        {
            DisableHotkey = HotkeySetting.FromBinding(binding);
        }
    }

    /// <summary>단축키 한 벌의 직렬화 표현.</summary>
    [DataContract(Name = "Hotkey", Namespace = "")]
    internal sealed class HotkeySetting
    {
        [DataMember(Name = "modifiers", Order = 0)]
        public uint Modifiers { get; set; }

        [DataMember(Name = "virtualKey", Order = 1)]
        public uint VirtualKey { get; set; }

        public static HotkeySetting FromBinding(HotkeyBinding binding)
        {
            if (binding == null)
            {
                return null;
            }

            HotkeySetting setting = new HotkeySetting();
            setting.Modifiers = binding.Modifiers;
            setting.VirtualKey = binding.VirtualKey;
            return setting;
        }

        public HotkeyBinding ToBinding()
        {
            return new HotkeyBinding(Modifiers, VirtualKey);
        }
    }
}
