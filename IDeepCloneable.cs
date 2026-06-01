using System;

/// <summary>
/// 깊은 복사를 인터페이스로 강제한다.
/// Preview는 실제 데이터를 변경하지 않고, 이 복사본(PreviewInstance)에 효과를 적용한 뒤
/// 그 사본을 통째로 반환한다. 기본 구현은 DeepCloneHelper.AutoClone(this)에 위임하면 된다.
/// </summary>
public interface IDeepCloneable
{
    object DeepClone();
}

/// <summary>
/// "이것은 Preview(가짜) 인스턴스인가?"를 나타내는 플래그.
/// - DeepClone으로 만들어진 사본은 IsPreview=true가 된다.
/// - Logic 안에서 새 커맨드를 만들 때, 그 Context/RuntimeData가 preview면
///   파이프라인이 자동으로 부수효과·commit 없는 preview 경로로 재분류한다.
/// </summary>
public interface IPreviewable
{
    bool IsPreview { get; set; }
}

/// <summary>
/// DeepClone 시 "복사하지 않고 참조를 그대로 유지"할 필드에 부착한다.
/// 예: Owner(Mono), 공유 ScriptableObject, 다른 엔티티를 가리키는 Target 등.
/// (UnityEngine.Object 파생 타입과 delegate는 어트리뷰트가 없어도 자동으로 참조 복사된다.)
/// 자동 프로퍼티에 적용할 때는 [field: CloneReference] 형태로 백킹 필드에 부착할 것.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = true)]
public sealed class CloneReferenceAttribute : Attribute { }

/// <summary>
/// DeepClone 대상에서 완전히 제외한다(복사도, 참조 유지도 하지 않고 기본값으로 둔다).
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = true)]
public sealed class CloneIgnoreAttribute : Attribute { }
