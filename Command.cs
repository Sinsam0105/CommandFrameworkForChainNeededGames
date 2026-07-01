using Cysharp.Threading.Tasks;

namespace Sinsam.CommandFramework
{
    /// <summary>
    /// Type-safe command base.
    /// Execute runs the full command pipeline.
    /// NumPreview is limited to temporary numeric changes and validation.
    /// Project-specific preview presentation should be implemented outside the package.
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
    }
}
