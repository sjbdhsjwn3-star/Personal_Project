using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

// 상호작용 문: 플레이어가 가까이 오면 안내 문구를 띄우고, 지정 키(E/F)로 연다.
//  - requiredStage의 퀴즈를 풀어야 열린다 (비우면 항상 열림)
//  - 열리면 blockers(보이지 않는 막이)를 끄고 문짝을 경첩 기준으로 회전
//  - isExit이면 열 때 게임 클리어
//  - VR: 레이저로 문짝을 가리키고 트리거 (데스크톱: 조준점 클릭도 가능)
public class InteractDoor : MonoBehaviour, IPointerTarget
{
    [Header("Door")]
    [Tooltip("회전할 문짝 (피벗이 경첩 위치여야 함)")]
    public Transform doorLeaf;
    [Range(30f, 120f)] public float openAngle = 95f;
    [Range(0.1f, 3f)] public float openDuration = 0.8f;
    [Tooltip("swingAwayFromPlayer를 끄면 회전 대신 이 상태를 재생")]
    public Animator animator;
    [Tooltip("켜면 애니메이션 대신 플레이어 반대쪽(안쪽)으로 열려 몸을 밀지 않는다")]
    public bool swingAwayFromPlayer = true;
    public string openState = "Opening";
    [Tooltip("문이 열리기 전까지 통로를 막는 오브젝트")]
    public List<GameObject> blockers = new List<GameObject>();

    [Header("Interaction")]
    public Key interactKey = Key.E;
    public Key altInteractKey = Key.F;
    [Tooltip("이 거리 안(수평)에 들어오면 상호작용 가능")]
    [Range(0.5f, 5f)] public float interactRadius = 2.2f;
    [Tooltip("문과 플레이어의 높이 차이 허용치 (다른 층에서 반응하지 않게)")]
    [Range(0.5f, 5f)] public float maxHeightDiff = 2.5f;

    [Header("Condition")]
    [Tooltip("이 스테이지의 퀴즈를 풀어야 열림 (비우면 항상 열림)")]
    public FloorStage requiredStage;
    [Tooltip("열면 게임 클리어 (최종 출구)")]
    public bool isExit;

    [Header("Text")]
    public string promptOpen = "[ E ]  문 열기";
    public string promptOpenVR = "트리거  —  문 열기";
    public string promptLocked = "잠겨 있어요 — 이 층의 퀴즈를 먼저 풀어야 해요";
    public string openedToast = "";

    public UnityEvent onOpened;

    public bool IsOpen { get; private set; }
    public bool IsLocked => requiredStage != null && !requiredStage.IsSolved;

    static InteractDoor promptOwner;   // 안내 문구를 띄운 문 (가장 가까운 한 개만)

    void Awake()
    {
        if (doorLeaf != null) PointerTargetLink.Attach(doorLeaf.gameObject, this);
    }

    // VR 레이저 / 조준점 클릭
    public void OnPointerClick(Vector3 playerPosition)
    {
        var m = GameLoopManager.Instance;
        if (IsOpen || m == null) return;
        if (!IsPlayerNear(playerPosition)) { m.UI.Toast("문 가까이 가서 열어 주세요", 2f); return; }
        if (IsLocked) { m.UI.Toast(promptLocked, 2f); return; }
        Open(playerPosition);
    }

    void Update()
    {
        var m = GameLoopManager.Instance;
        if (m == null || m.player == null) return;

        bool near = !IsOpen && !m.UI.IsOpen && !PaintingViewer.IsBusy && IsPlayerNear(m.player.position);
        if (near && (promptOwner == null || promptOwner == this || Dist(m.player.position) < promptOwner.Dist(m.player.position)))
        {
            promptOwner = this;
            m.UI.ShowPrompt(IsLocked ? promptLocked : VRRig.IsActiveNow ? promptOpenVR : promptOpen);
        }
        else if (promptOwner == this && !near)
        {
            promptOwner = null;
            m.UI.HidePrompt();
        }

        if (promptOwner != this || !near) return;
        var kb = Keyboard.current;
        if (kb == null || !(kb[interactKey].wasPressedThisFrame || kb[altInteractKey].wasPressedThisFrame)) return;

        if (IsLocked) { m.UI.Toast(promptLocked, 2f); return; }
        Open(m.player.position);
    }

    Vector3 Center => doorLeaf != null && doorLeaf.GetComponentInChildren<Renderer>() != null
        ? doorLeaf.GetComponentInChildren<Renderer>().bounds.center
        : transform.position;

    float Dist(Vector3 p) { var c = Center; return new Vector2(p.x - c.x, p.z - c.z).magnitude; }

    bool IsPlayerNear(Vector3 p) => Dist(p) <= interactRadius && Mathf.Abs(p.y - Center.y) <= maxHeightDiff;

    public void Open(Vector3 fromPosition)
    {
        if (IsOpen) return;
        IsOpen = true;
        foreach (var b in blockers) if (b != null) b.SetActive(false);

        var m = GameLoopManager.Instance;
        if (promptOwner == this) { promptOwner = null; m.UI.HidePrompt(); }
        if (!string.IsNullOrEmpty(openedToast)) m.UI.Toast(openedToast, 2.5f);

        GameSfx.PlayDoor(Center);
        if (doorLeaf != null && (swingAwayFromPlayer || animator == null)) StartCoroutine(Swing(fromPosition));
        else if (animator != null) animator.Play(openState);
        onOpened?.Invoke();
        if (isExit) m.CompleteGame();
    }

    // 플레이어 반대쪽으로 밀어서 열리게 회전 방향을 고른다
    IEnumerator Swing(Vector3 fromPosition)
    {
        if (animator != null) animator.enabled = false;   // 애니메이션이 회전을 덮어쓰지 않게
        var end = DoorSwing.AwayFrom(doorLeaf, Center, fromPosition, openAngle);
        DoorSwing.SetColliders(doorLeaf, false);          // 도는 동안 플레이어를 밀지 않게
        yield return DoorSwing.Rotate(doorLeaf, end, openDuration);
        DoorSwing.SetColliders(doorLeaf, true);
    }

    void OnDisable()
    {
        if (promptOwner == this) { promptOwner = null; GameLoopManager.Instance?.UI.HidePrompt(); }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(Center, interactRadius);
    }
}
