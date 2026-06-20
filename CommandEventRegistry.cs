using System;
using System.Collections.Generic;

namespace Sinsam.CommandFramework
{
    /// <summary>
    /// Stores CommandEvent instances by command type.
    /// </summary>
    public static class CommandEventRegistry
    {
        public static readonly Dictionary<Type, ICommandEvent> CommandEvents = new();

        public static CommandEvent<TContext> GetOrCreate<TContext>(Type commandType)
            where TContext : class, ICommandContext
        {
            if (CommandEvents.TryGetValue(commandType, out var existing))
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

            CommandEvents.Add(commandType, created);
            return created;
        }

        public static CommandEvent<TContext> GetOrCreate<TCommand, TContext>()
            where TCommand : Command<TContext>
            where TContext : class, ICommandContext
        {
            return GetOrCreate<TContext>(typeof(TCommand));
        }
    }
}
