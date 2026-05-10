using System;
using UnityEngine;

/// <summary>
/// 커맨드 파이프라인에서 EditEvent로 보정 가능한 값의 인터페이스.
/// Reset: 임시 보정값 초기화 (커맨드 1회 실행 후)
/// Set:   임시 보정값을 영구 반영
/// </summary>
public interface IEffectableValue
{
    void Reset();
    void Set();
}

/// <summary>
/// 기본값 + 가산 보정 + 곱연 보정 구조의 수치.
/// TempAdditive/TempMultiplier는 커맨드 1회 실행 동안만 유효하며,
/// CommandEvent.Run의 finally에서 ResetContext → Reset으로 자동 정리된다.
///
/// FinalValue = (BaseValue + Additive + TempAdditive) × (Multiplier × TempMultiplier)
/// </summary>
[Serializable]
public abstract class EffectableValue<T> : IEffectableValue where T : struct, IConvertible
{
    public T BaseValue;
    public float Multiplier;
    public float TempMultiplierForOneCommand;
    public T Additive;
    public T TempAdditiveForOneCommand;

    public virtual T FinalValue =>
        FromDouble(
            (ToDouble(BaseValue) + ToDouble(Additive) + ToDouble(TempAdditiveForOneCommand))
            * (Multiplier * TempMultiplierForOneCommand)
        );

    protected abstract float DefaultMultiplier { get; }
    protected abstract T DefaultAdditive { get; }

    protected double ToDouble(T value) => Convert.ToDouble(value);
    protected T FromDouble(double value) => (T)Convert.ChangeType(value, typeof(T));

    /// <summary>
    /// 모든 보정값을 기본값으로 초기화.
    /// </summary>
    public virtual void HardReset()
    {
        Multiplier = DefaultMultiplier;
        Additive = DefaultAdditive;
        TempAdditiveForOneCommand = DefaultAdditive;
        TempMultiplierForOneCommand = DefaultMultiplier;
    }

    /// <summary>
    /// 현재 FinalValue를 BaseValue로 확정하고 보정값 초기화.
    /// </summary>
    public virtual void HardSet()
    {
        BaseValue = FinalValue;
        HardReset();
    }

    /// <summary>
    /// 임시 보정값만 초기화 (커맨드 1회 실행 후 호출).
    /// </summary>
    public virtual void Reset()
    {
        TempAdditiveForOneCommand = DefaultAdditive;
        TempMultiplierForOneCommand = DefaultMultiplier;
    }

    /// <summary>
    /// 임시 보정값을 영구 보정값에 반영 후 임시값 초기화.
    /// </summary>
    public virtual void Set()
    {
        Multiplier *= TempMultiplierForOneCommand;
        double newAdd = ToDouble(Additive) + ToDouble(TempAdditiveForOneCommand);
        Additive = FromDouble(newAdd);
        Reset();
    }
}

[Serializable]
public class EffectableInt : EffectableValue<int>
{
    public EffectableInt() => HardReset();
    protected override float DefaultMultiplier => 1.0f;
    protected override int DefaultAdditive => 0;
}

[Serializable]
public class EffectableFloat : EffectableValue<float>
{
    public EffectableFloat() => HardReset();
    protected override float DefaultMultiplier => 1f;
    protected override float DefaultAdditive => 0f;
}

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
