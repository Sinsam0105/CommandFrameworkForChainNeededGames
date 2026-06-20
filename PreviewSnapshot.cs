using System;
using System.Collections.Generic;
using System.Reflection;

namespace Sinsam.CommandFramework
{
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class PreviewValueAttribute : Attribute
    {
    }

    public sealed class CommandPreviewResult
    {
        private static readonly IReadOnlyDictionary<string, object> EmptyValues =
            new Dictionary<string, object>();

        public static CommandPreviewResult Invalid { get; } =
            new CommandPreviewResult(false, EmptyValues);

        public bool IsValid { get; }
        public IReadOnlyDictionary<string, object> Values { get; }

        public CommandPreviewResult(bool isValid, IReadOnlyDictionary<string, object> values)
        {
            IsValid = isValid;
            Values = values ?? EmptyValues;
        }

        public static CommandPreviewResult FromContext(bool isValid, object context)
        {
            return new CommandPreviewResult(isValid, Capture(context));
        }

        public bool TryGet<T>(string name, out T value)
        {
            if (Values.TryGetValue(name, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }

            value = default;
            return false;
        }

        public T Get<T>(string name)
        {
            if (!Values.TryGetValue(name, out var value))
                throw new KeyNotFoundException($"Preview value '{name}' was not found.");

            return (T)value;
        }

        private static IReadOnlyDictionary<string, object> Capture(object context)
        {
            if (context == null)
                return EmptyValues;

            var result = new Dictionary<string, object>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var type = context.GetType();
            while (type != null && type != typeof(object))
            {
                foreach (var field in type.GetFields(flags))
                {
                    if (!Attribute.IsDefined(field, typeof(PreviewValueAttribute), true))
                        continue;

                    object value = field.GetValue(context);
                    result[field.Name] = value is IEffectableValue effectable
                        ? effectable.BoxedFinalValue
                        : value;
                }

                type = type.BaseType;
            }

            return result;
        }
    }
}
