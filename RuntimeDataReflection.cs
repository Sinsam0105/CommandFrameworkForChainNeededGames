using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Sinsam.CommandFramework
{
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class NullCheckAttribute : Attribute { }

    public static class RuntimeDataReflection
    {
        private const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();
        private static readonly Dictionary<Type, FieldInfo[]> NullCheckCache = new Dictionary<Type, FieldInfo[]>();

        public static void ForEachEffectable(object root, Action<IEffectableValue> action)
        {
            if (root == null) return;
            WalkContainer(root, action, new HashSet<object>(IdentityComparer.Instance));
        }

        private static void WalkContainer(object obj, Action<IEffectableValue> action, HashSet<object> visited)
        {
            if (obj == null || !visited.Add(obj)) return;
            foreach (var f in GetFields(obj.GetType()))
                WalkValue(f.GetValue(obj), action, visited);
        }

        private static void WalkValue(object v, Action<IEffectableValue> action, HashSet<object> visited)
        {
            switch (v)
            {
                case null: return;
                case IEffectableValue e: action(e); return;
                case UnityEngine.Object: return;
                case Delegate: return;
                case string: return;
                case IRuntimeData rd: WalkContainer(rd, action, visited); return;
                case IList list:
                    foreach (var item in list) WalkValue(item, action, visited);
                    return;
            }

            Type t = v.GetType();
            if (t.IsPrimitive || t.IsEnum) return;
            if (t.IsValueType)
            {
                foreach (var f in GetFields(t)) WalkValue(f.GetValue(v), action, visited);
            }
        }

        public static List<string> GetNullCheckViolations(object target)
        {
            var result = new List<string>();
            if (target == null) return result;

            foreach (var f in GetNullCheckFields(target.GetType()))
            {
                object value = f.GetValue(target);
                bool isNull = value == null ||
                              (value is UnityEngine.Object uo && uo == null);
                if (isNull) result.Add(f.Name);
            }
            return result;
        }

        public static bool HasNullCheckViolation(object target, out string firstViolation)
        {
            var violations = GetNullCheckViolations(target);
            firstViolation = violations.Count > 0 ? violations[0] : null;
            return violations.Count > 0;
        }

        private static FieldInfo[] GetFields(Type type)
        {
            if (FieldCache.TryGetValue(type, out var cached)) return cached;

            var list = new List<FieldInfo>();
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
                list.AddRange(t.GetFields(Flags | BindingFlags.DeclaredOnly));

            var arr = list.ToArray();
            FieldCache[type] = arr;
            return arr;
        }

        private static FieldInfo[] GetNullCheckFields(Type type)
        {
            if (NullCheckCache.TryGetValue(type, out var cached)) return cached;

            var list = new List<FieldInfo>();
            foreach (var f in GetFields(type))
                if (f.IsDefined(typeof(NullCheckAttribute), true))
                    list.Add(f);

            var arr = list.ToArray();
            NullCheckCache[type] = arr;
            return arr;
        }

        private sealed class IdentityComparer : IEqualityComparer<object>
        {
            public static readonly IdentityComparer Instance = new IdentityComparer();
            bool IEqualityComparer<object>.Equals(object x, object y) => ReferenceEquals(x, y);
            int IEqualityComparer<object>.GetHashCode(object obj) =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
