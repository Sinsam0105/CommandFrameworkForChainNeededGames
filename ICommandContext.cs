/// <summary>
/// 모든 Command Context가 구현해야 하는 인터페이스.
/// ResetContext: 커맨드 실행 후 임시값 초기화 (Run에서 finally로 자동 호출)
/// SetContext:   임시값을 영구 반영할 때 호출
/// </summary>
public interface ICommandContext
{
    void ResetContext() 
    void SetContext()
}

/// <summary>
/// Context가 필요 없는 커맨드용 빈 컨텍스트.
/// 비제네릭 베이스 클래스 없이도 Context-free 커맨드를 만들 수 있다.
/// </summary>
public sealed class EmptyContext : ICommandContext
{
    public static readonly EmptyContext Default = new();
}
