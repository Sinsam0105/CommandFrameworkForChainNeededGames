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
///   1. ValidationEvent (외부 구독자) — 하나라도 false면 중단
///   2. ValidateInCommand()          — 커맨드 자체 검증
///   3. EditEvent                    — Context 수치 보정 (데미지 증감 등)
///   4. BeforeFrontEndEvent          — Logic 직전 처리
///   5. Command.Logic()              — 핵심 로직 실행
///   6. FrontEndEvent                — UI 연출 등
///   7. AfterEvent                   — 후처리
///   finally: Context.ResetContext() — 임시값 정리 보장
///
/// 모든 delegate가 T를 직접 받으므로 구독자 쪽 캐스팅 불필요.
/// </summary>
public class CommandEvent<T> : ICommandEvent where T : class, ICommandContext
{
    public string CommandName { get; set; }
    public Type ContextType => typeof(T);

    public delegate bool ValidationEventHandler(T context);
    public delegate void EditEventHandler(T context);
    public delegate void EventHandler(T context);

    private ValidationEventHandler _validationEvent;
    private EditEventHandler _editEvent;
    private EventHandler _beforeFrontEndEvent;
    private EventHandler _frontEndEvent;
    private EventHandler _afterEvent;

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

    public event EventHandler BeforeFrontEndEvent
    {
        add => _beforeFrontEndEvent += value;
        remove => _beforeFrontEndEvent -= value;
    }

    public event EventHandler FrontEndEvent
    {
        add => _frontEndEvent += value;
        remove => _frontEndEvent -= value;
    }

    public event EventHandler AfterEvent
    {
        add => _afterEvent += value;
        remove => _afterEvent -= value;
    }

    /// <summary>
    /// 전체 파이프라인 실행. finally로 ResetContext 보장.
    /// </summary>
    public async UniTask<bool> Run(T context, Command<T> command)
    {
        try
        {
            // 1. Edit (수치 보정)
            _editEvent?.Invoke(context);
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
            // 4. Before
            InvokeSequential(_beforeFrontEndEvent, context);

            // 5. Logic
            bool result = await command.Logic();
            if (!result)
            {
                return false;
            }
            // 6. FrontEnd
            InvokeSequential(_frontEndEvent, context);

            // 7. After
            InvokeSequential(_afterEvent, context);

            return true;
        }
        finally
        {
            context?.ResetContext();
        }
    }

    /// <summary>
    /// Validation + Edit까지만 실행. Logic은 호출하지 않는다.
    /// 카드 프리뷰, 데미지 미리보기 등에 사용.
    /// 주의: 반환 후 caller가 context.ResetContext()를 호출해야 한다.
    /// </summary>
    public (bool IsValid, T Context) PreviewRun(T context, Command<T> command)
    {
        if (_validationEvent != null)
        {
            foreach (var handler in _validationEvent.GetInvocationList()
                         .Cast<ValidationEventHandler>())
            {
                if (!handler(context))
                    return (false, context);
            }
        }

        if (!command.ValidateInCommand())
            return (false, context);

        _editEvent?.Invoke(context);

        return (true, context);
    }

    private static void InvokeSequential(EventHandler evt, T context)
    {
        if (evt == null)
            return;

        foreach (EventHandler handler in evt.GetInvocationList())
            handler(context);
    }
}
