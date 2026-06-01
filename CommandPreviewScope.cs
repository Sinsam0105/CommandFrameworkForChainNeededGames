/// <summary>
/// "지금 Preview(가짜) 실행 중인가?"를 나타내는 ambient 스코프.
///
/// 용도:
/// - 파이프라인이 preview로 Logic을 돌리는 동안 활성화된다.
/// - Logic 안의 "데이터 외적" 부수효과(AP 소모, 카드 이동, UI 선택, Destroy, 효과 구독 등)는
///   IsActive일 때 건너뛴다. 데이터 변경(체력 등)은 그대로 실행되어 클론에 반영된다.
/// - Logic 도중 생성된 중첩 커맨드의 Execute()는 IsActive를 보고 자동으로 preview 경로를 탄다.
///
/// Unity는 기본적으로 단일 스레드(PlayerLoop)에서 돌고, preview 경로의 Logic은 동기로 완료되므로
/// 단순 정적 카운터로 충분하다.
/// </summary>
public static class CommandPreviewScope
{
    private static int _depth;

    public static bool IsActive => _depth > 0;

    /// <summary>현재 preview의 보드 스냅샷(최상위 진입 시 생성, 모든 깊이에서 공유).</summary>
    public static PreviewSnapshot Snapshot { get; private set; }

    public static void Enter()
    {
        if (_depth == 0) Snapshot = new PreviewSnapshot();
        _depth++;
    }

    public static void Exit()
    {
        if (_depth <= 0) return;
        _depth--;
        if (_depth == 0) Snapshot = null;
    }
}
