using System;
using System.Drawing;
using System.Windows.Forms;

namespace MouseCursorTrap.Services
{
    /// <summary>
    /// 알림 영역 아이콘과 그 컨텍스트 메뉴.
    ///
    /// WinForms NotifyIcon은 TaskbarCreated 메시지를 내부에서 처리하므로
    /// Explorer가 재시작되어도 아이콘이 저절로 복구된다.
    /// </summary>
    internal sealed class TrayIconHost : IDisposable
    {
        private const string ApplicationName = "MouseCursorTrap";

        private readonly Func<bool> _isClipOn;
        private readonly Func<string> _describeRegion;
        private readonly Action _onToggleClip;
        private readonly Action _onOpenSettings;
        private readonly Action _onExit;

        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _toggleItem;
        private ToolStripMenuItem _stateItem;
        private Icon _icon;
        private bool _disposed;

        public TrayIconHost(
            Func<bool> isClipOn,
            Func<string> describeRegion,
            Action onToggleClip,
            Action onOpenSettings,
            Action onExit)
        {
            _isClipOn = isClipOn;
            _describeRegion = describeRegion;
            _onToggleClip = onToggleClip;
            _onOpenSettings = onOpenSettings;
            _onExit = onExit;
        }

        public void Show()
        {
            _icon = LoadIcon();

            _menu = new ContextMenuStrip();

            ToolStripMenuItem titleItem = new ToolStripMenuItem(ApplicationName);
            titleItem.Enabled = false;

            // CheckOnClick을 쓰지 않는다. 켜기가 실패할 수 있으므로(주 디스플레이 영역을
            // 얻지 못한 경우) 체크 표시는 클릭이 아니라 실제 상태를 따라가야 한다.
            _toggleItem = new ToolStripMenuItem("마우스 커서 가두기");
            _toggleItem.Click += OnToggleClicked;

            _stateItem = new ToolStripMenuItem();
            _stateItem.Enabled = false;

            ToolStripMenuItem settingsItem = new ToolStripMenuItem("설정");
            settingsItem.Click += OnSettingsClicked;

            ToolStripMenuItem exitItem = new ToolStripMenuItem("종료");
            exitItem.Click += OnExitClicked;

            RefreshState();

            _menu.Items.AddRange(new ToolStripItem[]
            {
                titleItem,
                _toggleItem,
                _stateItem,
                settingsItem,
                new ToolStripSeparator(),
                exitItem,
            });

            // 메뉴를 열 때마다 최신 상태로 갱신한다.
            _menu.Opening += OnMenuOpening;

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = _icon;
            _notifyIcon.Text = ApplicationName;
            _notifyIcon.ContextMenuStrip = _menu;
            // Click은 일부 경로에서 우클릭에도 발생하므로 MouseUp에서 버튼을 직접 가린다.
            _notifyIcon.MouseUp += OnNotifyIconMouseUp;
            _notifyIcon.Visible = true;
        }

        /// <summary>제한 상태가 바뀌었을 때 체크 표시와 메뉴 문구를 갱신한다.</summary>
        public void RefreshState()
        {
            if (_toggleItem != null)
            {
                _toggleItem.Checked = _isClipOn();
            }

            if (_stateItem != null)
            {
                _stateItem.Text = _describeRegion();
            }
        }

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            RefreshState();
        }

        private void OnToggleClicked(object sender, EventArgs e)
        {
            if (_onToggleClip != null)
            {
                _onToggleClip();
            }

            // 상태가 그대로여서 StateChanged가 오지 않는 경우에도 표시를 맞춘다.
            RefreshState();
        }

        private void OnNotifyIconMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _onOpenSettings != null)
            {
                _onOpenSettings();
            }
        }

        private void OnSettingsClicked(object sender, EventArgs e)
        {
            if (_onOpenSettings != null)
            {
                _onOpenSettings();
            }
        }

        private void OnExitClicked(object sender, EventArgs e)
        {
            if (_onExit != null)
            {
                _onExit();
            }
        }

        /// <summary>
        /// 알림 영역은 배율에 따라 16~32px 아이콘을 요청한다.
        /// Icon(Stream) 단일 인자 오버로드는 파일의 첫 항목(대개 256px)을 골라
        /// 작은 슬롯에 뭉갠 그림을 넣으므로, 크기를 지정하는 오버로드를 쓴다.
        /// </summary>
        private static Icon LoadIcon()
        {
            try
            {
                Uri uri = new Uri("pack://application:,,,/Resources/AppIcon.ico", UriKind.Absolute);
                System.Windows.Resources.StreamResourceInfo info =
                    System.Windows.Application.GetResourceStream(uri);

                if (info != null && info.Stream != null)
                {
                    using (System.IO.Stream stream = info.Stream)
                    {
                        return new Icon(stream, SystemInformation.SmallIconSize);
                    }
                }
            }
            catch (System.IO.IOException) { }
            catch (ArgumentException) { }

            return (Icon)SystemIcons.Application.Clone();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_notifyIcon != null)
            {
                // Visible을 먼저 내리지 않으면 사용자가 마우스를 올릴 때까지
                // 알림 영역에 유령 아이콘이 남는다.
                _notifyIcon.Visible = false;
                _notifyIcon.MouseUp -= OnNotifyIconMouseUp;
                _notifyIcon.ContextMenuStrip = null;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            if (_menu != null)
            {
                _menu.Opening -= OnMenuOpening;
                _menu.Dispose();
                _menu = null;
            }

            if (_icon != null)
            {
                _icon.Dispose();
                _icon = null;
            }
        }
    }
}
