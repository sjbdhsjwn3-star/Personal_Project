using UnityEngine;

// VR 레이저(트리거) 또는 마우스 클릭으로 누를 수 있는 대상 (문 등)
public interface IPointerTarget
{
    void OnPointerClick(Vector3 playerPosition);
}

// 콜라이더가 있는 오브젝트(문짝 등)에 붙어서 실제 대상(IPointerTarget)을 가리킨다.
// 대상 스크립트가 콜라이더와 다른 오브젝트에 있어도 레이캐스트로 찾을 수 있게 하는 연결 고리.
public class PointerTargetLink : MonoBehaviour
{
    public MonoBehaviour target;

    public static void Attach(GameObject colliderObject, MonoBehaviour target)
    {
        var link = colliderObject.GetComponent<PointerTargetLink>();
        if (link == null) link = colliderObject.AddComponent<PointerTargetLink>();
        link.target = target;
    }

    public static IPointerTarget Find(Collider hit)
    {
        var link = hit != null ? hit.GetComponentInParent<PointerTargetLink>() : null;
        return link != null && link.target != null && link.target.isActiveAndEnabled ? link.target as IPointerTarget : null;
    }
}
