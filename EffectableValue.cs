using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 커맨드 파이프라인에서 EditEvent로 보정 가능한 값의 인터페이스.
/// Reset: 1커맨드 수명(OneCommand) 보정 제거 (커맨드 1회 실행 후)
/// Set:   1커맨드 보정을 영구(Permanent)로 승격
/// </summary>
public interface IEffectableValue
{
    void Reset();
    void Set();
}

public enum ModifierOp
{
    Additive,        // BaseValue에 더한다
    Multiplicative   // 최종에 곱한다 (1.1 = +10%)
}

public enum ModifierLifetime
{
    Permanent,    // HardReset/RemoveFrom 전까지 유지 (유물 등)
    OneCommand    // 커맨드 1회 동안만 유효 (Reset에서 제거)
}

/// <summary>
/// 값 보정 1건. Source로 출처(유물/효과)를 식별해 장착/해제를 깔끔히 처리한다.
/// 리스트로 보관하므로 깊은 복사가 자명하고, 순서/스택을 명시적으로 다룰 수 있다.
/// </summary>
[Serializable]
public struct ValueModifier
{
    public ModifierOp Op;
    public double Value;
    /// <summary>추가 주체(유물/효과 등). 제거·식별용. DeepClone 시 참조를 그대로 유지한다.</summary>
    [CloneReference] public object Source;
    public ModifierLifetime Lifetime;

    public static ValueModifier Add(double value, object source = null, ModifierLifetime life = ModifierLifetime.Permanent)
        => new ValueModifier { Op = ModifierOp.Additive, Value = value, Source = source, Lifetime = life };

    public static ValueModifier Mul(double multiplier, object source = null, ModifierLifetime life = ModifierLifetime.Permanent)
        => new ValueModifier { Op = ModifierOp.Multiplicative, Value = multiplier, Source = source, Lifetime = life };
}

/// <summary>
/// 기본값 + Modifier 리스트 구조의 수치.
/// FinalValue = (BaseValue + Σ Additive) × Π Multiplicative
///
/// 기존 in-place 보정/Temp 버퍼 대신 Modifier 리스트를 사용한다:
/// - 깊은 복사가 자명 → 클론 기반 Preview와 자연스럽게 맞물린다.
/// - 유물 등은 장착 시 AddMultiplier(..., source), 해제 시 RemoveFrom(source)로 관리.
/// - "1커맨드 한정" 버프는 OneCommand 수명으로 추가하면 ResetContext에서 자동 제거된다.
/// </summary>
[Serializable]
public abstract class EffectableValue<T> : IEffectableValue where T : struct, IConvertible
{
    public T BaseValue;

    private List<ValueModifier> _modifiers = new List<ValueModifier>();
    public IReadOnlyList<ValueModifier> Modifiers => _modifiers;

    public virtual T FinalValue
    {
        get
        {
            double add = 0.0;
            double mul = 1.0;
            for (int i = 0; i < _modifiers.Count; i++)
            {
                var m = _modifiers[i];
                if (m.Op == ModifierOp.Additive) add += m.Value;
                else mul *= m.Value;
            }
            return FromDouble((ToDouble(BaseValue) + add) * mul);
        }
    }

    // ───────── 추가/제거 ─────────
    public void Add(ValueModifier modifier) => _modifiers.Add(modifier);

    public void AddAdditive(double value, object source = null, ModifierLifetime life = ModifierLifetime.Permanent)
        => _modifiers.Add(ValueModifier.Add(value, source, life));

    public void AddMultiplier(double multiplier, object source = null, ModifierLifetime life = ModifierLifetime.Permanent)
        => _modifiers.Add(ValueModifier.Mul(multiplier, source, life));

    /// <summary>특정 출처가 추가한 모든 보정 제거 (유물 해제 등).</summary>
    public void RemoveFrom(object source) => _modifiers.RemoveAll(m => Equals(m.Source, source));

    // ───────── 수명 처리 ─────────
    /// <summary>커맨드 1회 종료 시: OneCommand 수명 보정만 제거.</summary>
    public virtual void Reset() => _modifiers.RemoveAll(m => m.Lifetime == ModifierLifetime.OneCommand);

    /// <summary>OneCommand 보정을 Permanent로 승격(임시 보정을 영구 반영).</summary>
    public virtual void Set()
    {
        for (int i = 0; i < _modifiers.Count; i++)
        {
            var m = _modifiers[i];
            if (m.Lifetime == ModifierLifetime.OneCommand)
            {
                m.Lifetime = ModifierLifetime.Permanent;
                _modifiers[i] = m;
            }
        }
    }

    /// <summary>모든 보정 제거.</summary>
    public virtual void HardReset() => _modifiers.Clear();

    /// <summary>현재 FinalValue를 BaseValue로 확정하고 모든 보정 제거.</summary>
    public virtual void HardSet()
    {
        BaseValue = FinalValue;
        _modifiers.Clear();
    }

    protected double ToDouble(T value) => Convert.ToDouble(value);
    protected T FromDouble(double value) => (T)Convert.ChangeType(value, typeof(T));
}

[Serializable] public class EffectableInt : EffectableValue<int> { }
[Serializable] public class EffectableFloat : EffectableValue<float> { }

/// <summary>
/// 값을 덮어쓰는(치환) 구조의 수치. EffectableValue와 용도가 달라 그대로 유지한다.
/// </summary>
[Serializable]
public abstract class ReplaceableValue<T> : IEffectableValue
{
    public T BaseValue;
    public bool IsAltered;
    public T AlteredValue;

    public virtual T FinalValue => IsAltered ? AlteredValue : BaseValue;

    public void Replace(T value)
    {
        AlteredValue = value;
        IsAltered = true;
    }

    public virtual void Reset()
    {
        AlteredValue = default;
        IsAltered = false;
    }

    public virtual void Set()
    {
        BaseValue = FinalValue;
        Reset();
    }
}

[Serializable] public class ReplaceableBool : ReplaceableValue<bool> { }
[Serializable] public class ReplaceableInt : ReplaceableValue<int> { }
[Serializable] public class ReplaceableFloat : ReplaceableValue<float> { }
