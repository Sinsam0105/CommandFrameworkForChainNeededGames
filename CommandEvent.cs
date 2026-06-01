using System;
using System.Linq;
using Cysharp.Threading.Tasks;

public interface ICommandEvent
{
    string CommandName { get; set; }
    Type ContextType { get; }
}

/// <summary>
/// 커맨드 실행 파이프라인.
/// 
/// 실행 순서:
///   1. EditEvent                    — Context 수치 보정
///   2. ValidationEvent              — 하나라도 false면 중단
///   3. ValidateInCommand()          — 커맨드 자체 검증
///   4. BeforeFrontEndEvent          — Logic 직전 처리
///   5. ResolveEvent                 — Context 최종 보정
///   6. Command.Logic()              — 핵심 로직 실행
///   7. FrontEndEvent                — UI 연출 등
///   8. AfterEvent                   — 후처리
///   finally: Context.ResetContext()
/// </summary>
public class CommandEvent<T> : ICommandEvent where T : class, ICommandContext
{
    public string CommandName { get; set; } = string.Empty;
    public Type ContextType => typeof(T);

    public delegate bool ValidationEventHandler(T context);
    public delegate void EditEventHandler(T context);
    public delegate void ResolveEventHandler(T context);
    public delegate void CommandEventHandler(T context);

    private ValidationEventHandler _validationEvent;
    private EditEventHandler _editEvent;
    private ResolveEventHandler _resolveEvent;
    private CommandEventHandler _beforeFrontEndEvent;
    private CommandEventHandler _frontEndEvent;
    private CommandEventHandler _afterEvent;

    public event ValidationEventHandler ValidationEvent
    {
        add => _validationEvent += value;
        remove => _validationEvent -= value;
    }

    public event EditEventHandler EditEvent
    {
        add => _editEvent += value;
        remove => _editEvent -= value;
    }

    public event ResolveEventHandler ResolveEvent
    {
        add => _resolveEvent += value;
        remove => _resolveEvent -= value;
    }

    public event CommandEventHandler BeforeFrontEndEvent
    {
        add => _beforeFrontEndEvent += value;
        remove => _beforeFrontEndEvent -= value;
    }

    public event CommandEventHandler FrontEndEvent
    {
        add => _frontEndEvent += value;
        remove => _frontEndEvent -= value;
    }

    public event CommandEventHandler AfterEvent
    {
        add => _afterEvent += value;
        remove => _afterEvent -= value;
    }

    public UniTask<bool> Run(T context, Command<T> command)
        => RunInternal(context, command, preview: false);

    /// <summary>
    /// 실제/Preview 공통 파이프라인.
    ///
    /// 데이터 단계(Edit → NullCheck → Validation → Resolve → Logic)는 preview에서도 실행된다.
    /// preview에서는 클론 위에서 Logic이 돌고, Logic 안의 "데이터 외적" 부수효과는
    /// CommandPreviewScope.IsActive 가드로 건너뛴다(AP/카드이동/UI선택/Destroy/효과구독 등).
    ///
    /// 연출·후처리 그룹(Before/FrontEnd/After)과 ResetContext는 preview에서 건너뛴다.
    /// preview 동안 CommandPreviewScope를 활성화하여 Logic 도중 생성된 중첩 커맨드도
    /// 자동으로 preview 경로를 타게 한다.
    /// </summary>
    internal async UniTask<bool> RunInternal(T context, Command<T> command, bool preview)
    {
        if (preview) CommandPreviewScope.Enter();
        try
        {
            // 1. Edit
            _editEvent?.Invoke(context);

            // 1.5 자동 NullCheck ([NullCheck] 필드가 null이면 무효)
            if (RuntimeDataReflection.HasNullCheckViolation(context, out var nullField))
            {
                UnityEngine.Debug.LogWarning(
                    $"[{CommandName}] NullCheck 실패: '{nullField}' 필드가 null이라 커맨드를 중단합니다.");
                return false;
            }

            // 2. 외부 Validation
            if (_validationEvent != null)
            {
                foreach (var handler in _validationEvent.GetInvocationList()
                             .Cast<ValidationEventHandler>())
                {
                    if (!handler(context))
                    {
                        return false;
                    }
                }
            }

            // 3. 커맨드 자체 Validation
            if (!command.ValidateInCommand())
            {
                return false;
            }

            // 4. Before (연출/후처리 그룹 — preview에서는 건너뜀)
            if (!preview)
            {
                InvokeSequential(_beforeFrontEndEvent, context);
            }

            // 5. Resolve (데이터 최종 보정 — preview에서도 실행)
            _resolveEvent?.Invoke(context);

            // 6. Logic (preview에서도 실행 — 데이터 변경은 클론에 반영, 부수효과는 스코프 가드로 차단)
            bool result = await command.Logic();
            if (!result)
            {
                return false;
            }

            // 7~8. FrontEnd/After (연출/후처리 그룹 — preview에서는 건너뜀)
            if (!preview)
            {
                InvokeSequential(_frontEndEvent, context);
                InvokeSequential(_afterEvent, context);
            }

            return true;
        }
        finally
        {
            if (preview)
            {
                CommandPreviewScope.Exit();
            }
            else
            {
                // Preview 사본은 그대로 버리므로 Reset이 필요 없다.
                context?.ResetContext();
            }
        }
    }

    /// <summary>
    /// 최상위 Preview 진입점.
    /// 실제 Context를 깊은 복사하여 Preview 사본을 만들고, 그 사본에 파이프라인을 적용한다.
    /// 실제 데이터는 일절 변경되지 않으므로 호출 후 ResetContext가 필요 없다.
    /// 반환되는 Context는 효과가 적용된 사본(PreviewInstance)이다.
    /// </summary>
    public (bool IsValid, T Context) PreviewRun(T context, Command<T> command)
    {
        if (context == null)
        {
            return (false, null);
        }

        var clone = DeepCloneHelper.AutoClone(context, markPreview: true);

        // 검증/Edit이 사본을 보도록 command.Context를 잠시 사본으로 교체한다.
        var original = command.Context;
        command.Context = clone;
        bool valid;
        try
        {
            // preview=true이므로 Logic에 도달하기 전에 동기적으로 완료된다.
            valid = RunInternal(clone, command, preview: true).GetAwaiter().GetResult();
        }
        finally
        {
            command.Context = original;
        }

        return (valid, clone);
    }

    private static void InvokeSequential(CommandEventHandler evt, T context)
    {
        if (evt == null)
        {
            return;
        }

        foreach (CommandEventHandler handler in evt.GetInvocationList())
        {
            handler(context);
        }
    }
}