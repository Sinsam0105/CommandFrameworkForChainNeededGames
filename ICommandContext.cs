/// <summary>
/// 모든 Command Context가 구현해야 하는 인터페이스.
/// ResetContext: 커맨드 실행 후 임시값 초기화 (Run에서 finally로 자동 호출)
/// SetContext:   임시값을 영구 반영할 때 호출
/// IsPreview:    이 Context가 Preview(가짜) 사본인지 여부.
///               true면 파이프라인이 commit·부수효과 없이 동작한다(중첩 커맨드 전파용).
/// </summary>
public interface ICommandContext : IPreviewable
{
    void ResetContext();
    void SetContext();
}
