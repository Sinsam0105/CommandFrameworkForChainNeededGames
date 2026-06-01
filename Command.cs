using Cysharp.Threading.Tasks;

/// <summary>
/// 커맨드 패턴 베이스.
/// - Hemi: 제네릭 단일 계층, Context를 타입 안전하게 노출
/// - PRC:  ValidateInCommand()로 커맨드 자체 검증 지원
///
/// 사용법:
///   var cmd = new AttackCommand { Context = new HealthCommandContext(...) };
///   bool success = await cmd.Execute();
/// </summary>
public abstract class Command<T> where T : class, ICommandContext
{
    public T Context;

    /// <summary>
    /// 커맨드 자체의 유효성 검증. 
    /// CommandEvent.Run에서 외부 ValidationEvent 통과 후 호출된다.
    /// 기본 구현은 Context null 체크만 수행.
    /// </summary>
    public virtual bool ValidateInCommand()
    {
        return Context != null;
    }

    /// <summary>
    /// 실제 비즈니스 로직. 서브클래스에서 구현.
    /// </summary>
    public abstract UniTask<bool> Logic();

    /// <summary>
    /// 커맨드 실행 진입점.
    /// EventBus에서 CommandEvent를 꺼내 파이프라인(Edit → Validation → Logic → FrontEnd → After)을 실행.
    ///
    /// Context가 이미 Preview 사본(IsPreview=true)이면 — 즉 Preview 도중 Logic에서 만들어진
    /// 중첩 커맨드라면 — commit·부수효과 없이 in-place로 preview 경로를 탄다(재복사하지 않음).
    /// </summary>
    public UniTask<bool> Execute()
    {
        var commandEvent = CommandEventRegistry.GetOrCreate<T>(GetType());
        // 이미 preview 사본이거나(Context.IsPreview), preview 실행 도중 생성된 중첩 커맨드면
        // (CommandPreviewScope.IsActive) commit·부수효과 없이 in-place preview 경로로.
        if ((Context != null && Context.IsPreview) || CommandPreviewScope.IsActive)
        {
            return commandEvent.RunInternal(Context, this, preview: true);
        }
        return commandEvent.Run(Context, this);
    }

    /// <summary>
    /// 실제 데이터를 깊은 복사한 Preview 사본에 파이프라인을 적용해 최종 상태를 미리 본다.
    /// 실제 Context/엔티티는 변경되지 않으므로 ResetContext 호출이 필요 없다.
    /// 반환되는 Context는 효과가 적용된 사본(PreviewInstance)이다.
    /// (Phase 0: Edit/Validation 단계까지. Logic-in-preview는 부수효과 분리 후 개방.)
    /// </summary>
    public (bool IsValid, T Context) Preview()
    {
        var commandEvent = CommandEventRegistry.GetOrCreate<T>(GetType());
        return commandEvent.PreviewRun(Context, this);
    }
}
