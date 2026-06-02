using System;
using System.Linq;
using Cysharp.Threading.Tasks;

namespace Sinsam.CommandFramework
{
    public interface ICommandEvent
    {
        string CommandName { get; set; }
        Type ContextType { get; }
    }

    /// <summary>
    /// 커맨드 실행 파이프라인.
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

        internal async UniTask<bool> RunInternal(T context, Command<T> command, bool preview)
        {
            if (preview) CommandPreviewScope.Enter();
            try
            {
                _editEvent?.Invoke(context);

                if (RuntimeDataReflection.HasNullCheckViolation(context, out var nullField))
                {
                    UnityEngine.Debug.LogWarning(
                        $"[{CommandName}] NullCheck 실패: '{nullField}' 필드가 null이라 커맨드를 중단합니다.");
                    return false;
                }

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

                if (!command.ValidateInCommand())
                {
                    return false;
                }

                if (!preview)
                {
                    InvokeSequential(_beforeFrontEndEvent, context);
                }

                _resolveEvent?.Invoke(context);

                bool result = await command.Logic();
                if (!result)
                {
                    return false;
                }

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
                    context?.ResetContext();
                }
            }
        }

        public (bool IsValid, T Context) PreviewRun(T context, Command<T> command)
        {
            if (context == null)
            {
                return (false, null);
            }

            CommandPreviewScope.Enter();
            try
            {
                var clone = CommandPreviewScope.Snapshot.GetClone(context);
                var original = command.Context;
                command.Context = clone;
                try
                {
                    // Preview는 동기를 전제로 한다. AsyncLocal 기반 PreviewScope는 await 경계를
                    // 넘어 유지되지 않으므로, Logic이 실제로 await하면 중첩 커맨드가 preview가 아닌
                    // 실제 데이터를 건드릴 수 있다(또는 GetResult가 미완료 task를 블로킹).
                    // 따라서 Logic 호출 직후 즉시 완료됐는지 확인하고, 아니면 조용한 오염 대신
                    // 시끄러운 실패(throw)로 바꾼다.
                    var runTask = RunInternal(clone, command, preview: true);
                    if (!runTask.Status.IsCompleted())
                    {
                        throw new InvalidOperationException(
                            $"[{CommandName}] Preview Logic은 동기적으로 완료되어야 합니다. " +
                            $"Logic()이 preview 도중 실제 비동기 작업(await)을 수행했습니다. " +
                            $"AsyncLocal 기반 PreviewScope는 await 경계를 넘어 유지되지 않으므로, " +
                            $"중첩 커맨드가 preview가 아닌 실제 데이터를 건드릴 수 있습니다. " +
                            $"preview 대상 Logic은 즉시 완료되도록(동기) 작성하세요.");
                    }

                    bool valid = runTask.GetAwaiter().GetResult();
                    return (valid, clone);
                }
                finally
                {
                    command.Context = original;
                }
            }
            finally
            {
                CommandPreviewScope.Exit();
            }
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
}