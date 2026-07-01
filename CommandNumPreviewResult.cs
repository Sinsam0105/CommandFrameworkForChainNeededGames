using System;
using System.Collections.Generic;

namespace Sinsam.CommandFramework
{
    public sealed class CommandNumPreviewResult
    {
        private static readonly IReadOnlyDictionary<IEffectableValue, object> EmptyValues =
            new Dictionary<IEffectableValue, object>();

        public static CommandNumPreviewResult Invalid { get; } =
            new CommandNumPreviewResult(false, EmptyValues);

        public bool IsValid { get; }
        public IReadOnlyDictionary<IEffectableValue, object> Values { get; }

        public CommandNumPreviewResult(bool isValid, IReadOnlyDictionary<IEffectableValue, object> values)
        {
            IsValid = isValid;
            Values = values ?? EmptyValues;
        }

        public static CommandNumPreviewResult FromContext(bool isValid, object context)
        {
            if (context == null)
                return new CommandNumPreviewResult(isValid, EmptyValues);

            var values = new Dictionary<IEffectableValue, object>();
            RuntimeDataReflection.ForEachEffectable(context, value =>
            {
                values[value] = value.BoxedFinalValue;
            });

            return new CommandNumPreviewResult(isValid, values);
        }

        public bool TryGet<TValue>(IEffectableValue value, out TValue result)
        {
            if (value != null && Values.TryGetValue(value, out var raw))
            {
                if (raw is TValue typed)
                {
                    result = typed;
                    return true;
                }

                try
                {
                    result = (TValue)Convert.ChangeType(raw, typeof(TValue));
                    return true;
                }
                catch
                {
                }
            }

            result = default;
            return false;
        }
    }

    internal sealed class ModifierIdSnapshot
    {
        private readonly List<Entry> _entries = new();

        private ModifierIdSnapshot()
        {
        }

        public static ModifierIdSnapshot Capture(object context)
        {
            var snapshot = new ModifierIdSnapshot();

            RuntimeDataReflection.ForEachEffectable(context, value =>
            {
                var ids = new HashSet<int>();
                value.CollectModifierIds(ids);
                snapshot._entries.Add(new Entry(value, ids));
            });

            return snapshot;
        }

        public void CleanUp()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                entry.Value.RemoveModifiersExcept(entry.PreservedIds);
            }
        }

        private readonly struct Entry
        {
            public readonly IEffectableValue Value;
            public readonly HashSet<int> PreservedIds;

            public Entry(IEffectableValue value, HashSet<int> preservedIds)
            {
                Value = value;
                PreservedIds = preservedIds;
            }
        }
    }
}
