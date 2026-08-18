using System;
using System.Reflection;
using System.Security;
using Microsoft.Win32;
using MouseCursorTrap.Models;

namespace MouseCursorTrap.Services
{
    /// <summary>
    /// HKCU의 Run 키로 Windows 시작 시 자동 실행을 켜고 끈다.
    ///
    /// 레지스트리가 진실의 원천이다. 작업 관리자의 시작 프로그램 탭, 설정 앱, 각종 정리
    /// 유틸리티가 밖에서 값을 바꿀 수 있고 settings.json은 그것을 알 수 없기 때문이다.
    /// </summary>
    internal sealed class StartupRegistrar
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ValueName = "MouseCursorTrap";

        /// <summary>마지막 레지스트리 접근이 실패했으면 true. 설정 창이 안내에 쓴다.</summary>
        public bool LastOperationFailed { get; private set; }

        /// <summary>
        /// 레지스트리의 실제 상태를 읽어 설정에 반영한다.
        /// 값이 있는데 경로가 낡았으면 현재 exe 경로로 고쳐 쓴다.
        /// </summary>
        /// <returns>설정이 바뀌었으면 true.</returns>
        public bool Reconcile(AppSettings settings)
        {
            bool actuallyEnabled = IsEnabled();

            if (actuallyEnabled && !HasCurrentPath())
            {
                // 폴더를 옮긴 뒤에도 자동 실행이 계속 동작하도록 경로를 갱신한다.
                SetEnabled(true);
            }

            if (settings.RunAtStartup != actuallyEnabled)
            {
                settings.RunAtStartup = actuallyEnabled;
                return true;
            }

            return false;
        }

        /// <summary>Windows가 실제로 이 프로그램을 시작 시 실행하는지.</summary>
        public bool IsEnabled()
        {
            LastOperationFailed = false;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null || key.GetValue(ValueName) == null)
                    {
                        return false;
                    }
                }

                // Run 값이 있어도 StartupApproved에서 사용 안 함으로 꺼 두면 실행되지 않는다.
                return !IsDisabledByStartupApproved();
            }
            catch (SecurityException) { LastOperationFailed = true; }
            catch (UnauthorizedAccessException) { LastOperationFailed = true; }
            catch (System.IO.IOException) { LastOperationFailed = true; }

            return false;
        }

        public void SetEnabled(bool enabled)
        {
            LastOperationFailed = false;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        LastOperationFailed = true;
                        return;
                    }

                    if (enabled)
                    {
                        key.SetValue(ValueName, GetQuotedExecutablePath(), RegistryValueKind.String);
                    }
                    else if (key.GetValue(ValueName) != null)
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }

                if (enabled)
                {
                    // 체크는 켜져 있는데 부팅 때 안 뜨는 상황을 막는다.
                    RemoveStartupApprovedOverride();
                }
            }
            catch (SecurityException) { LastOperationFailed = true; }
            catch (UnauthorizedAccessException) { LastOperationFailed = true; }
            catch (System.IO.IOException) { LastOperationFailed = true; }
        }

        private bool HasCurrentPath()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    string stored = key.GetValue(ValueName) as string;
                    return string.Equals(stored, GetQuotedExecutablePath(), StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.IO.IOException) { }

            return false;
        }

        private static bool IsDisabledByStartupApproved()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    byte[] data = key.GetValue(ValueName) as byte[];
                    if (data == null || data.Length == 0)
                    {
                        return false;
                    }

                    // 첫 바이트의 최하위 비트가 서 있으면 사용 안 함이다.
                    return (data[0] & 0x01) != 0;
                }
            }
            catch (SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.IO.IOException) { }

            return false;
        }

        private static void RemoveStartupApprovedOverride()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, true))
                {
                    if (key != null && key.GetValue(ValueName) != null)
                    {
                        // Explorer의 영역이므로 우리 값만 지우고 새로 쓰지는 않는다.
                        key.DeleteValue(ValueName, false);
                    }
                }
            }
            catch (SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.IO.IOException) { }
        }

        /// <summary>
        /// 공백이 든 경로를 따옴표 없이 쓰면 Windows가 엉뚱한 실행 파일을 찾는다.
        /// 항상 큰따옴표로 감싼다.
        /// </summary>
        private static string GetQuotedExecutablePath()
        {
            Assembly entry = Assembly.GetEntryAssembly();
            string path = entry != null ? entry.Location : Environment.GetCommandLineArgs()[0];
            return "\"" + path + "\"";
        }
    }
}
