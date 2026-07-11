using System.Collections.Generic;

namespace Sinsam.CommandFramework
{
    /// <summary>
    /// Opts a context out of recursive reflection-based preview discovery.
    /// Only values explicitly returned here participate in numeric preview
    /// snapshots, result capture, and modifier cleanup.
    /// </summary>
    public interface IExplicitPreviewValueProvider
    {
        IEnumerable<IEffectableValue> GetPreviewEffectableValues();
    }
}
