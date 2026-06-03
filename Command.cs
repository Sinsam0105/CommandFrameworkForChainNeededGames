using Cysharp.Threading.Tasks;

namespace Sinsam.CommandFramework
{
    /// <summary>
    /// 커맨드 패턴 베이스.
    /// - 제네릭 단일 계층으로 Context를 타입 안전하게 노출한다.
    /// - CommandSession을 통해 nested command, preview clone, after command chain을 같은 실행 수명으로 묶는다.
    /// </summary>
    public abstract class Command<T> : ICommand where T : class, ICommandContext
    {
        public T Context { get; set; }

        /// <summary>
        /// 커맨드 자체의 유효성 검증.
        /// 외부 ValidationEvent 통과 후 호출된다.
        /// 기본 구현은 Context null 체크만 수행.
        /// </summary>
        public virtual bool ValidateInCommand()
        {
            return Context != null;
        }

        /// <summary>
        /// 실제 비즈니스 로직. 서브클래스에서 구현.
        /// Preview 안정성을 위해 Logic은 Context/RuntimeData graph 중심으로만 상태를 변경해야 한다.
        /// UnityEngine.Object, singleton, static state에 대한 직접 side effect는 금지한다.
        /// </summary>
        public abstract bool Logic();

        /// <summary>
        /// 커맨드 실행 진입점.
        /// Context에 이미 CommandSession이 있으면 해당 session에 합류한다.
        /// Context가 Preview 사본이면 preview session으로 실행된다.
        /// </summary>
        public UniTask<bool> Execute()
        {
            var session = CommandSession.Resolve(Context, Context != null && Context.IsPreview);
            return Execute(session);
        }

        /// <summary>
        /// 외부에서 제공된 CommandSession 안에서 실행한다.
        /// AfterCommand queue drain과 SessionEnded 발행은 최상위 command 종료 시 session이 관리한다.
        /// </summary>
        public UniTask<bool> Execute(CommandSession session)
        {
            var commandEvent = CommandEventRegistry.GetOrCreate<T>(GetType());
            return commandEvent.Run(Context, this, session);
        }

        /// <summary>
        /// 실제 데이터를 깊은 복사한 Preview 사본에 Logic까지의 파이프라인을 적용해 최종 상태를 미리 본다.
        /// 실제 Context/엔티티는 변경되지 않는다.
        /// </summary>
        public (bool IsValid, T Context) Preview()
        {
            var commandEvent = CommandEventRegistry.GetOrCreate<T>(GetType());
            return commandEvent.PreviewRun(Context, this);
        }

        /// <summary>
        /// EditAsync까지 포함해 preview 파이프라인을 실행한다.
        /// FrontEnd/After event는 실행하지 않는다.
        /// afterMode에 따라 AfterCommand를 수집하거나 preview session 위에서 시뮬레이션할 수 있다.
        /// </summary>
        public UniTask<(bool IsValid, T Context)> AsyncPreview(PreviewAfterMode afterMode = PreviewAfterMode.None)
        {
            var commandEvent = CommandEventRegistry.GetOrCreate<T>(GetType());
            return commandEvent.AsyncPreviewRun(Context, this, afterMode);
        }
    }
}
