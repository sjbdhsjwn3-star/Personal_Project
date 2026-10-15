using UnityEngine;
using UnityEngine.InputSystem;

// 층 이동 문(계단실 문): 문이 열리는 연출 없이 바로 층 이동.
//  갈 수 있는 층이 하나면(1층→2층, 3층→2층) 바로 이동하고, 둘이면(2층→3층/1층) 고르는 창(FloorChoiceUI)을 띄운다.
//  - 데스크톱: 문 앞에서 E/F, 또는 조준점으로 문 클릭
//  - VR: 레이저로 문을 가리키고 트리거
public class FloorDoor : MonoBehaviour, IPointerTarget
{
    public FloorStage stage;
    [Tooltip("문짝 (클릭·레이저가 맞을 콜라이더가 있는 오브젝트)")]
    public Transform doorLeaf;

    [Header("Interaction")]
    public Key interactKey = Key.E;
    public Key altInteractKey = Key.F;
    [Tooltip("이 거리(수평) 안에서만 쓸 수 있다 — VR 레이저도 동일")]
    [Range(0.5f, 6f)] public float interactRadius = 3f;
    [Range(0.5f, 5f)] public float maxHeightDiff = 2.5f;

    [Header("Text")]
    public string promptDesktop = "[ E ]  {0}";
    public string promptVR = "트리거  —  {0}";
    public string promptLocked = "아직 이 층에 올 수 없어요";
    public string tooFarToast = "문 가까이 가서 눌러 주세요";

    static FloorDoor promptOwner;   // 가장 가까운 문 하나만 안내

    void Awake()
    {
        if (doorLeaf == null) return;
        PointerTargetLink.Attach(doorLeaf.gameObject, this);
        // 에셋 기본 문 스크립트가 같은 클릭으로 문을 여닫지 않게 끈다
        var legacy = doorLeaf.GetComponent<SojaExiles.opencloseDoor>();
        if (legacy != null) legacy.enabled = false;
    }

    Vector3 Center
    {
        get
        {
            var t = doorLeaf != null ? doorLeaf : transform;
            var r = t.GetComponentInChildren<Renderer>();
            return r != null ? r.bounds.center : t.position;
        }
    }

    float Dist(Vector3 p) { var c = Center; return new Vector2(p.x - c.x, p.z - c.z).magnitude; }

    bool IsNear(Vector3 p) => Dist(p) <= interactRadius && Mathf.Abs(p.y - Center.y) <= maxHeightDiff;

    string Prompt(GameLoopManager m)
    {
        if (!stage.IsUnlocked) return promptLocked;
        return string.Format(VRRig.IsActiveNow ? promptVR : promptDesktop, m.DestinationText(stage));
    }

    void Update()
    {
        var m = GameLoopManager.Instance;
        if (m == null || m.player == null || stage == null) return;

        var p = m.player.position;
        bool near = !m.UI.IsOpen && !m.IsTeleporting && !PaintingViewer.IsBusy && !FloorChoiceUI.IsOpen && IsNear(p);

        if (near && (promptOwner == null || promptOwner == this || Dist(p) < promptOwner.Dist(p)))
        {
            promptOwner = this;
            m.UI.ShowPrompt(Prompt(m));
        }
        else if (promptOwner == this && !near)
        {
            promptOwner = null;
            m.UI.HidePrompt();
        }

        if (promptOwner != this || !near) return;
        var kb = Keyboard.current;
        if (kb != null && (kb[interactKey].wasPressedThisFrame || kb[altInteractKey].wasPressedThisFrame)) TryOpen();
    }

    public void OnPointerClick(Vector3 playerPosition) => TryOpen();

    public void TryOpen()
    {
        var m = GameLoopManager.Instance;
        if (m == null || m.IsTeleporting || FloorChoiceUI.IsOpen || stage == null) return;
        if (!stage.IsUnlocked) { m.UI.Toast(promptLocked, 2f); return; }
        if (!IsNear(m.player.position)) { m.UI.Toast(tooFarToast, 2f); return; }
        if (promptOwner == this) { promptOwner = null; m.UI.HidePrompt(); }
        m.PassDoor(stage);
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
