namespace Sinsam.CommandFramework
{
    /// <summary>
    /// RuntimeData / entity data marker interface used by command preview and reflection helpers.
    /// </summary>
    public interface IRuntimeData : IPreviewable, IDeepCloneable
    {
    }
}
