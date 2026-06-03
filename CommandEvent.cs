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
    ///
    /// 실행 순서:
    ///   [Preview 포함]
    ///     EditAsync (Preview 스킵) → Edit → Validation
    ///     → ValidateInCommand → Logic
    ///   [Preview 스킵]
    ///     → BeforeFrontEndAsync → BeforeFrontEnd
    ///     → FrontEndAsync → FrontEnd
    ///     → AfterAsync → After
    /// </summary>
    public class CommandEvent<T> : ICommandEvent where T : class, ICommandContext
    {
        public string CommandName { get; set; } = string.Empty;
        public Type ContextType => typeof(T);

        // ── Delegate 정의 ──────────────────────────────────────────────
        public delegate bool ValidationEventHandler(T context);
        public delegate UniTask AsyncEventHandler(T context);
        public delegate void EditEventHandler(T context);
        public delegate void CommandEventHandler(T context);

        // ── Async 이벤트 ───────────────────────────────────────────────
        private AsyncEventHandler _editAsyncEvent;
        private AsyncEventHandler _beforeFrontEndAsyncEvent;
        private AsyncEventHandler _frontEndAsyncEvent;
        private AsyncEventHandler _afterAsyncEvent;

        // ── Sync 이벤트 ────────────────────────────────────────────────
        private EditEventHandler _editEvent;
        private ValidationEventHandler _validationEvent;
        private CommandEventHandler _beforeFrontEndEvent;
        private CommandEventHandler _frontEndEvent;
        private CommandEventHandler _afterEvent;

        // ── Async 이벤트 등록 ──────────────────────────────────────────
        /// <summary>선택 대기, 외부 입력 주입 등. Preview에서 스킵된다.</summary>
        public event AsyncEventHandler EditAsyncEvent
        {
            add => _editAsyncEvent += value;
            remove => _editAsyncEvent -= value;
        }

        public event AsyncEventHandler BeforeFrontEndAsyncEvent
        {
            add => _beforeFrontEndAsyncEvent += value;
            remove => _beforeFrontEndAsyncEvent -= value;
        }

        public event AsyncEventHandler FrontEndAsyncEvent
        {
            add => _frontEndAsyncEvent += value;
            remove => _frontEndAsyncEvent -= value;
        }

        public event AsyncEventHandler AfterAsyncEvent
        {
            add => _afterAsyncEvent += value;
            remove => _afterAsyncEvent -= value;
        }

        // ── Sync 이벤트 등록 ───────────────────────────────────────────
        public event EditEventHandler EditEvent
        {
            add => _editEvent += value;
            remove => _editEvent -= value;
        }

        public event ValidationEventHandler ValidationEvent
        {
            add => _validationEvent += value;
            remove => _validationEvent -= value;
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

        // ── 실행 ───────────────────────────────────────────────────────
        public UniTask<bool> Run(T context, Command<T> command)
            => RunInternal(context, command, preview: false);

        internal async UniTask<bool> RunInternal(T context, Command<T> command, bool preview)
        {
            if (context == null || command == null)
                return false;

            T originalCommandContext = null;
            bool restoreCommandContext = false;

            if (preview) CommandPreviewScope.Enter();
            try
            {
                // PreviewScope 안에서 실제 Context가 들어오면 같은 Snapshot registry를 통해 자동으로 사본으로 치환한다.
                if (preview && !context.IsPreview)
                {
                    var snapshot = CommandPreviewScope.Snapshot;
                    if (snapshot != null)
                    {
                        originalCommandContext = command.Context;
                        context = snapshot.GetClone(context);
                        command.Context = context;
                        restoreCommandContext = true;
                    }
                }

                // EditAsync: 선택 대기 등 비동기 주입. Preview에서는 스킵.
                if (!preview && _editAsyncEvent != null)
                {
                    await InvokeSequentialAsync(_editAsyncEvent, context);
                }

                _editEvent?.Invoke(context);

                if (RuntimeDataReflection.HasNullCheckViolation(context, out var nullField))
                {
                    UnityEngine.Debug.LogWarning(
                        $"[{CommandName}] NullCheck 실패: '{nullField}' 필드가 null이라 커맨드를 중단합니다.");
                    return false;
                }

                // Validation (sync)
                if (_validationEvent != null)
                {
                    foreach (var handler in _validationEvent.GetInvocationList()
                                 .Cast<ValidationEventHandler>())
                    {
                        if (!handler(context))
                            return false;
                    }
                }

                if (!command.ValidateInCommand())
                    return false;

                // Logic (sync) — Preview는 여기까지
                bool result = command.Logic();
                if (!result)
                    return false;

                if (!preview)
                {
                    if (_beforeFrontEndAsyncEvent != null)
                        await InvokeSequentialAsync(_beforeFrontEndAsyncEvent, context);

                    InvokeSequential(_beforeFrontEndEvent, context);

                    if (_frontEndAsyncEvent != null)
                        await InvokeSequentialAsync(_frontEndAsyncEvent, context);

                    InvokeSequential(_frontEndEvent, context);

                    if (_afterAsyncEvent != null)
                        await InvokeSequentialAsync(_afterAsyncEvent, context);

                    InvokeSequential(_afterEvent, context);
                }

                return true;
            }
            finally
            {
                if (restoreCommandContext)
                    command.Context = originalCommandContext;

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

        /// <summary>
        /// Logic이 sync이므로 Preview는 항상 동기적으로 완료된다.
        /// EditAsync는 스킵되므로 Context에 이미 필요한 값이 채워진 상태여야 한다.
        /// </summary>
        public (bool IsValid, T Context) PreviewRun(T context, Command<T> command)
        {
            if (context == null || command == null)
                return (false, null);

            CommandPreviewScope.Enter();
            try
            {
                var clone = CommandPreviewScope.Snapshot.GetClone(context);
                var original = command.Context;
                command.Context = clone;
                try
                {
                    var runTask = RunInternal(clone, command, preview: true);

                    // Logic이 sync이고 Preview에서는 async/front-end 계열이 스킵되므로 즉시 완료되어야 한다.
                    // 완료되지 않았다면 Preview 파이프라인 설계 위반으로 본다.
                    if (!runTask.Status.IsCompleted())
                    {
                        throw new InvalidOperationException(
                            $"[{CommandName}] Preview 실행이 동기적으로 완료되지 않았습니다. " +
                            $"Logic()은 sync여야 하며, Preview 파이프라인이 실제 await를 수행해선 안 됩니다.");
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

        // ── 헬퍼 ───────────────────────────────────────────────────────
        private static void InvokeSequential(CommandEventHandler evt, T context)
        {
            if (evt == null) return;
            foreach (CommandEventHandler handler in evt.GetInvocationList())
                handler(context);
        }

        private static async UniTask InvokeSequentialAsync(AsyncEventHandler evt, T context)
        {
            if (evt == null) return;
            foreach (AsyncEventHandler handler in evt.GetInvocationList())
                await handler(context);
        }
    }
}