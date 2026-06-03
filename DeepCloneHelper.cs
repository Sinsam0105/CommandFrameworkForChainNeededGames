using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Sinsam.CommandFramework
{
    /// <summary>
    /// 어트리뷰트 기반 자동 깊은 복사 유틸리티.
    ///
    /// 복사 정책:
    ///   - primitive / enum / string / decimal      : 그대로 복사(불변)
    ///   - struct(값 타입)                            : 박싱 복사 후, 내부 참조 필드만 깊은 복사
    ///   - UnityEngine.Object(Mono/SO/GameObject 등)  : 참조 유지(절대 복제하지 않음)
    ///   - delegate                                   : 참조 유지
    ///   - [CloneReference] 필드                       : 참조 유지
    ///   - [CloneIgnore]  필드                         : 건너뜀(기본값)
    ///   - [SelfClone]   필드                         : IDeepCloneable.DeepClone() 직접 호출
    ///   - IList<T>                                   : 원소별 복사
    ///   - IDictionary<TKey,TValue>                   : key/value별 복사
    ///   - ISet<T> / HashSet<T>                       : 원소별 복사
    ///   - 그 외 class                                 : 생성자 우회 후 필드 재귀 복사
    ///
    /// 순환 참조와 공유 참조는 visited 맵으로 보존한다.
    /// markPreview=true면 복제된 모든 IPreviewable의 IsPreview를 true로 설정한다.
    /// CommandSession이 제공되면 복제된 모든 ICommandSessionCarrier에 같은 session을 주입한다.
    /// 일반 class 복제는 FormatterServices.GetUninitializedObject로 생성자를 우회한다.
    /// Unity IL2CPP/AOT 환경에서는 이 경로의 호환성을 프로젝트 단위로 검증해야 한다.
    /// </summary>
    public static class DeepCloneHelper
    {
        private const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static T AutoClone<T>(T source, bool markPreview = false)
        {
            return (T)CloneObject(source, NewRegistry(), markPreview, null);
        }

        /// <summary>
        /// 외부 registry(real→clone 맵)를 공유해 복제한다.
        /// 같은 real 객체는 항상 같은 clone으로 매핑되므로, 여러 번에 걸쳐 복제해도 그래프 일관성이 유지된다.
        /// </summary>
        public static T AutoClone<T>(T source, bool markPreview, IDictionary<object, object> registry)
        {
            return (T)CloneObject(source, registry, markPreview, null);
        }

        /// <summary>
        /// 외부 registry와 CommandSession을 공유해 복제한다.
        /// CommandSession은 복제된 ICommandSessionCarrier에 자동 주입된다.
        /// </summary>
        public static T AutoClone<T>(T source, bool markPreview, IDictionary<object, object> registry, CommandSession session)
        {
            return (T)CloneObject(source, registry, markPreview, session);
        }

        /// <summary>참조 동일성(identity) 기반의 빈 registry 생성.</summary>
        public static IDictionary<object, object> NewRegistry()
            => new Dictionary<object, object>(ReferenceEqualityComparer.Instance);

        private static object CloneObject(object source, IDictionary<object, object> visited, bool markPreview, CommandSession session)
        {
            if (source == null)
                return null;

            Type type = source.GetType();

            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
                return source;

            if (type.IsValueType)
                return CloneStruct(source, type, visited, markPreview, session);

            if (source is UnityEngine.Object || source is Delegate)
                return source;

            if (visited.TryGetValue(source, out var existing))
                return existing;

            if (type.IsArray)
                return CloneArray((Array)source, visited, markPreview, session);

            if (source is IDictionary dictionary && type.IsGenericType)
                return CloneDictionary(dictionary, type, visited, markPreview, session);

            if (IsSetType(type))
                return CloneSet((IEnumerable)source, type, visited, markPreview, session);

            if (source is IList list && type.IsGenericType)
                return CloneList(list, type, visited, markPreview, session);

            object clone = FormatterServices.GetUninitializedObject(type);
            visited[source] = clone;
            CloneFields(source, clone, type, visited, markPreview, session);
            MarkPreview(clone, markPreview, session);
            return clone;
        }

        private static Array CloneArray(Array source, IDictionary<object, object> visited, bool markPreview, CommandSession session)
        {
            var clonedArray = Array.CreateInstance(source.GetType().GetElementType(), source.Length);
            visited[source] = clonedArray;
            for (int i = 0; i < source.Length; i++)
                clonedArray.SetValue(CloneObject(source.GetValue(i), visited, markPreview, session), i);
            return clonedArray;
        }

        private static object CloneList(IList source, Type type, IDictionary<object, object> visited, bool markPreview, CommandSession session)
        {
            var clone = (IList)CreateCollectionInstance(type);
            visited[source] = clone;
            foreach (var item in source)
                clone.Add(CloneObject(item, visited, markPreview, session));
            MarkPreview(clone, markPreview, session);
            return clone;
        }

        private static object CloneDictionary(IDictionary source, Type type, IDictionary<object, object> visited, bool markPreview, CommandSession session)
        {
            var clone = (IDictionary)CreateCollectionInstance(type);
            visited[source] = clone;
            foreach (DictionaryEntry entry in source)
            {
                object keyClone = CloneObject(entry.Key, visited, markPreview, session);
                object valueClone = CloneObject(entry.Value, visited, markPreview, session);
                clone.Add(keyClone, valueClone);
            }
            MarkPreview(clone, markPreview, session);
            return clone;
        }

        private static object CloneSet(IEnumerable source, Type type, IDictionary<object, object> visited, bool markPreview, CommandSession session)
        {
            var clone = CreateCollectionInstance(type);
            visited[source] = clone;

            MethodInfo addMethod = clone.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
            if (addMethod == null)
                throw new InvalidOperationException($"Set type '{clone.GetType().FullName}' does not expose a public Add method.");

            foreach (var item in source)
                addMethod.Invoke(clone, new[] { CloneObject(item, visited, markPreview, session) });

            MarkPreview(clone, markPreview, session);
            return clone;
        }

        private static object CreateCollectionInstance(Type type)
        {
            if (!type.IsInterface && !type.IsAbstract)
                return Activator.CreateInstance(type);

            if (IsGenericDictionaryType(type))
            {
                Type[] args = type.GetGenericArguments();
                return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(args));
            }

            if (IsGenericSetType(type))
            {
                Type[] args = type.GetGenericArguments();
                return Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(args));
            }

            if (IsGenericListType(type))
            {
                Type[] args = type.GetGenericArguments();
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(args));
            }

            throw new InvalidOperationException($"Collection type '{type.FullName}' cannot be cloned automatically.");
        }

        private static object CloneStruct(object source, Type type, IDictionary<object, object> visited, bool markPreview, CommandSession session)
        {
            object boxed = source;
            foreach (var field in type.GetFields(Flags))
            {
                if (field.IsDefined(typeof(CloneIgnoreAttribute), true))
                    continue;

                Type ft = field.FieldType;
                if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string) || ft == typeof(decimal))
                    continue;

                object value = field.GetValue(boxed);
                if (value == null)
                    continue;

                if (field.IsDefined(typeof(CloneReferenceAttribute), true) ||
                    value is UnityEngine.Object || value is Delegate)
                    continue;

                field.SetValue(boxed, CloneFieldValue(field, value, visited, markPreview, session));
            }
            MarkPreview(boxed, markPreview, session);
            return boxed;
        }

        private static void CloneFields(object source, object clone, Type type, IDictionary<object, object> visited, bool markPreview, CommandSession session)
        {
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var field in t.GetFields(Flags | BindingFlags.DeclaredOnly))
                {
                    if (field.IsDefined(typeof(CloneIgnoreAttribute), true))
                        continue;

                    object value = field.GetValue(source);
                    if (value == null)
                    {
                        field.SetValue(clone, null);
                        continue;
                    }

                    if (field.IsDefined(typeof(CloneReferenceAttribute), true) ||
                        value is UnityEngine.Object || value is Delegate)
                    {
                        field.SetValue(clone, value);
                        continue;
                    }

                    field.SetValue(clone, CloneFieldValue(field, value, visited, markPreview, session));
                }
            }
        }

        private static object CloneFieldValue(FieldInfo field, object value, IDictionary<object, object> visited, bool markPreview, CommandSession session)
        {
            if (field.IsDefined(typeof(SelfCloneAttribute), true))
                return CloneBySelfClone(value, visited, markPreview, session);

            return CloneObject(value, visited, markPreview, session);
        }

        private static object CloneBySelfClone(object source, IDictionary<object, object> visited, bool markPreview, CommandSession session)
        {
            if (source == null)
                return null;

            if (visited.TryGetValue(source, out var existing))
                return existing;

            if (!(source is IDeepCloneable cloneable))
                throw new InvalidOperationException(
                    $"[SelfClone] field value '{source.GetType().FullName}' must implement IDeepCloneable.");

            object clone = cloneable.DeepClone();
            if (clone == null)
                return null;

            visited[source] = clone;
            MarkPreview(clone, markPreview, session);
            return clone;
        }

        private static void MarkPreview(object instance, bool markPreview, CommandSession session)
        {
            if (markPreview && instance is IPreviewable previewable)
                previewable.IsPreview = true;

            if (session != null && instance is ICommandSessionCarrier carrier)
                carrier.CommandSession = session;
        }

        private static bool IsSetType(Type type)
        {
            return IsGenericSetType(type) || ImplementsGenericDefinition(type, typeof(ISet<>));
        }

        private static bool IsGenericDictionaryType(Type type)
        {
            return type.IsGenericType &&
                   (type.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                    type.GetGenericTypeDefinition() == typeof(Dictionary<,>));
        }

        private static bool IsGenericSetType(Type type)
        {
            return type.IsGenericType &&
                   (type.GetGenericTypeDefinition() == typeof(ISet<>) ||
                    type.GetGenericTypeDefinition() == typeof(HashSet<>));
        }

        private static bool IsGenericListType(Type type)
        {
            return type.IsGenericType &&
                   (type.GetGenericTypeDefinition() == typeof(IList<>) ||
                    type.GetGenericTypeDefinition() == typeof(List<>));
        }

        private static bool ImplementsGenericDefinition(Type type, Type genericDefinition)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == genericDefinition)
                return true;

            foreach (var interfaceType in type.GetInterfaces())
            {
                if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == genericDefinition)
                    return true;
            }

            return false;
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            bool IEqualityComparer<object>.Equals(object x, object y) => ReferenceEquals(x, y);
            int IEqualityComparer<object>.GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
