using System.Threading;

namespace Sinsam.CommandFramework
{
    public static class CommandPreviewScope
    {
        private sealed class State
        {
            public int Depth;
            public PreviewSnapshot Snapshot;
        }

        private static readonly AsyncLocal<State> Current = new AsyncLocal<State>();

        public static bool IsActive => Current.Value != null && Current.Value.Depth > 0;

        public static PreviewSnapshot Snapshot => Current.Value?.Snapshot;

        public static void Enter()
        {
            var state = Current.Value;
            if (state == null)
            {
                state = new State
                {
                    Snapshot = new PreviewSnapshot()
                };
                Current.Value = state;
            }

            state.Depth++;
        }

        public static void Exit()
        {
            var state = Current.Value;
            if (state == null || state.Depth <= 0)
            {
                return;
            }

            state.Depth--;
            if (state.Depth == 0)
            {
                Current.Value = null;
            }
        }
    }
}
