using Cysharp.Threading.Tasks;

namespace Sinsam.CommandFramework
{
    /// <summary>
    /// 커맨드 패턴 베이스.
    /// - Hemi: 제네릭 단일 계층, Context를 타입 안전하게 노출
    /// - PRC:  ValidateInCommand()로 커맨드 자체 검증 지원
    ///
    /// 사용법:
    ///   var cmd = new AttackCommand { Context = new HealthCommandContext(...) };
    ///   bool success = await cmd.Execute();
    ///
    /// 파이프라인 순서:
    ///   EditAsync → Edit → Validation → ValidateInCommand
    ///   → Logic
    ///   → BeforeFrontEndAsync → BeforeFrontEnd → FrontEndAsync → FrontEnd → AfterAsync → After
    /// </summary>
    public abstract class Command<T> where T : class, ICommandContext
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
        /// 동기 PreviewRun을 유지하기 위해 sync로 작성한다.
        /// 비동기 대기가 필요한 경우 EditAsync / BeforeFrontEndAsync 등 이벤트 레이어 또는 AsyncPreview를 사용할 것.
        /// </summary>
        public abstract bool Logic();

        /// <summary>
        /// 커맨드 실행 진입점.
        /// Context가 이미 Preview 사본(IsPreview=true)이면 commit 없이 preview 경로로 실행된다.
        /// </summary>
        public UniTask<bool> Execute()
        {
            var commandEvent = CommandEventRegistry.GetOrCreate<T>(GetType());
            if (Context != null && Context.IsPreview)
            {
                return commandEvent.RunInternal(Context, this, preview: true);
            }
            return commandEvent.Run(Context, this);
        }

        /// <summary>
        /// 실제 데이터를 깊은 복사한 Preview 사본에 Logic까지의 파이프라인을 적용해 최종 상태를 미리 본다.
        /// 실제 Context/엔티티는 변경되지 않으므로 ResetContext 호출이 필요 없다.
        /// 반환되는 Context는 효과가 적용된 사본(PreviewInstance)이다.
        /// Logic이 sync이므로 Preview는 항상 동기적으로 완료된다.
        /// </summary>
        public (bool IsValid, T Context) Preview()
        {
            var commandEvent = CommandEventRegistry.GetOrCreate<T>(GetType());
            return commandEvent.PreviewRun(Context, this);
        }

        /// <summary>
        /// 실제 데이터를 깊은 복사한 Preview 사본에 async/front-end 이벤트까지 포함해 파이프라인을 적용한다.
        /// runAfterEvents=true면 AfterAsync/After까지 preview context로 발행된다.
        /// </summary>
        public UniTask<(bool IsValid, T Context)> AsyncPreview(bool runAfterEvents = true)
        {
            var commandEvent = CommandEventRegistry.GetOrCreate<T>(GetType());
            return commandEvent.AsyncPreviewRun(Context, this, runAfterEvents);
        }
    }
}