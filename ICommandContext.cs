namespace Sinsam.CommandFramework
{
    /// <summary>
    /// Base interface for command contexts.
    /// ResetContext/SetContext default to walking all nested IEffectableValue fields.
    /// </summary>
    public interface ICommandContext
    {
        void ResetContext()
        {
            RuntimeDataReflection.ForEachEffectable(this, e => e.Reset());
        }

        void SetContext()
        {
            RuntimeDataReflection.ForEachEffectable(this, e => e.Set());
        }
    }
}
