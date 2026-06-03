using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Sinsam.CommandFramework
{
    /// <summary>
    /// Preview에서 AfterCommand를 어떻게 다룰지 지정한다.
    /// None: AfterCommand를 수집하지 않는다.
    /// CollectCommands: AfterCommand를 session queue에 넣기만 하고 실행하지 않는다.
    /// SimulateCommands: AfterCommand를 preview clone graph 위에서 끝까지 실행한다.
    /// </summary>
    public enum PreviewAfterMode
    {
        None,
        CollectCommands,
        SimulateCommands
    }

    /// <summary>
    /// CommandContext나 RuntimeData가 자신이 속한 CommandSession을 들고 있을 때 구현한다.
    /// Context에 이미 session이 있으면 새 session을 만들지 않고 기존 chain에 합류한다.
    /// </summary>
    public interface ICommandSessionCarrier
    {
        CommandSession CommandSession { get; set; }
    }

    /// <summary>
    /// Command<T>를 비제네릭 queue에 넣기 위한 최소 실행 인터페이스.
    /// </summary>
    public interface ICommand
    {
        UniTask<bool> Execute();
        UniTask<bool> Execute(CommandSession session);
    }

    public delegate void SessionEndedEventHandler(CommandSession session);
    public delegate UniTask SessionEndedAsyncEventHandler(CommandSession session);

    /// <summary>
    /// 하나의 command chain 실행 수명 단위.
    /// - Preview clone registry
    /// - nested command session 전파
    /// - after command queue
    /// - session ended lifecycle
    /// - field mutation guard 진입점
    /// 을 통합해서 관리한다.
    /// </summary>
    public sealed class CommandSession
    {
        private readonly IDictionary<object, object> _previewRegistry = DeepCloneHelper.NewRegistry();
        private readonly Queue<ICommand> _afterCommands = new Queue<ICommand>();

        private int _depth;
        private bool _isDraining;

        public bool IsPreview { get; }
        public bool IsEnded { get; private set; }
        public int Depth => _depth;
        public int QueuedCommandCount => _afterCommands.Count;
        public bool HasQueuedCommands => _afterCommands.Count > 0;
        public PreviewAfterMode PreviewAfterMode { get; }
        public CommandMutationGuard MutationGuard { get; } = new CommandMutationGuard();

        public event SessionEndedEventHandler SessionEndedEvent;
        public event SessionEndedAsyncEventHandler SessionEndedAsyncEvent;

        public CommandSession(bool isPreview = false, PreviewAfterMode previewAfterMode = PreviewAfterMode.None)
        {
            IsPreview = isPreview;
            PreviewAfterMode = previewAfterMode;
        }

        public static CommandSession Resolve(ICommandContext context, bool isPreview = false, PreviewAfterMode previewAfterMode = PreviewAfterMode.None)
        {
            if (context is ICommandSessionCarrier carrier && carrier.CommandSession != null)
                return carrier.CommandSession;

            var session = new CommandSession(isPreview, previewAfterMode);
            if (context is ICommandSessionCarrier newCarrier)
                newCarrier.CommandSession = session;

            return session;
        }

        public T GetPreviewClone<T>(T real) where T : class
        {
            if (real == null)
                return null;

            return DeepCloneHelper.AutoClone(real, markPreview: true, _previewRegistry, this);
        }

        public void EnqueueAfter(ICommand command)
        {
            if (command == null)
                return;

            if (IsEnded)
                throw new InvalidOperationException("Cannot enqueue an after command into an ended CommandSession.");

            _afterCommands.Enqueue(command);
        }

        internal void EnterCommand(ICommandContext context)
        {
            if (IsEnded)
                throw new InvalidOperationException("Cannot execute a command in an ended CommandSession.");

            _depth++;

            if (context is ICommandSessionCarrier carrier)
                carrier.CommandSession = this;
        }

        internal async UniTask ExitCommandAsync(bool drainAfterCommands)
        {
            if (_depth > 0)
                _depth--;

            if (_depth > 0)
                return;

            if (drainAfterCommands)
                await DrainAfterCommandsAsync();

            await EndIfIdleAsync();
        }

        internal async UniTask DrainAfterCommandsAsync()
        {
            if (_isDraining)
                return;

            _isDraining = true;
            try
            {
                while (_depth == 0 && _afterCommands.Count > 0)
                {
                    var next = _afterCommands.Dequeue();
                    await next.Execute(this);
                }
            }
            finally
            {
                _isDraining = false;
            }
        }

        internal async UniTask EndIfIdleAsync()
        {
            if (IsEnded || _depth != 0 || _afterCommands.Count != 0)
                return;

            IsEnded = true;
            SessionEndedEvent?.Invoke(this);

            if (SessionEndedAsyncEvent != null)
            {
                foreach (SessionEndedAsyncEventHandler handler in SessionEndedAsyncEvent.GetInvocationList())
                    await handler(this);
            }
        }
    }

    /// <summary>
    /// [CommandReadOnly] 필드의 직접 교체를 감지하기 위한 lightweight guard.
    /// 내부 객체 mutation까지 완전 차단하려면 해당 객체의 쓰기 API를 CommandSession 기반으로 제한해야 한다.
    /// </summary>
    public sealed class CommandMutationGuard
    {
        public MutationSnapshot Capture(object target)
        {
            return MutationSnapshot.Capture(target);
        }
    }
}
