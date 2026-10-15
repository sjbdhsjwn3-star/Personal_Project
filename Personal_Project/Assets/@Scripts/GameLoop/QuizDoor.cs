using UnityEngine;
using UnityEngine.InputSystem;

// 퀴즈 문: 문 앞에서 E(또는 F)를 누르면 그 층의 퀴즈 창을 연다.
// 문은 실제로 열리지 않는다. 정답을 맞히면 GameLoopManager가 다음 층 스폰 위치로 플레이어를 옮긴다.
public class QuizDoor : MonoBehaviour
{
    public FloorStage stage;
    [Tooltip("거리 계산 기준 (문짝). 비우면 이 오브젝트")]
    public Transform focus;

    public Key interactKey = Key.E;
    public Key altInteractKey = Key.F;
    [Range(0.5f, 5f)] public float interactRadius = 2.2f;
    [Range(0.5f, 5f)] public float maxHeightDiff = 2.5f;

    public string promptQuiz = "[ E ]  문을 두드려 퀴즈 풀기";
    public string promptSolved = "[ E ]  다음 층으로 이동";
    public string promptLocked = "아직 이 층에 올 수 없어요";

    static QuizDoor promptOwner;   // 가장 가까운 문 하나만 안내

    Vector3 Center
    {
        get
        {
            var t = focus != null ? focus : transform;
            var r = t.GetComponentInChildren<Renderer>();
            return r != null ? r.bounds.center : t.position;
        }
    }

    float Dist(Vector3 p) { var c = Center; return new Vector2(p.x - c.x, p.z - c.z).magnitude; }

    void Update()
    {
        var m = GameLoopManager.Instance;
        if (m == null || m.player == null || stage == null) return;

        var p = m.player.position;
        bool near = !m.UI.IsOpen && !m.IsTeleporting && Dist(p) <= interactRadius && Mathf.Abs(p.y - Center.y) <= maxHeightDiff;

        if (near && (promptOwner == null || promptOwner == this || Dist(p) < promptOwner.Dist(p)))
        {
            promptOwner = this;
            m.UI.ShowPrompt(!stage.IsUnlocked ? promptLocked : stage.IsSolved ? promptSolved : promptQuiz);
        }
        else if (promptOwner == this && !near)
        {
            promptOwner = null;
            m.UI.HidePrompt();
        }

        if (promptOwner != this || !near) return;
        var kb = Keyboard.current;
        if (kb == null || !(kb[interactKey].wasPressedThisFrame || kb[altInteractKey].wasPressedThisFrame)) return;

        if (!stage.IsUnlocked) { m.UI.Toast(promptLocked, 2f); return; }
        m.UI.HidePrompt();
        promptOwner = null;
        if (stage.IsSolved) m.GoToNextFloor(stage);   // 이미 푼 층: 다시 이동만
        else if (stage.Quiz != null) m.UI.Open(stage.Quiz);
    }

    void OnDisable()
    {
        if (promptOwner == this) { promptOwner = null; GameLoopManager.Instance?.UI.HidePrompt(); }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(Center, interactRadius);
    }
}
