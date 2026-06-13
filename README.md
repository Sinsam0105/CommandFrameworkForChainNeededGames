# Command Framework

Unity에서 커맨드 패턴을 기반으로 게임 로직을 구조화하기 위한 C# 프레임워크입니다.

이 패키지는 단순히 `Command.Execute()`를 제공하는 수준이 아니라, 커맨드 실행 전후 이벤트, validation, preview simulation, after-command chain, one-command modifier, deep clone 기반 상태 예측까지 함께 다룹니다.

## Features

- Type-safe `Command<TContext>` 기반 커맨드 작성
- `Edit`, `Validation`, `FrontEnd`, `AfterCommands` 이벤트 파이프라인
- `CommandSession`을 통한 nested command / after-command queue 관리
- Preview 실행으로 실제 데이터를 변경하지 않고 결과 예측
- `EffectableValue<T>` 기반 additive / multiplicative modifier 처리
- `OneCommand` modifier의 reset / set 지원
- Attribute 기반 deep clone 정책 제어
- `[NullCheck]`, `[CommandReadOnly]` 기반 런타임 안전장치
- Unity + UniTask 기반 async 이벤트 지원

## Requirements

- Unity 2021.3 이상
- UniTask 2.0.0 이상

패키지명은 다음과 같습니다.

```json
{
  "name": "com.sinsam.command-framework",
  "version": "3.0.0"
}
```

## Installation

Unity Package Manager에서 Git URL로 추가할 수 있습니다.

```text
https://github.com/Sinsam0105/CommandFrameworkForChainNeededGames.git
```

Unity Editor 기준:

1. `Window > Package Manager`
2. `+` 버튼 클릭
3. `Add package from git URL...`
4. 위 Git URL 입력

UniTask가 프로젝트에서 resolve되어야 합니다. 기존 프로젝트에 UniTask가 없다면 OpenUPM, Git URL, 또는 별도 패키지 설치 방식으로 먼저 추가하세요.

## Core Concept

이 프레임워크의 중심 구조는 다음과 같습니다.

```text
Command<TContext>
    |
    v
CommandEvent<TContext>
    |
    |-- EditAsync
    |-- Edit
    |-- Validation
    |-- ValidateInCommand
    |-- Logic
    |-- BeforeFrontEndAsync
    |-- BeforeFrontEnd
    |-- FrontEndAsync
    |-- FrontEnd
    |-- AfterCommands
    |
    v
CommandSession
    |
    |-- after-command queue drain
    |-- preview clone registry
    |-- session ended event
```

일반 실행에서는 `Logic` 이후 front-end side effect와 after-command chain이 실행됩니다.

Preview 실행에서는 실제 데이터를 변경하지 않기 위해 clone graph 위에서 command logic을 실행합니다. `FrontEnd` 계열 이벤트는 preview에서 실행되지 않습니다.

## Minimal Example

### 1. Runtime Data 작성

```csharp
using System;
using Sinsam.CommandFramework;

[Serializable]
public sealed class UnitRuntimeData : IRuntimeData
{
    public bool IsPreview { get; set; }

    public EffectableInt Hp = new EffectableInt
    {
        BaseValue = 100
    };

    public object DeepClone()
    {
        return DeepCloneHelper.AutoClone(this);
    }
}
```

### 2. Command Context 작성

```csharp
using Sinsam.CommandFramework;

public sealed class DamageContext : ICommandContext, ICommandSessionCarrier
{
    public bool IsPreview { get; set; }
    public CommandSession CommandSession { get; set; }

    [CommandReadOnly]
    public UnitRuntimeData Target;

    public int Amount;
}
```

`ICommandSessionCarrier`를 구현하면 nested command와 after-command가 같은 `CommandSession` 안에서 실행됩니다.

### 3. Command 작성

```csharp
using Sinsam.CommandFramework;

public sealed class DealDamageCommand : Command<DamageContext>
{
    public override bool ValidateInCommand()
    {
        return Context != null &&
               Context.Target != null &&
               Context.Amount > 0;
    }

    public override bool Logic()
    {
        Context.Target.Hp.AddAdditive(
            -Context.Amount,
            source: this,
            life: ModifierLifetime.OneCommand
        );

        // OneCommand modifier를 영구 반영하고 싶을 때 호출합니다.
        Context.SetContext();

        return true;
    }
}
```

### 4. Command 실행

```csharp
var unit = new UnitRuntimeData();

var command = new DealDamageCommand
{
    Context = new DamageContext
    {
        Target = unit,
        Amount = 25
    }
};

bool success = await command.Execute();
```

## Event Pipeline

커맨드별 이벤트는 `CommandEventRegistry`를 통해 등록합니다.

```csharp
using Sinsam.CommandFramework;
using UnityEngine;

var damageEvent =
    CommandEventRegistry.GetOrCreate<DealDamageCommand, DamageContext>();

damageEvent.EditEvent += context =>
{
    context.Amount = Math.Max(0, context.Amount);
};

damageEvent.ValidationEvent += context =>
{
    return context.Target != null && context.Amount > 0;
};

damageEvent.FrontEndEvent += context =>
{
    Debug.Log($"Damage applied: {context.Amount}");
};
```

실행 순서는 다음과 같습니다.

```text
EditAsync
→ Edit
→ Validation
→ ValidateInCommand
→ Logic
→ BeforeFrontEndAsync
→ BeforeFrontEnd
→ FrontEndAsync
→ FrontEnd
→ AfterCommands
→ ResetContext
→ CommandSession drain
→ SessionEnded
```

## AfterCommands

현재 커맨드 이후 후속 커맨드를 큐에 넣을 수 있습니다.

```csharp
damageEvent.AfterCommands += (context, session) =>
{
    return new ICommand[]
    {
        new CheckDeathCommand
        {
            Context = new CheckDeathContext
            {
                Target = context.Target
            }
        }
    };
};
```

`AfterCommands`는 직접 side effect를 수행하기보다, 다음 커맨드를 반환하는 방식으로 사용하는 것이 좋습니다.

## Preview

### Basic Preview

```csharp
var command = new DealDamageCommand
{
    Context = new DamageContext
    {
        Target = unit,
        Amount = 25
    }
};

var (isValid, previewContext) = command.Preview();

if (isValid)
{
    int predictedHp = previewContext.Target.Hp.FinalValue;
}
```

`Preview()`는 clone된 context에서 `Logic`까지 실행하고, 실제 context와 runtime data는 변경하지 않습니다.

### Async Preview

`EditAsync`까지 포함한 preview가 필요하면 `AsyncPreview()`를 사용합니다.

```csharp
var (isValid, previewContext) =
    await command.AsyncPreview(PreviewAfterMode.None);
```

### PreviewAfterMode

```csharp
public enum PreviewAfterMode
{
    None,
    CollectCommands,
    SimulateCommands
}
```

| Mode | 동작 |
|---|---|
| `None` | after-command를 수집하지 않음 |
| `CollectCommands` | after-command를 queue에 넣지만 실행하지 않음 |
| `SimulateCommands` | preview clone graph 위에서 after-command까지 실행 |

## PreviewAware

외부 runtime data를 command logic 안에서 직접 참조하는 경우, preview 실행 시 실제 데이터가 변경될 수 있습니다.

이 경우 `PreviewAware.Data`를 사용해 현재 session이 preview인지 확인하고 clone을 받아 사용합니다.

```csharp
var safeData = PreviewAware.Data(Context, externalRuntimeData);
```

이미 command context 내부에 포함되어 clone된 데이터라면 직접 사용해도 됩니다.

## EffectableValue

`EffectableValue<T>`는 base value에 modifier를 적용해 final value를 계산합니다.

```csharp
var hp = new EffectableInt
{
    BaseValue = 100
};

hp.AddAdditive(-20);
hp.AddMultiplier(1.5);

int finalHp = hp.FinalValue;
```

계산 구조는 다음과 같습니다.

```text
FinalValue = (BaseValue + additive modifiers) * multiplicative modifiers
```

### ModifierLifetime

```csharp
public enum ModifierLifetime
{
    Permanent,
    OneCommand
}
```

- `Permanent`: 계속 유지되는 modifier
- `OneCommand`: command 종료 후 `ResetContext()`에서 제거되는 modifier

`OneCommand` modifier를 실제 결과로 확정하려면 `SetContext()`를 호출합니다.

```csharp
Context.Target.Hp.AddAdditive(-10, life: ModifierLifetime.OneCommand);
Context.SetContext();
```

## Deep Clone Policy

Preview는 deep clone을 기반으로 동작합니다.

기본 정책은 다음과 같습니다.

| 대상 | 복사 정책 |
|---|---|
| primitive / enum / string / decimal | 그대로 복사 |
| struct | 값 복사 후 내부 참조 필드 재귀 복사 |
| UnityEngine.Object | 참조 유지 |
| delegate | 참조 유지 |
| `[CloneReference]` | 참조 유지 |
| `[CloneIgnore]` | 복사 제외 |
| `[SelfClone]` | `IDeepCloneable.DeepClone()` 직접 호출 |
| `IList<T>` | 원소별 복사 |
| `IDictionary<TKey, TValue>` | key/value별 복사 |
| `ISet<T>` / `HashSet<T>` | 원소별 복사 |
| 일반 class | 생성자 우회 후 field 기반 재귀 복사 |

## Attributes

### `[NullCheck]`

해당 field가 null이면 command 실행을 중단합니다.

```csharp
public sealed class DamageContext : ICommandContext
{
    public bool IsPreview { get; set; }

    [NullCheck]
    public UnitRuntimeData Target;

    public int Amount;
}
```

### `[CommandReadOnly]`

command 실행 중 해당 field가 다른 객체로 교체되는 것을 감지합니다.

```csharp
[CommandReadOnly]
public UnitRuntimeData Target;
```

주의: field 자체의 교체를 감지하는 기능입니다. field가 가리키는 객체 내부의 mutation까지 막지는 않습니다.

### `[CloneReference]`

deep clone 시 해당 field를 복제하지 않고 같은 참조를 유지합니다.

```csharp
[CloneReference]
public ScriptableObject Config;
```

### `[CloneIgnore]`

deep clone 대상에서 제외합니다.

```csharp
[CloneIgnore]
private object _cachedView;
```

### `[SelfClone]`

reflection clone 대신 해당 객체의 `DeepClone()`을 직접 호출합니다.

```csharp
[SelfClone]
public CustomRuntimeData Data;
```

## Recommended Usage Rules

- `Logic()`은 가능하면 context / runtime data graph만 수정하세요.
- `Logic()` 안에서 Unity object, singleton, static state를 직접 변경하지 마세요.
- 화면 연출, 로그, 애니메이션, VFX 등 side effect는 `FrontEndEvent` 계열로 분리하세요.
- 실제 결과 예측이 필요한 UI는 `Preview()` 또는 `AsyncPreview()`를 사용하세요.
- after-effect는 직접 실행하지 말고 `AfterCommands`에서 command로 반환하세요.
- preview-safe logic이 필요하면 외부 데이터 접근 시 `PreviewAware.Data()`를 사용하세요.
- 테스트나 Play Mode 재진입 시 전역 이벤트가 누적될 수 있으므로 `CommandEventRegistry.Clear()`를 적절히 사용하세요.

## Testing

테스트 asmdef는 Editor 전용으로 구성되어 있습니다.

주요 테스트 대상:

- after-command FIFO drain
- nested command depth 처리
- reentrancy guard
- session ended event 1회 발행
- 실패 command의 after-command 미실행
- preview mode별 after-command 처리

Unity Test Runner에서 Editor 테스트로 실행하세요.

## Known Limitations

- `CommandEventRegistry`는 static registry이므로 이벤트 구독 lifetime 관리가 필요합니다.
- reflection 기반 deep clone은 IL2CPP/AOT 환경에서 프로젝트별 검증이 필요합니다.
- `FormatterServices.GetUninitializedObject`를 사용하므로 생성자 side effect나 invariant에 의존하는 타입은 주의가 필요합니다.
- `EffectableValue<int>`는 내부적으로 double 계산 후 int 변환을 수행하므로 소수점/반올림 정책을 명확히 정해 사용하는 편이 좋습니다.
- `Logic()`에서 발생한 예외는 `false`로 변환되지 않고 호출자에게 전파됩니다.

## License

현재 저장소에 별도 license 파일이 없다면, 배포 전 라이선스를 명시하는 것을 권장합니다.
