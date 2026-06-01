using System.Collections.Generic;

namespace Sinsam.CommandFramework
{
    public sealed class PreviewSnapshot
    {
        private readonly IDictionary<object, object> _registry = DeepCloneHelper.NewRegistry();

        public T GetClone<T>(T real) where T : class
        {
            if (real == null)
            {
                return null;
            }

            return DeepCloneHelper.AutoClone(real, markPreview: true, _registry);
        }
    }

    public static class PreviewAware
    {
        public static T Data<T>(T real) where T : class
        {
            var snapshot = CommandPreviewScope.Snapshot;
            return CommandPreviewScope.IsActive && snapshot != null ? snapshot.GetClone(real) : real;
        }
    }
}
