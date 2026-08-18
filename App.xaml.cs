using System;
using System.Windows;
using System.Windows.Threading;
using MouseCursorTrap.Interop;
using MouseCursorTrap.Services;

namespace MouseCursorTrap
{
    /// <summary>
    /// 컴포지션 루트.
    /// StartupUri 없이 트레이 상주로 시작하며, ShutdownMode는 OnExplicitShutdown이다.
    /// 기본값인 OnLastWindowClose면 설정 창을 숨기는 순간 프로세스가 끝나 버린다.
    /// </summary>
    public partial class App : Application
    {
        private SingleInstanceGuard _guard;
        private AppController _controller;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 중복 실행 방지는 UI를 만들기 전에 가장 먼저 한다.
            _guard = new SingleInstanceGuard();
            if (!_guard.TryAcquire())
            {
                // 이미 실행 중이면 그쪽 설정 창을 띄우게 하고 조용히 물러난다.
                _guard.SignalRunningInstance();
                _guard.Dispose();
                _guard = null;
                Shutdown(0);
                return;
            }

            // 커서가 갇힌 채 남는 것이 이 프로그램 최악의 실패 모드다.
            // 예상 못 한 경로로 죽더라도 제한만은 반드시 풀고 나간다.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            SessionEnding += OnSessionEnding;

            _controller = new AppController(_guard);
            _controller.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_controller != null)
            {
                _controller.Dispose();
                _controller = null;
            }

            if (_guard != null)
            {
                _guard.Dispose();
                _guard = null;
            }

            AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

            base.OnExit(e);
        }

        private static void ReleaseCursorClip()
        {
            try
            {
                NativeMethods.ClipCursorRelease(IntPtr.Zero);
            }
            catch (Exception)
            {
                // 여기서 더 할 수 있는 일이 없다.
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ReleaseCursorClip();
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ReleaseCursorClip();
        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            ReleaseCursorClip();
        }

        private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            ReleaseCursorClip();

            if (_controller != null)
            {
                _controller.Dispose();
                _controller = null;
            }
        }
    }
}
