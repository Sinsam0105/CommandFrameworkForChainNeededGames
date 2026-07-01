using System;
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

        public delegate bool ValidationEventHandler(T context);
        public delegate UniTask AsyncEventHandler(T context);
        public delegate void EditEventHandler(T context);
        public delegate void ResolveEventHandler(T context);
        public delegate void CommandEventHandler(T context);
        public delegate void NumPreviewCaptureHandler(T context, bool isValid);

        private AsyncEventHandler _editAsyncEvent;
        private AsyncEventHandler _frontEndAsyncEvent;
        private ValidationEventHandler _validationEvent;
        private EditEventHandler _editEvent;
        private CommandEventHandler _numChangeEvent;
        private ResolveEventHandler _resolveEvent;
        private CommandEventHandler _beforeFrontEndEvent;
        private CommandEventHandler _frontEndEvent;
        private CommandEventHandler _afterEvent;

        public event AsyncEventHandler EditAsyncEvent
        {
            add => _editAsyncEvent += value;
            remove => _editAsyncEvent -= value;
        }

        public event EditEventHandler EditEvent
        {
            add => _editEvent += value;
            remove => _editEvent -= value;
        }

        public event CommandEventHandler NumChangeEvent
        {
            add => _numChangeEvent += value;
            remove => _numChangeEvent -= value;
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

        public event ResolveEventHandler ResolveEvent
        {
            add => _resolveEvent += value;
            remove => _resolveEvent -= value;
        }

        public event AsyncEventHandler FrontEndAsyncEvent
        {
            add => _frontEndAsyncEvent += value;
            remove => _frontEndAsyncEvent -= value;
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

        public async UniTask<bool> Run(T context, Command<T> command)
        {
            if (context == null || command == null)
                return false;

            command.Context = context;
            ModifierIdSnapshot modifierSnapshot = null;

            try
            {
                if (_editAsyncEvent != null)
                    await InvokeSequentialAsync(_editAsyncEvent, context);

                modifierSnapshot = ModifierIdSnapshot.Capture(context);
                InvokeSequential(_numChangeEvent, context);

                if (!RunValidationOnly(context, command))
                    return false;

                _editEvent?.Invoke(context);

                InvokeSequential(_beforeFrontEndEvent, context);
                _resolveEvent?.Invoke(context);

                bool result = command.Logic();
                if (!result)
                    return false;

                if (_frontEndAsyncEvent != null)
                    await InvokeSequentialAsync(_frontEndAsyncEvent, context);

                InvokeSequential(_frontEndEvent, context);
                InvokeSequential(_afterEvent, context);

                return true;
            }
            finally
            {
                modifierSnapshot?.CleanUp();
            }
        }

        public CommandNumPreviewResult NumPreview(
            T context,
            Command<T> command,
            NumPreviewCaptureHandler beforeCapture = null)
        {
            if (context == null || command == null)
                return CommandNumPreviewResult.Invalid;

            command.Context = context;
            return RunNumPreviewCore(context, command, beforeCapture);
        }

        public async UniTask<CommandNumPreviewResult> NumPreviewAsync(
            T context,
            Command<T> command,
            NumPreviewCaptureHandler beforeCapture = null)
        {
            if (context == null || command == null)
                return CommandNumPreviewResult.Invalid;

            command.Context = context;

            if (_editAsyncEvent != null)
                await InvokeSequentialAsync(_editAsyncEvent, context);

            return RunNumPreviewCore(context, command, beforeCapture);
        }

        public bool RunValidationOnly(T context, Command<T> command)
        {
            if (!command.ValidateInCommand())
                return false;

            if (_validationEvent != null)
            {
                foreach (var handler in _validationEvent.GetInvocationList().Cast<ValidationEventHandler>())
                {
                    if (!handler(context))
                        return false;
                }
            }

            return true;
        }

        private CommandNumPreviewResult RunNumPreviewCore(
            T context,
            Command<T> command,
            NumPreviewCaptureHandler beforeCapture)
        {
            var modifierSnapshot = ModifierIdSnapshot.Capture(context);

            try
            {
                InvokeSequential(_numChangeEvent, context);
                bool isValid = RunValidationOnly(context, command);
                beforeCapture?.Invoke(context, isValid);
                return CommandNumPreviewResult.FromContext(isValid, context);
            }
            finally
            {
                modifierSnapshot.CleanUp();
            }
        }

        private static void InvokeSequential(CommandEventHandler evt, T context)
        {
            if (evt == null)
                return;

            foreach (CommandEventHandler handler in evt.GetInvocationList())
                handler(context);
        }

        private static async UniTask InvokeSequentialAsync(AsyncEventHandler evt, T context)
        {
            if (evt == null)
                return;

            foreach (AsyncEventHandler handler in evt.GetInvocationList())
                await handler(context);
        }
    }
}
