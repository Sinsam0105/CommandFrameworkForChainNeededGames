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
    /// 일반 실행 순서:
    ///   EditAsync → Edit → Validation → ValidateInCommand → Logic
    ///   → BeforeFrontEndAsync → BeforeFrontEnd
    ///   → FrontEndAsync → FrontEnd
    ///   → AfterAsync → After
    ///
    /// PreviewRun은 Edit → Validation → ValidateInCommand → Logic까지만 실행한다.
    /// AsyncPreviewRun은 EditAsync와 front-end 계열을 포함하며, 옵션에 따라 After 계열까지 실행한다.
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
        /// <summary>선택 대기, 외부 입력 주입 등. 기본 PreviewRun에서는 스킵되고 AsyncPreviewRun에서는 실행된다.</summary>
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

            try
            {
                if (!preview && _editAsyncEvent != null)
                    await InvokeSequentialAsync(_editAsyncEvent, context);

                bool result = RunSyncCore(context, command);
                if (!result)
                    return false;

                if (!preview)
                    await RunFrontEndAndAfterAsync(context, runAfterEvents: true);

                return true;
            }
            finally
            {
                if (!preview)
                    context?.ResetContext();
            }
        }

        /// <summary>
        /// Logic이 sync이므로 기본 PreviewRun은 항상 동기적으로 완료된다.
        /// EditAsync와 front-end 계열은 실행하지 않는다.
        /// </summary>
        public (bool IsValid, T Context) PreviewRun(T context, Command<T> command)
        {
            if (context == null || command == null)
                return (false, null);

            var session = new PreviewSession();
            var clone = session.GetClone(context);
            var original = command.Context;
            command.Context = clone;

            try
            {
                bool valid = RunSyncCore(clone, command);
                return (valid, clone);
            }
            finally
            {
                command.Context = original;
            }
        }

        /// <summary>
        /// PreviewSession이 주입된 clone context에서 async/front-end 이벤트까지 포함해 preview 파이프라인을 실행한다.
        /// runAfterEvents=true면 AfterAsync/After도 preview context를 들고 발행된다.
        /// </summary>
        public async UniTask<(bool IsValid, T Context)> AsyncPreviewRun(T context, Command<T> command, bool runAfterEvents = true)
        {
            if (context == null || command == null)
                return (false, null);

            var session = new PreviewSession();
            var clone = session.GetClone(context);
            var original = command.Context;
            command.Context = clone;

            try
            {
                if (_editAsyncEvent != null)
                    await InvokeSequentialAsync(_editAsyncEvent, clone);

                bool valid = RunSyncCore(clone, command);
                if (!valid)
                    return (false, clone);

                await RunFrontEndAndAfterAsync(clone, runAfterEvents);
                return (true, clone);
            }
            finally
            {
                command.Context = original;
            }
        }

        private bool RunSyncCore(T context, Command<T> command)
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
                        return false;
                }
            }

            if (!command.ValidateInCommand())
                return false;

            return command.Logic();
        }

        private async UniTask RunFrontEndAndAfterAsync(T context, bool runAfterEvents)
        {
            if (_beforeFrontEndAsyncEvent != null)
                await InvokeSequentialAsync(_beforeFrontEndAsyncEvent, context);

            InvokeSequential(_beforeFrontEndEvent, context);

            if (_frontEndAsyncEvent != null)
                await InvokeSequentialAsync(_frontEndAsyncEvent, context);

            InvokeSequential(_frontEndEvent, context);

            if (!runAfterEvents)
                return;

            if (_afterAsyncEvent != null)
                await InvokeSequentialAsync(_afterAsyncEvent, context);

            InvokeSequential(_afterEvent, context);
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