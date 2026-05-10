using System;
using System.Collections.Generic;

/// <summary>
/// CommandEvent를 Type 키로 관리하는 레지스트리.
/// Hemi 방식: Type 키 + 제네릭 타입 검증으로 런타임 안전성 확보.
/// </summary>
public sealed class CommandEventRegistry
{
    private readonly Dictionary<Type, ICommandEvent> _events = new();

    /// <summary>
    /// commandType에 해당하는 CommandEvent를 가져오거나 새로 생성.
    /// 이미 등록된 이벤트의 Context 타입이 다르면 예외 발생.
    /// </summary>
    public CommandEvent<TContext> GetOrCreate<TContext>(Type commandType)
        where TContext : class, ICommandContext
    {
        if (_events.TryGetValue(commandType, out var existing))
        {
            if (existing is CommandEvent<TContext> typed)
                return typed;

            throw new InvalidOperationException(
                $"CommandEvent context type mismatch. " +
                $"Command: {commandType.FullName}, " +
                $"Stored: {existing.ContextType.FullName}, " +
                $"Requested: {typeof(TContext).FullName}"
            );
        }

        var created = new CommandEvent<TContext>
        {
            CommandName = commandType.Name
        };

        _events.Add(commandType, created);
        return created;
    }

    /// <summary>
    /// 제네릭 타입 파라미터로 커맨드 타입을 지정하는 편의 메서드.
    /// 이벤트 구독 시 사용: 
    ///   registry.GetOrCreate&lt;AttackCommand, HealthCommandContext&gt;().EditEvent += ...
    /// </summary>
    public CommandEvent<TContext> GetOrCreate<TCommand, TContext>()
        where TCommand : Command<TContext>
        where TContext : class, ICommandContext
    {
        return GetOrCreate<TContext>(typeof(TCommand));
    }
}

/// <summary>
/// 프레임워크 전역 이벤트 버스.
/// 프로젝트별 확장이 필요하면 partial class 또는 상속으로 추가.
/// (예: PRC의 TurnEvents는 프로젝트 쪽에서 EventBus를 상속해 추가)
/// </summary>
public class EventBus : MonoSingleton<EventBus>
{
    public readonly CommandEventRegistry CommandEvents = new();
}
