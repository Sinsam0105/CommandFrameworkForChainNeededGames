using Cysharp.Threading.Tasks;

namespace Sinsam.CommandFramework
{
    public abstract class Command<T> where T : class, ICommandContext
    {
        public T Context { get; set; }

        public virtual bool ValidateInCommand()
        {
            return Context != null;
        }

        public abstract bool Logic();

        public UniTask<bool> Execute()
        {
            var commandEvent = CommandEventRegistry.GetOrCreate<T>(GetType());
            if (Context != null && Context.IsPreview)
            {
                return commandEvent.RunInternal(Context, this, preview: true);
            }

            return commandEvent.Run(Context, this);
        }

        public (bool IsValid, T Context) Preview()
        {
            var commandEvent = CommandEventRegistry.GetOrCreate<T>(GetType());
            return commandEvent.PreviewRun(Context, this);
        }
    }
}
