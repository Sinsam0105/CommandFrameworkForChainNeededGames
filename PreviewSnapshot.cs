using System.Collections.Generic;

/// <summary>
/// 한 번의 Preview 동안 유지되는 "보드 스냅샷".
/// real RuntimeData → preview clone 매핑을 공유 registry로 보관하므로:
/// - 같은 엔티티를 여러 번 참조해도 항상 같은 클론을 돌려준다(일관성).
/// - 최초 컨텍스트에 없던 엔티티도 나중에 GetClone으로 지연 복제하면 같은 그래프에 합류한다.
/// </summary>
public sealed class PreviewSnapshot
{
    private readonly IDictionary<object, object> _registry = DeepCloneHelper.NewRegistry();

    /// <summary>real을 이 스냅샷의 클론으로 매핑(이미 있으면 동일 클론 반환). preview 마킹됨.</summary>
    public T GetClone<T>(T real) where T : class
    {
        if (real == null) return null;
        return DeepCloneHelper.AutoClone(real, markPreview: true, _registry);
    }
}

/// <summary>
/// 게임 로직이 "지금 다뤄야 할 데이터"를 얻을 때 쓰는 헬퍼.
/// preview 중이면 현재 스냅샷의 클론을, 아니면 원본을 반환한다.
/// 카드/효과 로직이 다른 엔티티를 대상으로 할 때 Target = PreviewAware.Data(unit.RuntimeData)처럼 사용.
/// </summary>
public static class PreviewAware
{
    public static T Data<T>(T real) where T : class
    {
        var snapshot = CommandPreviewScope.Snapshot;
        return (CommandPreviewScope.IsActive && snapshot != null) ? snapshot.GetClone(real) : real;
    }
}
