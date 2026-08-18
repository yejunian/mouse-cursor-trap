namespace MouseCursorTrap.Models
{
    /// <summary>커서 영역 제한의 상태.</summary>
    internal enum ClipState
    {
        /// <summary>제한 없음. 커서가 모든 디스플레이를 자유롭게 이동한다.</summary>
        Off = 0,

        /// <summary>제한 중. 감시 타이머가 돌면서 Windows가 푼 제한을 다시 건다.</summary>
        On = 1,
    }

    /// <summary>제한이 마지막으로 꺼진 이유. 설정 창에서 안내 문구를 고르는 데 쓴다.</summary>
    internal enum ClipOffReason
    {
        /// <summary>사용자가 직접 껐거나 아직 켠 적이 없다.</summary>
        None = 0,

        /// <summary>디스플레이 구성이 바뀌어 자동으로 해제했다.</summary>
        DisplayChanged = 1,

        /// <summary>다른 프로그램이 커서 영역을 점유해 자리를 비켜 주었다.</summary>
        ForeignClip = 2,
    }
}
