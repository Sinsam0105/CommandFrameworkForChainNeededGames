using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// 이 필드/프로퍼티는 커맨드 실행 시 null이면 안 된다는 표시.
/// 파이프라인이 Edit 직후 자동으로 검사하여, 하나라도 null이면 커맨드를 무효 처리한다.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = true)]
public sealed class NullCheckAttribute : Attribute { }

/// <summary>
/// RuntimeData / Context 그래프를 리플렉션으로 순회하는 유틸리티.
/// - ForEachEffectable: 보유한 모든 IEffectableValue에 동작 적용(Reset/Set 자동화).
/// - GetNullCheckViolations: [NullCheck] 필드 중 null인 것들의 이름 반환.
/// 타입별 FieldInfo는 캐싱한다.
/// </summary>
public static class RuntimeDataReflection
{
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();
    private static readonly Dictionary<Type, FieldInfo[]> NullCheckCache = new Dictionary<Type, FieldInfo[]>();

    // ───────────────────────── 자동 Reset/Set ─────────────────────────

    /// <summary>root이 보유한(중첩 IRuntimeData·리스트·struct 포함) 모든 IEffectableValue에 action 적용.</summary>
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
            case IEffectableValue e: action(e); return;       // 리프
            case UnityEngine.Object: return;                   // Mono/SO/GameObject
            case Delegate: return;
            case string: return;
            case IRuntimeData rd: WalkContainer(rd, action, visited); return;
            case IList list:
                foreach (var item in list) WalkValue(item, action, visited);
                return;
        }

        Type t = v.GetType();
        if (t.IsPrimitive || t.IsEnum) return;
        if (t.IsValueType) // struct(예: ValueTuple) 내부 필드 검사
        {
            foreach (var f in GetFields(t)) WalkValue(f.GetValue(v), action, visited);
            return;
        }
        // 그 외 임의 class는 재귀하지 않음(데이터 그래프 폭주 방지).
    }

    // ───────────────────────── 자동 NullCheck ─────────────────────────

    /// <summary>[NullCheck]가 붙은 필드 중 null인 필드명을 반환. 위반이 없으면 빈 리스트.</summary>
    public static List<string> GetNullCheckViolations(object target)
    {
        var result = new List<string>();
        if (target == null) return result;

        foreach (var f in GetNullCheckFields(target.GetType()))
        {
            object value = f.GetValue(target);
            // UnityEngine.Object는 파괴된 경우 == null 오버로드를 타도록 비교
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

    // ───────────────────────── 필드 캐시 ─────────────────────────

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
