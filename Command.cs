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
    /// EventBus에서 CommandEvent를 꺼내 파이프라인(Validation → Edit → Logic → FrontEnd → After)을 실행.
    /// </summary>
    public UniTask<bool> Execute()
    {
        var commandEvent = CommandEventRegistry.CommandEvents.GetOrCreate<T>(GetType());
        return commandEvent.Run(Context, this);
    }

    /// <summary>
    /// EditEvent까지만 실행하여 최종 Context 상태를 미리 본다.
    /// Logic은 실행되지 않으므로 부수효과 없음.
    /// 주의: Preview 후 반드시 Context.ResetContext()를 호출할 것.
    /// </summary>
    public (bool IsValid, T Context) Preview()
    {
        var commandEvent = CommandEventRegistry.CommandEvents.GetOrCreate<T>(GetType());
        return commandEvent.PreviewRun(Context, this);
    }
}
