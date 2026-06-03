using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;

namespace Sinsam.CommandFramework
{
    public interface ICommandEvent
    {
        string CommandName { get; set; }
        Type ContextType { get; }
    }

    public class CommandEvent<T> : ICommandEvent where T : class, ICommandContext
    {
        public string CommandName { get; set; } = string.Empty;
        public Type ContextType => typeof(T);

        public delegate UniTask<bool> AsyncValidationEventHandler(T context);
        public delegate bool ValidationEventHandler(T context);
        public delegate UniTask AsyncEventHandler(T context);
        public delegate void EditEventHandler(T context);
        public delegate void CommandEventHandler(T context);

        private AsyncEventHandler _editAsyncEvent;
        private AsyncValidationEventHandler _validationAsyncEvent;
        private AsyncEventHandler _beforeFrontEndAsyncEvent;
        private AsyncEventHandler _frontEndAsyncEvent;
        private AsyncEventHandler _afterAsyncEvent;

        private EditEventHandler _editEvent;
        private ValidationEventHandler _validationEvent;
        private CommandEventHandler _beforeFrontEndEvent;
        private CommandEventHandler _frontEndEvent;
        private CommandEventHandler _afterEvent;
        private CommandEventHandler _previewFrontEndEvent;

        public event AsyncEventHandler EditAsyncEvent
        {
            add => _editAsyncEvent += value;
            remove => _editAsyncEvent -= value;
        }

        public event AsyncValidationEventHandler ValidationAsyncEvent
        {
            add => _validationAsyncEvent += value;
            remove => _validationAsyncEvent -= value;
        }

        public event AsyncEventHandler BeforeFrontEndAsyncEvent
        {
            add => _beforeFrontEndAsyncEvent += value;
            remove => _beforeFrontEndAsyncEvent -= value;
        }

        public event AsyncEventHandler FrontEndAsyncEvent
        {
            add => _frontEndAsyncEvent += value;
            remove => _frontEndAsyncEvent -= value;
        }

        public event AsyncEventHandler AfterAsyncEvent
        {
            add => _afterAsyncEvent += value;
            remove => _afterAsyncEvent -= value;
        }

        public event EditEventHandler EditEvent
        {
            add => _editEvent += value;
            remove => _editEvent -= value;
        }

        public event ValidationEventHandler ValidationEvent
        {
            add => _validationEvent += value;
            remove => _validationEvent -= value;
        }

        public event CommandEventHandler BeforeFrontEndEvent
        {
            add => _beforeFrontEndEvent += value;
            remove => _beforeFrontEndEvent -= value;
        }

        public event CommandEventHandler FrontEndEvent
        {
            add => _frontEndEvent += value;
            remove => _frontEndEvent -= value;
        }

        public event CommandEventHandler AfterEvent
        {
            add => _afterEvent += value;
            remove => _afterEvent -= value;
        }

        public event CommandEventHandler PreviewFrontEndEvent
        {
            add => _previewFrontEndEvent += value;
            remove => _previewFrontEndEvent -= value;
        }

        public UniTask<bool> Run(T context, Command<T> command)
            => RunInternal(context, command, preview: false);

        internal async UniTask<bool> RunInternal(T context, Command<T> command, bool preview)
        {
            try
            {
                if (!preview && _editAsyncEvent != null)
                {
                    await InvokeSequentialAsync(_editAsyncEvent, context);
                }

                _editEvent?.Invoke(context);

                if (RuntimeDataReflection.HasNullCheckViolation(context, out var nullField))
                {
                    UnityEngine.Debug.LogWarning($"[{CommandName}] NullCheck failed: '{nullField}' is null.");
                    return false;
                }

                if (_validationAsyncEvent != null)
                {
                    if (!await InvokeValidationAsync(_validationAsyncEvent, context))
                        return false;
                }

                if (_validationEvent != null)
                {
                    foreach (var handler in _validationEvent.GetInvocationList().Cast<ValidationEventHandler>())
                    {
                        if (!handler(context))
                            return false;
                    }
                }

                if (!command.ValidateInCommand())
                    return false;

                bool result = command.Logic();
                if (!result)
                    return false;

                if (preview)
                {
                    InvokeSequential(_previewFrontEndEvent, context);
                }
                else
                {
                    if (_beforeFrontEndAsyncEvent != null)
                        await InvokeSequentialAsync(_beforeFrontEndAsyncEvent, context);

                    InvokeSequential(_beforeFrontEndEvent, context);

                    if (_frontEndAsyncEvent != null)
                        await InvokeSequentialAsync(_frontEndAsyncEvent, context);

                    InvokeSequential(_frontEndEvent, context);

                    if (_afterAsyncEvent != null)
                        await InvokeSequentialAsync(_afterAsyncEvent, context);

                    InvokeSequential(_afterEvent, context);
                }

                return true;
            }
            finally
            {
                if (!preview)
                {
                    context?.ResetContext();
                }
            }
        }

        public (bool IsValid, T Context) PreviewRun(T context, Command<T> command)
        {
            if (context == null)
                return (false, null);

            IDictionary<object, object> registry = DeepCloneHelper.NewRegistry();
            var clone = DeepCloneHelper.AutoClone(context, markPreview: true, registry);
            var original = command.Context;
            command.Context = clone;

            try
            {
                var runTask = RunInternal(clone, command, preview: true);

                if (!runTask.Status.IsCompleted())
                {
                    throw new InvalidOperationException(
                        $"[{CommandName}] Preview did not complete synchronously. Logic() and preview-phase handlers must not await.");
                }

                bool valid = runTask.GetAwaiter().GetResult();
                return (valid, clone);
            }
            finally
            {
                command.Context = original;
            }
        }

        private static void InvokeSequential(CommandEventHandler evt, T context)
        {
            if (evt == null) return;
            foreach (CommandEventHandler handler in evt.GetInvocationList())
                handler(context);
        }

        private static async UniTask InvokeSequentialAsync(AsyncEventHandler evt, T context)
        {
            if (evt == null) return;
            foreach (AsyncEventHandler handler in evt.GetInvocationList())
                await handler(context);
        }

        private static async UniTask<bool> InvokeValidationAsync(AsyncValidationEventHandler evt, T context)
        {
            if (evt == null) return true;
            foreach (AsyncValidationEventHandler handler in evt.GetInvocationList().Cast<AsyncValidationEventHandler>())
            {
                if (!await handler(context))
                    return false;
            }
            return true;
        }
    }
}
