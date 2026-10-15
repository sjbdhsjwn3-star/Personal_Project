using UnityEngine;
using UnityEngine.Events;

// 그림 속 클릭 대상 (1층 숨은 물건, 3층 숨은 숫자)
// 콜라이더(Trigger 가능)가 필요하다. 조준점으로 클릭하면 Click()이 호출된다.
[RequireComponent(typeof(Collider))]
public class QuizClickTarget : MonoBehaviour
{
    public string displayName = "숨은 물건";
    [Tooltip("3층 숫자 단서일 때 숫자, 아니면 -1")]
    public int digit = -1;
    [Tooltip("비밀번호에서 이 숫자가 들어갈 자리 (0부터)")]
    public int digitOrder = -1;
    public UnityEvent onFound;

    public bool Found { get; private set; }
    public FloorQuiz Owner { get; set; }

    public void Click()
    {
        if (Found || Owner == null || !Owner.Stage.IsUnlocked) return;
        Found = true;
        onFound?.Invoke();
        Owner.OnTargetFound(this);
    }
}
