using System.Collections.Generic;

namespace Sinsam.CommandFramework
{
    /// <summary>
    /// Preview 실행 중 real 객체와 clone 객체의 대응 관계를 보존하는 세션.
    /// 같은 PreviewSession을 공유하면 같은 real 객체는 항상 같은 preview clone으로 매핑된다.
    /// </summary>
    public sealed class PreviewSession
    {
        private readonly IDictionary<object, object> _registry = DeepCloneHelper.NewRegistry();

        public T GetClone<T>(T real) where T : class
        {
            if (real == null)
                return null;

            return DeepCloneHelper.AutoClone(real, markPreview: true, _registry, this);
        }
    }

    /// <summary>
    /// PreviewSession을 context나 runtime data에 싣기 위한 선택 인터페이스.
    /// ICommandContext에는 강제하지 않아 기존 context 구현체와의 호환성을 유지한다.
    /// </summary>
    public interface IPreviewSessionCarrier
    {
        PreviewSession PreviewSession { get; set; }
    }

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
            return context is IPreviewSessionCarrier carrier ? Data(carrier, real) : real;
        }

        public static T Data<T>(IPreviewSessionCarrier carrier, T real) where T : class
        {
            if (real == null)
                return null;

            return carrier?.PreviewSession != null ? carrier.PreviewSession.GetClone(real) : real;
        }
    }
}