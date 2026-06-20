using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Sinsam.CommandFramework
{
    public static class RuntimeDataReflection
    {
        private const BindingFlags FieldFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void ForEachEffectable(object root, Action<IEffectableValue> action)
        {
            if (root == null || action == null)
                return;

            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            Walk(root, action, visited);
        }

        private static void Walk(object value, Action<IEffectableValue> action, HashSet<object> visited)
        {
            if (value == null)
                return;

            if (value is IEffectableValue effectable)
            {
                action(effectable);
                return;
            }

            var type = value.GetType();

            if (ShouldStop(type, value))
                return;

            if (!type.IsValueType && !visited.Add(value))
                return;

            if (value is IEnumerable enumerable && !(value is string))
            {
                foreach (var item in enumerable)
                    Walk(item, action, visited);
                return;
            }

            while (type != null && type != typeof(object))
            {
                foreach (var field in type.GetFields(FieldFlags))
                {
                    if (field.IsStatic)
                        continue;

                    var fieldValue = field.GetValue(value);
                    Walk(fieldValue, action, visited);
                }

                type = type.BaseType;
            }
        }

        private static bool ShouldStop(Type type, object value)
        {
            if (type.IsPrimitive || type.IsEnum)
                return true;

            if (value is string || value is decimal || value is Delegate)
                return true;

            if (value is UnityEngine.Object)
                return true;

            return false;
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
