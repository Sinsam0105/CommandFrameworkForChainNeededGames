using System.Collections.Generic;

namespace Sinsam.CommandFramework
{
    /// <summary>
    /// Captures all IEffectableValue states reachable from a command context.
    /// Used to rollback ResetableEditEvent changes when validation or execution fails.
    /// This is intentionally narrower than full runtime-data transaction rollback.
    /// </summary>
    public sealed class ResetableEditSnapshot
    {
        private readonly List<Entry> _entries = new();

        private ResetableEditSnapshot()
        {
        }

        public static ResetableEditSnapshot Capture(object context)
        {
            var snapshot = new ResetableEditSnapshot();

            RuntimeDataReflection.ForEachEffectable(context, value =>
            {
                snapshot._entries.Add(new Entry(value, value.CreateSnapshot()));
            });

            return snapshot;
        }

        public void Restore()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                entry.Value.RestoreSnapshot(entry.Snapshot);
            }
        }

        private readonly struct Entry
        {
            public readonly IEffectableValue Value;
            public readonly object Snapshot;

            public Entry(IEffectableValue value, object snapshot)
            {
                Value = value;
                Snapshot = snapshot;
            }
        }
    }
}
