using System;
using System.Collections.Generic;
using System.Reflection;

namespace Sinsam.CommandFramework
{
    /// <summary>
    /// Command execution 중 직접 교체를 허용하지 않을 필드에 부착한다.
    /// 내부 객체의 메서드 호출로 발생하는 mutation은 감지 대상이 아니므로,
    /// 그런 객체는 CommandSession 기반 쓰기 API로 별도 제한해야 한다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class CommandReadOnlyAttribute : Attribute { }

    public sealed class MutationSnapshot
    {
        private static readonly BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly object _target;
        private readonly List<Entry> _entries;

        private MutationSnapshot(object target, List<Entry> entries)
        {
            _target = target;
            _entries = entries;
        }

        public static MutationSnapshot Capture(object target)
        {
            var entries = new List<Entry>();
            if (target == null)
                return new MutationSnapshot(null, entries);

            for (Type t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var field in t.GetFields(Flags | BindingFlags.DeclaredOnly))
                {
                    if (!field.IsDefined(typeof(CommandReadOnlyAttribute), true))
                        continue;

                    entries.Add(new Entry(field, field.GetValue(target)));
                }
            }

            return new MutationSnapshot(target, entries);
        }

        public bool HasViolation(out string fieldName)
        {
            fieldName = null;
            if (_target == null)
                return false;

            foreach (var entry in _entries)
            {
                object current = entry.Field.GetValue(_target);
                if (!SameValue(entry.Value, current))
                {
                    fieldName = entry.Field.Name;
                    return true;
                }
            }

            return false;
        }

        public void ThrowIfViolated(string commandName)
        {
            if (HasViolation(out var fieldName))
            {
                throw new InvalidOperationException(
                    $"[{commandName}] CommandReadOnly violation: '{fieldName}' field was replaced during command execution.");
            }
        }

        private static bool SameValue(object before, object after)
        {
            if (ReferenceEquals(before, after))
                return true;

            if (before == null || after == null)
                return false;

            Type type = before.GetType();
            if (type.IsValueType || before is string)
                return before.Equals(after);

            return false;
        }

        private readonly struct Entry
        {
            public readonly FieldInfo Field;
            public readonly object Value;

            public Entry(FieldInfo field, object value)
            {
                Field = field;
                Value = value;
            }
        }
    }
}
