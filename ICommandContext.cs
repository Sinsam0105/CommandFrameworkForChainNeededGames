/// <summary>
/// 모든 Command Context가 구현해야 하는 인터페이스.
/// ResetContext: 커맨드 실행 후 임시값 초기화 (Run에서 finally로 자동 호출)
/// SetContext:   임시값을 영구 반영할 때 호출
/// </summary>
public interface ICommandContext
{
    void ResetContext();
    void SetContext();
}