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

    public async UniTask<bool> Run(T context, Command<T> command)
    {
        try
        {
            // 1. Edit
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

            // 5. Resolve
            InvokeSequential(_resolveEvent, context);

            // 6. Logic
            bool result = await command.Logic();

            if (!result)
            {
                return false;
            }

            // 7. FrontEnd
            InvokeSequential(_frontEndEvent, context);

            // 8. After
            InvokeSequential(_afterEvent, context);

            return true;
        }
        finally
        {
            context?.ResetContext();
        }
    }

    public (bool IsValid, T Context) PreviewRun(T context, Command<T> command)
    {
        _editEvent?.Invoke(context);

        if (_validationEvent != null)
        {
            foreach (var handler in _validationEvent.GetInvocationList()
                         .Cast<ValidationEventHandler>())
            {
                if (!handler(context))
                {
                    return (false, context);
                }
            }
        }

        if (!command.ValidateInCommand())
        {
            return (false, context);
        }

        return (true, context);
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