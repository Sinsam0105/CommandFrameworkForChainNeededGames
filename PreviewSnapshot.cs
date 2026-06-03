namespace Sinsam.CommandFramework
{
    /// <summary>
    /// Preview 관련 호환 헬퍼.
    /// 기존 Data(real)는 no-op으로 유지하고, session carrier를 넘기는 overload에서만 clone을 반환한다.
    /// </summary>
    public static class PreviewAware
    {
        public static T Data<T>(T real) where T : class
        {
            return real;
        }

        public static T Data<T>(ICommandContext context, T real) where T : class
        {
            return context is ICommandSessionCarrier carrier ? Data(carrier, real) : real;
        }

        public static T Data<T>(ICommandSessionCarrier carrier, T real) where T : class
        {
            if (real == null)
                return null;

            return carrier?.CommandSession != null && carrier.CommandSession.IsPreview
                ? carrier.CommandSession.GetPreviewClone(real)
                : real;
        }
    }
}
