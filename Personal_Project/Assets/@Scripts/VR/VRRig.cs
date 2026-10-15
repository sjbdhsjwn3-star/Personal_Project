using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR;

// VR 플레이어 리그. 헤드셋이 켜져 있으면 실행 중 자동으로 VR 모드로 바뀐다.
//  - 왼쪽 스틱 : 이동 (바라보는 방향 기준)
//  - 오른쪽 스틱: 방향 전환 (스냅 / 부드럽게)
//  - 양손 레이저 + 트리거(또는 A·X): 문 열기, World Space UI 버튼 클릭
// 헤드셋이 없으면 아무것도 하지 않으므로 키보드·마우스 플레이가 그대로 동작한다.
public class VRRig : MonoBehaviour
{
    public static VRRig Current { get; private set; }
    public static bool IsActiveNow => Current != null && Current.Active;
    public static event Action<VRRig> Activated;

    [Header("References")]
    public Camera head;
    [Tooltip("이동에 쓸 CharacterController (비우면 이동 없음 — 메인 메뉴 등)")]
    public CharacterController body;
    [Tooltip("VR 모드에서 끌 데스크톱 전용 컴포넌트 (마우스 시점, 키보드 이동 등)")]
    public List<Behaviour> disableInVR = new List<Behaviour>();

    [Header("Move — 왼쪽 스틱")]
    [Range(0.5f, 6f)] public float moveSpeed = 2.5f;
    public float gravity = -15f;
    [Range(0f, 0.5f)] public float deadZone = 0.15f;

    [Header("Turn — 오른쪽 스틱")]
    [Tooltip("켜면 한 번에 snapAngle만큼 회전 (멀미가 덜함), 끄면 스틱을 민 만큼 부드럽게 회전")]
    public bool snapTurn = false;
    [Range(15f, 90f)] public float snapAngle = 45f;
    [Tooltip("스틱을 끝까지 밀었을 때의 회전 속도 (도/초)")]
    [Range(30f, 360f)] public float smoothTurnSpeed = 150f;
    [Tooltip("1 = 민 만큼 그대로, 클수록 살짝 밀 때 더 느리고 끝까지 밀 때 빨라진다")]
    [Range(1f, 3f)] public float turnCurve = 1.8f;
    [Tooltip("회전 속도가 바뀔 때 따라붙는 빠르기 (클수록 즉각적)")]
    [Range(2f, 30f)] public float turnResponse = 10f;

    [Header("Pointer — 트리거로 클릭")]
    [Range(1f, 30f)] public float pointerLength = 8f;
    public Color pointerColor = new Color(1f, 1f, 1f, 0.35f);
    public Color pointerHoverColor = new Color(1f, 0.85f, 0.4f, 1f);

    [Header("Height")]
    [Tooltip("body가 없을 때(메뉴 등) 리그 위치를 눈높이로 보고 바닥을 이만큼 아래에 둔다. 바닥 기준 추적이 안 될 때의 눈높이로도 쓴다")]
    public float defaultEyeHeight = 1.7f;
    [Tooltip("켜면 실제 키·자세와 상관없이 시작할 때 눈높이를 defaultEyeHeight에 맞춘다 (앉아서 해도 성인 눈높이)")]
    public bool forceEyeHeight = true;

    public bool Active { get; private set; }
    public Transform CameraOffset { get; private set; }

    // 리그 기준 바닥 높이 (body가 있으면 캡슐 바닥)
    float FeetOffset => body != null ? body.center.y - body.height * 0.5f : -defaultEyeHeight;
    // 헤드셋 추적 전, 머리가 있을 것으로 예상되는 위치 (UI 판 배치용)
    public Vector3 ExpectedHeadPosition => transform.position + Vector3.up * (FeetOffset + defaultEyeHeight);

    class Hand
    {
        public Transform t;
        public LineRenderer line;
        public InputAction click;
        public Button hovered;
    }

    readonly List<Hand> hands = new List<Hand>();
    readonly RaycastHit[] hits = new RaycastHit[16];
    InputAction moveAction, turnAction;
    Button[] buttons = new Button[0];
    float nextButtonScan, fallSpeed, bodyCenterY, turnVelocity, calibrateAt = -1f;
    bool turnLatched;

    void Awake() => Current = this;

    void OnDestroy()
    {
        if (Current == this) Current = null;
        moveAction?.Dispose();
        turnAction?.Dispose();
        foreach (var h in hands) h.click.Dispose();
    }

    void Update()
    {
        if (!Active)
        {
            if (XRSettings.isDeviceActive) Activate();
            return;
        }

        var m = GameLoopManager.Instance;
        bool frozen = (m != null && (m.IsTeleporting || m.UI.IsOpen)) || PaintingViewer.IsBusy;
        CalibrateEyeHeight();
        FollowHead();
        if (!frozen) { Move(); Turn(); }
        else turnVelocity = 0f;
        foreach (var h in hands) UpdatePointer(h, frozen);
    }

    // ---------- 전환 ----------

    void Activate()
    {
        Active = true;
        foreach (var b in disableInVR) if (b != null) b.enabled = false;
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;   // 플레이어 캡슐 등 몸 모양 숨김

        // 바닥 기준 추적 (실제 키가 그대로 반영됨)
        bool floor = false;
        var subsystems = new List<XRInputSubsystem>();
        SubsystemManager.GetSubsystems(subsystems);
        foreach (var s in subsystems)
            floor |= s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor) && s.GetTrackingOriginMode() == TrackingOriginModeFlags.Floor;

        CameraOffset = new GameObject("XR Camera Offset").transform;
        CameraOffset.SetParent(transform, false);
        CameraOffset.localPosition = Vector3.up * (floor ? FeetOffset : FeetOffset + defaultEyeHeight);

        head.transform.SetParent(CameraOffset, false);
        head.transform.localPosition = Vector3.zero;
        head.transform.localRotation = Quaternion.identity;
        head.nearClipPlane = 0.05f;
        AddPoseDriver(head.gameObject, "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation");

        hands.Add(CreateHand("LeftHand"));
        hands.Add(CreateHand("RightHand"));

        moveAction = new InputAction("Move", InputActionType.Value, "<XRController>{LeftHand}/{Primary2DAxis}", expectedControlType: "Vector2");
        turnAction = new InputAction("Turn", InputActionType.Value, "<XRController>{RightHand}/{Primary2DAxis}", expectedControlType: "Vector2");
        moveAction.Enable();
        turnAction.Enable();

        if (body != null) bodyCenterY = body.center.y;
        Activated?.Invoke(this);
    }

    static void AddPoseDriver(GameObject go, string position, string rotation)
    {
        var tpd = go.GetComponent<TrackedPoseDriver>();
        if (tpd == null) tpd = go.AddComponent<TrackedPoseDriver>();
        tpd.enabled = false;
        tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        tpd.positionInput = new InputActionProperty(new InputAction(binding: position, expectedControlType: "Vector3"));
        tpd.rotationInput = new InputActionProperty(new InputAction(binding: rotation, expectedControlType: "Quaternion"));
        tpd.enabled = true;
    }

    Hand CreateHand(string side)
    {
        var go = new GameObject(side);
        go.SetActive(false);
        go.transform.SetParent(CameraOffset, false);
        string device = "<XRController>{" + side + "}";
        AddPoseDriver(go, device + "/pointerPosition", device + "/pointerRotation");

        // 컨트롤러 자리 표시용 작은 막대
        var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(model.GetComponent<Collider>());
        model.name = "Model";
        model.transform.SetParent(go.transform, false);
        model.transform.localScale = new Vector3(0.03f, 0.03f, 0.12f);
        model.transform.localPosition = new Vector3(0f, 0f, -0.06f);
        model.GetComponent<Renderer>().material.color = new Color(0.15f, 0.15f, 0.15f);

        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = 0.006f;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.material = new Material(Shader.Find("Sprites/Default"));

        var click = new InputAction(side + "Select", InputActionType.Button);
        click.AddBinding(device + "/{TriggerButton}");
        click.AddBinding(device + "/{PrimaryButton}");   // A / X 버튼으로도 클릭
        click.Enable();

        go.SetActive(true);
        return new Hand { t = go.transform, line = line, click = click };
    }

    // ---------- 이동 / 회전 ----------

    // 방 안에서 실제로 걸어도 몸(캡슐)이 머리 아래를 따라오게
    void FollowHead()
    {
        if (body == null) return;
        var local = transform.InverseTransformPoint(head.transform.position);
        body.center = new Vector3(local.x, bodyCenterY, local.z);
    }

    void Move()
    {
        if (body == null || !body.enabled) return;
        var input = moveAction.ReadValue<Vector2>();
        if (input.magnitude < deadZone) input = Vector2.zero;
        input = Vector2.ClampMagnitude(input, 1f);

        var yaw = Quaternion.Euler(0f, head.transform.eulerAngles.y, 0f);
        var move = yaw * new Vector3(input.x, 0f, input.y) * moveSpeed;
        fallSpeed = body.isGrounded ? -2f : fallSpeed + gravity * Time.deltaTime;
        body.Move((move + Vector3.up * fallSpeed) * Time.deltaTime);
    }

    void Turn()
    {
        float x = turnAction.ReadValue<Vector2>().x;
        if (snapTurn)
        {
            if (!turnLatched && Mathf.Abs(x) > 0.7f) { RotateAroundHead(Mathf.Sign(x) * snapAngle); turnLatched = true; }
            else if (Mathf.Abs(x) < 0.3f) turnLatched = false;
        }
        else
        {
            // 데드존 밖에서 0→1로 다시 매핑한 뒤 곡선 적용: 살짝 밀면 천천히, 끝까지 밀면 최고 속도
            float a = Mathf.Clamp01((Mathf.Abs(x) - deadZone) / (1f - deadZone));
            float target = Mathf.Sign(x) * Mathf.Pow(a, turnCurve) * smoothTurnSpeed;
            turnVelocity = Mathf.Lerp(turnVelocity, target, 1f - Mathf.Exp(-turnResponse * Time.deltaTime));
            if (Mathf.Abs(turnVelocity) > 0.5f) RotateAroundHead(turnVelocity * Time.deltaTime);
        }
    }

    // 머리 추적이 시작되고 잠시 뒤 한 번, 눈높이를 defaultEyeHeight로 맞춘다 (이후 숙이기·앉기는 그대로 반영)
    void CalibrateEyeHeight()
    {
        if (!forceEyeHeight || calibrateAt == float.MaxValue) return;
        if (calibrateAt < 0f)
        {
            if (head.transform.localPosition != Vector3.zero) calibrateAt = Time.time + 0.5f;   // 추적 시작 감지
            return;
        }
        if (Time.time < calibrateAt) return;
        RecalibrateEyeHeight();
        calibrateAt = float.MaxValue;
    }

    [ContextMenu("Recalibrate Eye Height")]
    public void RecalibrateEyeHeight()
    {
        if (CameraOffset == null) return;
        var p = CameraOffset.localPosition;
        p.y = FeetOffset + defaultEyeHeight - head.transform.localPosition.y;
        CameraOffset.localPosition = p;
    }

    // 머리 위치를 축으로 회전 (회전할 때 몸이 옆으로 밀리지 않게)
    void RotateAroundHead(float angle)
    {
        var pivot = head.transform.position;
        var rot = Quaternion.Euler(0f, angle, 0f);
        var newPos = pivot + rot * (transform.position - pivot);
        transform.rotation = rot * transform.rotation;
        var delta = newPos - transform.position;
        delta.y = 0f;
        if (body != null && body.enabled) body.Move(delta);
        else transform.position += delta;
    }

    // 순간이동 직후: 머리가 목표 지점·방향에 오도록 리그를 맞춘다 (GameLoopManager에서 호출)
    public void AlignHeadTo(Vector3 position, float yaw)
    {
        var headForward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up);
        float headYaw = Vector3.SignedAngle(transform.forward, headForward, Vector3.up);
        transform.rotation = Quaternion.Euler(0f, yaw - headYaw, 0f);
        var h = head.transform.position;
        transform.position += new Vector3(position.x - h.x, 0f, position.z - h.z);
    }

    // ---------- 레이저 ----------

    void UpdatePointer(Hand h, bool frozen)
    {
        var ray = new Ray(h.t.position, h.t.forward);
        float dist = pointerLength;
        IPointerTarget target = null;

        var button = frozen ? null : FindButton(ray, ref dist);
        if (RaycastWorld(ray, dist, out var hit))
        {
            dist = hit.distance;
            button = null;
            target = frozen ? null : PointerTargetLink.Find(hit.collider);
        }
        SetHover(h, button);

        bool hover = target != null || button != null;
        var c = hover ? pointerHoverColor : pointerColor;
        h.line.startColor = c;
        h.line.endColor = hover ? c : new Color(c.r, c.g, c.b, 0f);
        h.line.SetPosition(0, ray.origin);
        h.line.SetPosition(1, ray.GetPoint(dist));

        if (frozen || !h.click.WasPressedThisFrame()) return;
        if (button != null) button.onClick.Invoke();
        else if (target != null) target.OnPointerClick(transform.position);
    }

    // 플레이어 자신의 콜라이더는 건너뛰고 가장 가까운 물체
    bool RaycastWorld(Ray ray, float maxDist, out RaycastHit nearest)
    {
        nearest = default;
        int n = Physics.RaycastNonAlloc(ray, hits, maxDist, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (hits[i].transform.IsChildOf(transform) || hits[i].distance >= best) continue;
            best = hits[i].distance;
            nearest = hits[i];
        }
        return best < float.MaxValue;
    }

    // World Space 캔버스의 버튼 중 레이저가 닿는 것
    Button FindButton(Ray ray, ref float dist)
    {
        if (Time.unscaledTime >= nextButtonScan)
        {
            buttons = FindObjectsByType<Button>(FindObjectsSortMode.None);
            nextButtonScan = Time.unscaledTime + 1f;
        }

        Button best = null;
        foreach (var b in buttons)
        {
            if (b == null || !b.isActiveAndEnabled || !b.interactable) continue;
            var canvas = b.GetComponentInParent<Canvas>();
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace) continue;

            var rt = (RectTransform)b.transform;
            if (!new Plane(rt.forward, rt.position).Raycast(ray, out float d) || d > dist) continue;
            Vector2 local = rt.InverseTransformPoint(ray.GetPoint(d));
            if (!rt.rect.Contains(local)) continue;
            best = b;
            dist = d;
        }
        return best;
    }

    static void SetHover(Hand h, Button b)
    {
        if (h.hovered == b) return;
        if (h.hovered != null) h.hovered.transform.localScale = Vector3.one;
        h.hovered = b;
        if (b != null) b.transform.localScale = Vector3.one * 1.08f;
    }
}
