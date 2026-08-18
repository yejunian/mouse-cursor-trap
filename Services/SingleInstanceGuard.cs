using System;
using System.Threading;

namespace MouseCursorTrap.Services
{
    /// <summary>
    /// 애플리케이션이 한 번에 하나만 돌게 하고, 두 번째 인스턴스가 첫 인스턴스에게
    /// "설정 창을 띄워 달라"고 알릴 수 있게 한다.
    ///
    /// 스코프는 Local(세션 단위)이다. 커서 제한은 window station/세션 개념이므로
    /// 빠른 사용자 전환으로 로그온한 다른 사용자는 자기 인스턴스를 가져야 한다.
    /// </summary>
    internal sealed class SingleInstanceGuard : IDisposable
    {
        private const string MutexName =
            @"Local\MouseCursorTrap.SingleInstance.{8DBFEA9E-071B-49B5-959C-4D288BADEB3E}";
        private const string EventName =
            @"Local\MouseCursorTrap.ShowSettings.{8DBFEA9E-071B-49B5-959C-4D288BADEB3E}";

        // 지역 변수로 두면 GC되어 뮤텍스가 버려지므로 반드시 필드로 붙잡아 둔다.
        private Mutex _mutex;
        private EventWaitHandle _showEvent;
        private RegisteredWaitHandle _registration;

        /// <summary>다른 인스턴스가 설정 창 표시를 요청했다. 스레드 풀 스레드에서 발생한다.</summary>
        public event EventHandler ShowRequested;

        /// <summary>이 프로세스가 첫 인스턴스면 true.</summary>
        public bool TryAcquire()
        {
            bool createdNew;
            // WaitOne(timeout)은 확인과 획득 사이에 레이스 창을 만들므로 쓰지 않는다.
            _mutex = new Mutex(true, MutexName, out createdNew);
            return createdNew;
        }

        /// <summary>두 번째 인스턴스가 호출한다. 신호만 보내고 즉시 종료하면 된다.</summary>
        public void SignalRunningInstance()
        {
            try
            {
                bool created;
                using (EventWaitHandle handle =
                    new EventWaitHandle(false, EventResetMode.AutoReset, EventName, out created))
                {
                    handle.Set();
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (System.IO.IOException) { }
        }

        /// <summary>
        /// 첫 인스턴스가 호출한다.
        /// 두 번째 인스턴스가 먼저 Set()했더라도 AutoReset 이벤트가 신호 상태로 남아 있어
        /// 등록 즉시 콜백이 발생한다. 따라서 순서 레이스가 없다.
        /// </summary>
        public void StartListening()
        {
            bool created;
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName, out created);
            _registration = ThreadPool.RegisterWaitForSingleObject(
                _showEvent, OnSignal, null, Timeout.Infinite, false);
        }

        private void OnSignal(object state, bool timedOut)
        {
            EventHandler handler = ShowRequested;
            if (handler != null)
            {
                // 스레드 풀 스레드다. 구독자가 Dispatcher로 마샬링해야 한다.
                handler(this, EventArgs.Empty);
            }
        }

        public void StopListening()
        {
            if (_registration != null)
            {
                _registration.Unregister(null);
                _registration = null;
            }

            if (_showEvent != null)
            {
                _showEvent.Close();
                _showEvent = null;
            }
        }

        public void Dispose()
        {
            StopListening();

            if (_mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException) { }

                _mutex.Close();
                _mutex = null;
            }
        }
    }
}
