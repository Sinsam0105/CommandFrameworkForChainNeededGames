namespace Sinsam.CommandFramework
{
    /// <summary>
    /// Base interface for command contexts.
    /// ResetContext/SetContext default to walking all nested IEffectableValue fields.
    /// </summary>
    public interface ICommandContext
    {
        /// <summary>
        /// Dependency-injection hook. The pipeline calls this once at the very start of
        /// every entry path (Execute/Run, NumPreview, NumPreviewAsync, RunValidationOnly),
        /// before any handler runs, so all handlers see a fully provisioned context.
        /// Override in a project base context to wire in shared services.
        /// Must be idempotent: it can be invoked more than once per command.
        /// </summary>
        void PrepareContext() { }

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
