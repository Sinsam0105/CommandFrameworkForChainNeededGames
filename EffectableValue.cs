using System;
using System.Collections.Generic;

namespace Sinsam.CommandFramework
{
    public interface IEffectableValue
    {
        void Reset();
        void Set();

        object BoxedFinalValue { get; }
        Type ValueType { get; }

        object CreateSnapshot();
        void RestoreSnapshot(object snapshot);
    }

    public enum ModifierOp
    {
        Additive,
        Multiplicative
    }

    public enum ModifierLifetime
    {
        Permanent,
        OneCommand
    }

    [Serializable]
    public struct ValueModifier
    {
        public ModifierOp Op;
        public double Value;
        public object Source;
        public ModifierLifetime Lifetime;

        public static ValueModifier Add(double value, object source = null, ModifierLifetime life = ModifierLifetime.Permanent)
            => new ValueModifier { Op = ModifierOp.Additive, Value = value, Source = source, Lifetime = life };

        public static ValueModifier Mul(double multiplier, object source = null, ModifierLifetime life = ModifierLifetime.Permanent)
            => new ValueModifier { Op = ModifierOp.Multiplicative, Value = multiplier, Source = source, Lifetime = life };
    }

    [Serializable]
    public abstract class EffectableValue<T> : IEffectableValue where T : struct, IConvertible
    {
        public T BaseValue;

        private readonly List<ValueModifier> _modifiers = new();
        public IReadOnlyList<ValueModifier> Modifiers => _modifiers;

        public virtual T FinalValue
        {
            get
            {
                double add = 0.0;
                double mul = 1.0;

                for (int i = 0; i < _modifiers.Count; i++)
                {
                    var modifier = _modifiers[i];
                    if (modifier.Op == ModifierOp.Additive)
                        add += modifier.Value;
                    else
                        mul *= modifier.Value;
                }

                return FromDouble((ToDouble(BaseValue) + add) * mul);
            }
        }

        object IEffectableValue.BoxedFinalValue => FinalValue;
        Type IEffectableValue.ValueType => typeof(T);

        public void Add(ValueModifier modifier) => _modifiers.Add(modifier);

        public void AddAdditive(double value, object source = null, ModifierLifetime life = ModifierLifetime.Permanent)
            => _modifiers.Add(ValueModifier.Add(value, source, life));

        public void AddMultiplier(double multiplier, object source = null, ModifierLifetime life = ModifierLifetime.Permanent)
            => _modifiers.Add(ValueModifier.Mul(multiplier, source, life));

        public void RemoveFrom(object source) => _modifiers.RemoveAll(modifier => Equals(modifier.Source, source));

        public virtual void Reset() => _modifiers.RemoveAll(modifier => modifier.Lifetime == ModifierLifetime.OneCommand);

        public virtual void Set()
        {
            for (int i = 0; i < _modifiers.Count; i++)
            {
                var modifier = _modifiers[i];
                if (modifier.Lifetime == ModifierLifetime.OneCommand)
                {
                    modifier.Lifetime = ModifierLifetime.Permanent;
                    _modifiers[i] = modifier;
                }
            }
        }

        public virtual void HardReset() => _modifiers.Clear();

        public virtual void HardSet()
        {
            BaseValue = FinalValue;
            _modifiers.Clear();
        }

        public object CreateSnapshot()
        {
            return new Snapshot
            {
                BaseValue = BaseValue,
                Modifiers = new List<ValueModifier>(_modifiers)
            };
        }

        public void RestoreSnapshot(object snapshot)
        {
            if (snapshot is not Snapshot typedSnapshot)
                throw new ArgumentException($"Invalid snapshot type for {GetType().Name}.", nameof(snapshot));

            BaseValue = typedSnapshot.BaseValue;
            _modifiers.Clear();
            _modifiers.AddRange(typedSnapshot.Modifiers);
        }

        protected double ToDouble(T value) => Convert.ToDouble(value);
        protected T FromDouble(double value) => (T)Convert.ChangeType(value, typeof(T));

        private sealed class Snapshot
        {
            public T BaseValue;
            public List<ValueModifier> Modifiers;
        }
    }

    [Serializable] public class EffectableInt : EffectableValue<int> { }
    [Serializable] public class EffectableFloat : EffectableValue<float> { }

    [Serializable]
    public abstract class ReplaceableValue<T> : IEffectableValue
    {
        public T BaseValue;
        public bool IsAltered;
        public T AlteredValue;

        public virtual T FinalValue => IsAltered ? AlteredValue : BaseValue;

        object IEffectableValue.BoxedFinalValue => FinalValue;
        Type IEffectableValue.ValueType => typeof(T);

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

        public object CreateSnapshot()
        {
            return new Snapshot
            {
                BaseValue = BaseValue,
                IsAltered = IsAltered,
                AlteredValue = AlteredValue
            };
        }

        public void RestoreSnapshot(object snapshot)
        {
            if (snapshot is not Snapshot typedSnapshot)
                throw new ArgumentException($"Invalid snapshot type for {GetType().Name}.", nameof(snapshot));

            BaseValue = typedSnapshot.BaseValue;
            IsAltered = typedSnapshot.IsAltered;
            AlteredValue = typedSnapshot.AlteredValue;
        }

        private sealed class Snapshot
        {
            public T BaseValue;
            public bool IsAltered;
            public T AlteredValue;
        }
    }

    [Serializable] public class ReplaceableBool : ReplaceableValue<bool> { }
    [Serializable] public class ReplaceableInt : ReplaceableValue<int> { }
    [Serializable] public class ReplaceableFloat : ReplaceableValue<float> { }
}
