using Cysharp.Threading.Tasks;

namespace Sinsam.CommandFramework
{
    /// <summary>
    /// Type-safe command base.
    /// Execute runs the full command pipeline.
    /// Preview/PreviewAsync only calculate context-local preview values and never run Logic.
    /// </summary>
    public abstract class Command<T> where T : class, ICommandContext
    {
        public T Context;

        /// <summary>
        /// Command-owned validation. CommandEvent.Run calls this after external ValidationEvent handlers.
        /// </summary>
        public virtual bool ValidateInCommand()
        {
            return Context != null;
        }

        /// <summary>
        /// Actual business logic. Keep it synchronous; use EditAsyncEvent for async input/preparation.
        /// </summary>
        public abstract bool Logic();

        public UniTask<bool> Execute()
        {
            var commandEvent = CommandEventRegistry.GetOrCreate<T>(GetType());
            return commandEvent.Run(Context, this);
        }

        /// <summary>
        /// Runs Edit -> Validation -> ValidateInCommand, captures [PreviewValue] fields, then resets context.
        /// EditAsync, Logic, FrontEnd, and AfterEvent are skipped.
        /// </summary>
        public CommandPreviewResult Preview()
        {
            var commandEvent = CommandEventRegistry.GetOrCreate<T>(GetType());
            return commandEvent.PreviewRun(Context, this);
        }

        /// <summary>
        /// Runs EditAsync -> Edit -> Validation -> ValidateInCommand, captures [PreviewValue] fields, then resets context.
        /// Logic, FrontEnd, and AfterEvent are skipped.
        /// </summary>
        public UniTask<CommandPreviewResult> PreviewAsync()
        {
            var commandEvent = CommandEventRegistry.GetOrCreate<T>(GetType());
            return commandEvent.PreviewAsyncRun(Context, this);
        }
    }
}
