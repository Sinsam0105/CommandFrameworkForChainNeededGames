using System;
using System.Collections.Generic;
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
    ///   → AfterCommands 수집 → OneCommand Reset → CommandSession queue drain → SessionEnded
    ///
    /// PreviewRun은 Edit → Validation → ValidateInCommand → Logic까지만 실행한다.
    /// AsyncPreviewRun은 EditAsync까지 포함하며, PreviewAfterMode에 따라 AfterCommands를 수집/시뮬레이션한다.
    /// FrontEnd/After side-effect event는 preview에서 실행하지 않는다.
    /// </summary>
    public class CommandEvent<T> : ICommandEvent where T : class, ICommandContext
    {
        public string CommandName { get; set; } = string.Empty;
        public Type ContextType => typeof(T);

        public delegate bool ValidationEventHandler(T context);
        public delegate UniTask AsyncEventHandler(T context);
        public delegate void EditEventHandler(T context);
        public delegate void CommandEventHandler(T context);
        public delegate IEnumerable<ICommand> AfterCommandHandler(T context, CommandSession session);

        private AsyncEventHandler _editAsyncEvent;
        private AsyncEventHandler _beforeFrontEndAsyncEvent;
        private AsyncEventHandler _frontEndAsyncEvent;

        private EditEventHandler _editEvent;
        private ValidationEventHandler _validationEvent;
        private CommandEventHandler _beforeFrontEndEvent;
        private CommandEventHandler _frontEndEvent;
        private AfterCommandHandler _afterCommandsEvent;

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

        /// <summary>
        /// 현재 command 이후 이어질 후속 command들을 생성한다.
        /// 직접 side effect를 수행하지 말고 Command를 반환해야 한다.
        /// </summary>
        public event AfterCommandHandler AfterCommands
        {
            add => _afterCommandsEvent += value;
            remove => _afterCommandsEvent -= value;
        }

        public UniTask<bool> Run(T context, Command<T> command, CommandSession session = null)
        {
            if (context == null || command == null)
                return UniTask.FromResult(false);

            session ??= CommandSession.Resolve(context, context.IsPreview);
            return RunInternal(context, command, session, preview: session.IsPreview);
        }

        internal async UniTask<bool> RunInternal(T context, Command<T> command, CommandSession session, bool preview)
        {
            if (context == null || command == null || session == null)
                return false;

            session.EnterCommand(context);
            bool success = false;
            bool shouldDrainAfterCommands = !preview || session.PreviewAfterMode == PreviewAfterMode.SimulateCommands;

            try
            {
                // EditAsync/Edit/Logic 전 구간을 동일하게 가드하려면 EditAsync 실행 '이전'에 캡처해야 한다.
                // 그렇지 않으면 EditAsync만 [CommandReadOnly] 필드를 교체할 수 있는 비대칭이 생긴다.
                var snapshot = session.MutationGuard.Capture(context);

                if (_editAsyncEvent != null)
                    await InvokeSequentialAsync(_editAsyncEvent, context);

                success = RunSyncCore(context, command);
                snapshot.ThrowIfViolated(CommandName);

                if (!success)
                    return false;

                if (!preview)
                    await RunFrontEndAsync(context);

                if (!preview || session.PreviewAfterMode != PreviewAfterMode.None)
                    CollectAfterCommands(context, session);

                return true;
            }
            finally
            {
                if (!preview)
                {
                    // ResetContext()가 throw해도 ExitCommandAsync는 반드시 실행되어야 한다.
                    // 그렇지 않으면 session depth가 줄지 않아 세션이 누수된다.
                    try
                    {
                        context?.ResetContext();
                    }
                    catch (Exception resetEx)
                    {
                        UnityEngine.Debug.LogException(resetEx);
                    }
                }

                await session.ExitCommandAsync(success && shouldDrainAfterCommands);
            }
        }

        /// <summary>
        /// Logic이 sync이므로 기본 PreviewRun은 항상 동기적으로 완료된다.
        /// EditAsync, FrontEnd, AfterCommands는 실행하지 않는다.
        /// </summary>
        public (bool IsValid, T Context) PreviewRun(T context, Command<T> command)
        {
            if (context == null || command == null)
                return (false, null);

            var session = CreatePreviewSession(context, PreviewAfterMode.None);
            var clone = session.GetPreviewClone(context);
            var original = command.Context;
            command.Context = clone;

            // 실제 실행과 동일한 세션 depth 수명(EnterCommand/ExitCommandAsync)을 적용한다.
            session.EnterCommand(clone);
            try
            {
                var snapshot = session.MutationGuard.Capture(clone);
                bool valid = RunSyncCore(clone, command);
                snapshot.ThrowIfViolated(CommandName);
                return (valid, clone);
            }
            finally
            {
                command.Context = original;

                // PreviewRun은 EditAsync/FrontEnd/AfterCommands를 실행하지 않으므로
                // ExitCommandAsync는 depth 감소와 EndIfIdle만 수행하며 동기적으로 완료된다고 가정한다.
                // (preview 세션에 async SessionEnded 핸들러가 없다는 전제. 위반 시 GetResult가 throw한다.)
                session.ExitCommandAsync(drainAfterCommands: false).GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// EditAsync까지 포함해 preview 파이프라인을 실행한다.
        /// FrontEnd 이벤트는 실행하지 않는다.
        /// AfterCommands는 afterMode에 따라 수집 또는 preview session 위에서 시뮬레이션한다.
        /// </summary>
        public async UniTask<(bool IsValid, T Context)> AsyncPreviewRun(
            T context,
            Command<T> command,
            PreviewAfterMode afterMode = PreviewAfterMode.None)
        {
            if (context == null || command == null)
                return (false, null);

            var session = CreatePreviewSession(context, afterMode);
            var clone = session.GetPreviewClone(context);
            var original = command.Context;
            command.Context = clone;

            try
            {
                // 실제 실행과 동일한 세션 depth 수명(EnterCommand/ExitCommandAsync) 및
                // EditAsync/AfterCommands/drain 분기를 그대로 타도록 RunInternal을 재사용한다.
                // afterMode는 CreatePreviewSession에서 session.PreviewAfterMode로 반영된다.
                bool valid = await RunInternal(clone, command, session, preview: true);
                return (valid, clone);
            }
            finally
            {
                command.Context = original;
            }
        }

        private static CommandSession CreatePreviewSession(T context, PreviewAfterMode afterMode)
        {
            if (context is ICommandSessionCarrier carrier && carrier.CommandSession != null && carrier.CommandSession.IsPreview)
                return carrier.CommandSession;

            return new CommandSession(isPreview: true, previewAfterMode: afterMode);
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

        private async UniTask RunFrontEndAsync(T context)
        {
            if (_beforeFrontEndAsyncEvent != null)
                await InvokeSequentialAsync(_beforeFrontEndAsyncEvent, context);

            InvokeSequential(_beforeFrontEndEvent, context);

            if (_frontEndAsyncEvent != null)
                await InvokeSequentialAsync(_frontEndAsyncEvent, context);

            InvokeSequential(_frontEndEvent, context);
        }

        private void CollectAfterCommands(T context, CommandSession session)
        {
            if (_afterCommandsEvent == null)
                return;

            foreach (AfterCommandHandler handler in _afterCommandsEvent.GetInvocationList())
            {
                var commands = handler(context, session);
                if (commands == null)
                    continue;

                foreach (var next in commands)
                    session.EnqueueAfter(next);
            }
        }

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
