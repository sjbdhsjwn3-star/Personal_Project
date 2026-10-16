using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

// 층별 스테이지 순환 루프
//  [퀴즈 끔 — 기본] 계단실 문(FloorDoor)으로 바로 위·아래층으로 이동 (1층→2층, 2층→3층/1층 선택, 3층→2층). 3층 엔딩 책에서 게임 종료
//  [퀴즈 켬] 탐색(ObservationPoint 전부 관찰) → 퀴즈 창 자동 오픈 → 정답 → 다음 층 해제 → ...
//           Tab(또는 Q)로 언제든 퀴즈 창을 열고 닫을 수 있다.
//  화면 중앙(조준점)으로 클릭하면 문(IPointerTarget)·QuizClickTarget을 찾는다. VR에서는 VRRig의 레이저가 대신한다.
public class GameLoopManager : MonoBehaviour
{
    public static GameLoopManager Instance { get; private set; }

    [Header("Mode")]
    [Tooltip("끄면 퀴즈 없이 문만 열면 다음 층으로 이동")]
    public bool useQuiz = false;

    [Header("Stages (아래층부터 순서대로)")]
    public List<FloorStage> stages = new List<FloorStage>();

    [Header("Player")]
    public Transform player;
    public Camera playerCamera;
    [Tooltip("퀴즈 창이 열려 있는 동안 꺼 둘 컴포넌트 (이동·시점 회전 등)")]
    public List<Behaviour> disableWhileUIOpen = new List<Behaviour>();

    [Header("Input")]
    public Key toggleKey = Key.Tab;
    public Key altToggleKey = Key.Q;
    [Range(1f, 20f)] public float clickDistance = 8f;

    [Header("Teleport")]
    [Tooltip("마지막 층 정답 후 이동할 위치 (1층 출입문 안쪽)")]
    public Transform finalSpawnPoint;
    [Range(0f, 3f)] public float teleportDelay = 1.6f;   // 성공 연출을 보여 준 뒤 이동
    [Range(0.05f, 2f)] public float fadeTime = 0.45f;

    [Header("Events")]
    public UnityEvent<int> onFloorUnlocked;   // 해제된 층 번호(1부터)
    public UnityEvent onGameCleared;

    // 거리 판정용 플레이어 위치 (발 높이). VR에서는 방 안을 실제로 걸어 다니면 리그 중심과 머리가 어긋나므로 머리의 수평 위치를 쓴다
    public Vector3 PlayerPosition
    {
        get
        {
            if (player == null) return Vector3.zero;
            if (!VRRig.IsActiveNow) return player.position;
            var h = VRRig.Current.head.transform.position;
            return new Vector3(h.x, player.position.y, h.z);
        }
    }

    public QuizUI UI { get; private set; }
    public FloorStage CurrentStage { get; private set; }
    public bool IsTeleporting { get; private set; }

    void Awake()
    {
        Instance = this;
        UI = QuizUI.Create(transform);
        UI.OnVisibilityChanged += SetPlayerControl;
        VRRig.Activated += OnVRActivated;
    }

    void OnDestroy() => VRRig.Activated -= OnVRActivated;

    void Start()
    {
        for (int i = 0; i < stages.Count; i++)
        {
            stages[i].Init(this, i + 1);
            stages[i].SetUnlocked(i == 0);
        }
        UI.Toast(useQuiz
            ? "미술관을 둘러보세요. 그림을 모두 관찰하면 퀴즈가 열려요.  (Tab / Q : 퀴즈 창)"
            : "미술관을 둘러보세요. 계단실 문으로 다른 층에 갈 수 있어요.", 5f);
        SetPlayerControl(false);
    }

    // VR 모드로 전환되면: 데스크톱 이동·시점 스크립트를 다시 켜지 않게 빼고, 안내 UI를 눈앞에 띄운다
    void OnVRActivated(VRRig rig)
    {
        disableWhileUIOpen.RemoveAll(b => rig.disableInVR.Contains(b));
        UI.AttachToXRCamera(rig.head);
    }

    void Update()
    {
        UpdateCurrentStage();
        HandleToggleKey();
        HandleWorldClick();
    }

    // 플레이어 높이로 지금 서 있는 층을 판단 (해제된 층만)
    void UpdateCurrentStage()
    {
        if (player == null) return;
        float y = player.position.y;
        FloorStage found = null;
        foreach (var s in stages)
            if (s.IsUnlocked && y >= s.minY && y < s.maxY) found = s;
        if (found == CurrentStage) return;

        CurrentStage = found;
        if (found != null && !found.IsSolved)
            UI.Toast(found.Quiz != null ? $"{found.floorNumber}층 — {found.Quiz.Title}" : $"{found.floorNumber}층", 3f);
    }

    void HandleToggleKey()
    {
        if (!useQuiz) return;
        var kb = Keyboard.current;
        if (kb == null) return;
        if (!kb[toggleKey].wasPressedThisFrame && !kb[altToggleKey].wasPressedThisFrame) return;
        // 입력칸에 글자를 치는 중에는 Q 토글 무시
        if (UI.IsTyping && kb[altToggleKey].wasPressedThisFrame) return;

        if (UI.IsOpen) UI.Close();
        else if (CurrentStage != null && CurrentStage.Quiz != null) UI.Open(CurrentStage.Quiz);
        else UI.Toast("이 층에는 열 수 있는 퀴즈가 없어요.", 2f);
    }

    void HandleWorldClick()
    {
        if (UI.IsOpen || playerCamera == null || PaintingViewer.IsBusy || EndingBook.IsShowing || IntroPanel.IsOpen || FloorChoiceUI.IsOpen) return;
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

        var ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out var hit, clickDistance, ~0, QueryTriggerInteraction.Collide)) return;
        var pointerTarget = PointerTargetLink.Find(hit.collider);
        if (pointerTarget != null) { pointerTarget.OnPointerClick(player.position); return; }
        var target = hit.collider.GetComponentInParent<QuizClickTarget>();
        if (target != null) target.Click();
    }

    // --- FloorDoor에서 호출 (퀴즈 없이 문으로 층 이동) ---

    // 이 층의 계단실 문으로 갈 수 있는 층: 바로 위층(먼저)과 바로 아래층 — 1층은 2층만, 2층은 3층·1층, 3층은 2층만
    public List<FloorStage> Destinations(FloorStage stage)
    {
        var list = new List<FloorStage>();
        int idx = stages.IndexOf(stage);
        if (idx < 0) return list;
        if (idx < stages.Count - 1) list.Add(stages[idx + 1]);
        if (idx > 0) list.Add(stages[idx - 1]);
        return list;
    }

    // 문 앞 안내 문구용
    public string DestinationText(FloorStage stage)
    {
        var d = Destinations(stage);
        if (d.Count == 1) return $"{d[0].floorNumber}층으로 이동";
        return "층 이동  (" + string.Join(" / ", d.ConvertAll(s => s.floorNumber + "층")) + ")";
    }

    // 계단실 문을 열었을 때: 갈 곳이 하나면 바로 이동, 여러 개면 고르는 창을 띄운다
    public void PassDoor(FloorStage stage)
    {
        if (IsTeleporting || FloorChoiceUI.IsOpen) return;
        var dests = Destinations(stage);
        if (dests.Count == 0) return;
        if (dests.Count == 1) { GoToFloor(stage, dests[0]); return; }

        var options = new List<FloorChoiceUI.Option>();
        foreach (var d in dests)
        {
            var target = d;
            string verb = d.floorNumber > stage.floorNumber ? "올라가기" : "내려가기";
            options.Add(new FloorChoiceUI.Option($"{d.floorNumber}층으로 {verb}", () => GoToFloor(stage, target)));
        }
        FloorChoiceUI.Show(this, "어디로 갈까요?", options);
    }

    public void GoToFloor(FloorStage from, FloorStage to)
    {
        if (IsTeleporting || to == null) return;
        from.MarkSolved();
        if (!to.IsUnlocked)
        {
            to.SetUnlocked(true);
            onFloorUnlocked?.Invoke(to.floorNumber);
        }
        GameSfx.PlayDoor();   // 계단실 문을 열고 나가는 소리 (화면이 어두워지는 동안)
        var point = to.stairArrival != null ? to.stairArrival : to.spawnPoint;
        StartCoroutine(TeleportRoutine(point, $"{to.floorNumber}층", 0f));
    }

    // --- FloorStage에서 호출 ---

    public void NotifyExplorationComplete(FloorStage stage)
    {
        if (stage.IsSolved || stage.Quiz == null) return;
        UI.Toast("탐색 완료! 퀴즈 창이 열렸어요.", 3f);
        UI.Open(stage.Quiz);
    }

    public void NotifyStageSolved(FloorStage stage)
    {
        int idx = stages.IndexOf(stage);
        bool last = idx == stages.Count - 1;

        if (last)
        {
            // 1층 출입문 안쪽으로 이동 → 출입문 밖(ExitZone)으로 나가면 클리어
            UI.Celebrate("정답!\n이제 탈출할 수 있어요.");
            StartCoroutine(TeleportRoutine(finalSpawnPoint, "1층 출입문으로 나가 미술관을 빠져나가세요."));
            return;
        }

        var next = stages[idx + 1];
        next.SetUnlocked(true);
        UI.Celebrate($"정답!\n{next.floorNumber}층으로 이동해요.");
        onFloorUnlocked?.Invoke(next.floorNumber);
        StartCoroutine(TeleportRoutine(next.spawnPoint, $"{next.floorNumber}층 — {next.Quiz?.Title}. 그림을 둘러보고 계단실 문에서 [E]를 누르세요."));
    }

    // 이미 푼 층의 문에서 E를 다시 눌렀을 때: 다음 층으로 이동만
    public void GoToNextFloor(FloorStage solved)
    {
        int idx = stages.IndexOf(solved);
        var target = idx == stages.Count - 1 ? finalSpawnPoint : stages[idx + 1].spawnPoint;
        StartCoroutine(TeleportRoutine(target, null, 0f));
    }

    // 화면을 어둡게 → 플레이어 위치·방향 이동 → 다시 밝게
    System.Collections.IEnumerator TeleportRoutine(Transform target, string arrivalToast, float delay = -1f)
    {
        if (target == null || player == null) yield break;
        while (IsTeleporting) yield return null;   // 앞선 이동이 끝날 때까지 기다렸다가 이어서
        IsTeleporting = true;
        yield return new WaitForSecondsRealtime(delay < 0f ? teleportDelay : delay);

        UI.Close();
        yield return UI.Fade(1f, fadeTime);

        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;               // CharacterController가 위치 변경을 덮어쓰지 않게
        player.SetPositionAndRotation(target.position, Quaternion.Euler(0f, target.eulerAngles.y, 0f));
        if (VRRig.IsActiveNow) VRRig.Current.AlignHeadTo(target.position, target.eulerAngles.y);   // 방 안에서 걸어 다닌 만큼 어긋난 머리 위치 보정
        else if (playerCamera != null) ResetPitch();
        if (cc != null) cc.enabled = true;
        UpdateCurrentStage();

        yield return new WaitForSecondsRealtime(0.15f);
        yield return UI.Fade(0f, fadeTime);
        IsTeleporting = false;
        if (!string.IsNullOrEmpty(arrivalToast)) UI.Toast(arrivalToast, 5f);
    }

    // 카메라 상하 각도를 수평으로 (MouseLook의 내부 각도 값도 함께)
    void ResetPitch()
    {
        playerCamera.transform.localRotation = Quaternion.identity;
        foreach (var b in playerCamera.GetComponents<MonoBehaviour>())
        {
            var f = b != null ? b.GetType().GetField("xRotation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) : null;
            if (f != null && f.FieldType == typeof(float)) f.SetValue(b, 0f);
        }
    }

    // 최종 출구 문을 열었을 때 (InteractDoor.isExit)
    public void CompleteGame()
    {
        UI.Celebrate("탈출 성공!\n미술관 밖으로 나왔어요.");
        onGameCleared?.Invoke();
    }

    // 퀴즈 창이 열리면 이동·시점을 멈추고 커서를 보여 준다
    void SetPlayerControl(bool uiOpen)
    {
        foreach (var b in disableWhileUIOpen) if (b != null) b.enabled = !uiOpen;
        Cursor.lockState = uiOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = uiOpen;
    }
}
