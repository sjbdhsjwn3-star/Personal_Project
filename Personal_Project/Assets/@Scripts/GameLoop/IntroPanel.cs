using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 게임 시작 안내: 씬이 시작되면 티켓 이미지(ui_ticket_blank) 가운데에 게임 목표·조작법을 적어 띄우고, 뒤는 어둡게 가린다.
//  - 닫기: '관람 시작' 버튼 (데스크톱 클릭 / VR 레이저 + 트리거), 또는 Enter / Space / E
public class IntroPanel : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    [Tooltip("배경 티켓 이미지 (ui_ticket_blank)")]
    public Sprite ticket;

    [Header("Text")]
    public string title = "관람 안내";
    [TextArea(1, 3)] public string goal = "게임목표는 3층까지 관람하는 것";
    [TextArea(2, 6)] public string guideDesktop = "WASD 이동 · 마우스 시점\n그림 클릭 : 크게 보기 · E : 문 열기";
    [TextArea(2, 6)] public string guideVR = "왼쪽 스틱 이동 · 오른쪽 스틱 회전\n트리거 : 그림 보기 · 문 열기";
    public string startLabel = "관람 시작";

    [Header("Look")]
    [Range(0f, 1f)] public float backdropAlpha = 0.85f;
    public Color ink = new Color(0.1f, 0.07f, 0.05f, 1f);

    Canvas canvas;
    Text guideText;
    CanvasGroup group;

    void Start()
    {
        Build();
        Open();
        VRRig.Activated += OnVRActivated;
        if (VRRig.IsActiveNow) OnVRActivated(VRRig.Current);
    }

    void OnDestroy()
    {
        VRRig.Activated -= OnVRActivated;
        IsOpen = false;
    }

    void Update()
    {
        if (!IsOpen) return;
        // GameLoopManager.Start 등이 이동·커서를 다시 켜면 되돌린다
        if (!VRRig.IsActiveNow && Cursor.lockState != CursorLockMode.None) SetDesktopControl(false);
        var kb = Keyboard.current;
        if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)) Close();
    }

    public void Open()
    {
        IsOpen = true;
        canvas.gameObject.SetActive(true);
        SetDesktopControl(false);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        canvas.gameObject.SetActive(false);
        SetDesktopControl(true);
    }

    // 데스크톱: 안내가 떠 있는 동안 이동·시점 정지 + 커서 표시
    void SetDesktopControl(bool on)
    {
        var m = GameLoopManager.Instance;
        if (m == null || VRRig.IsActiveNow) return;
        foreach (var b in m.disableWhileUIOpen) if (b != null) b.enabled = on;
        Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !on;
    }

    // VR: 화면 오버레이는 헤드셋에 안 보이므로 눈앞 판으로 세운다 (VRRig 레이저로 버튼 클릭)
    void OnVRActivated(VRRig rig)
    {
        guideText.text = guideVR;
        XRCanvasAdapter.ToWorldSpace(canvas, rig.head, 1.6f);
        var fwd = Vector3.ProjectOnPlane(rig.transform.forward, Vector3.up).normalized;
        canvas.transform.SetPositionAndRotation(rig.ExpectedHeadPosition + fwd * 1.2f, Quaternion.LookRotation(fwd));
        var backdrop = (RectTransform)canvas.transform.Find("Backdrop");
        backdrop.anchorMin = backdrop.anchorMax = new Vector2(0.5f, 0.5f);
        backdrop.sizeDelta = new Vector2(40000f, 40000f);   // 주변을 어둡게
    }

    // ---------- UI ----------

    void Build()
    {
        var go = new GameObject("IntroCanvas", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();

        var font = QuizUI.LoadKoreanFont(64);

        var backdrop = MakeImage(go.transform, "Backdrop", new Color(0f, 0f, 0f, backdropAlpha));
        var brt = backdrop.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
        backdrop.raycastTarget = true;   // 뒤의 월드를 클릭하지 못하게

        // 티켓 (원본 914x520 비율)
        var card = MakeImage(go.transform, "Ticket", Color.white);
        card.sprite = ticket;
        card.preserveAspect = true;
        card.rectTransform.sizeDelta = new Vector2(1280, 728);

        // 티켓 안쪽 테두리 가운데에 내용 배치
        MakeText(card.transform, title, font, 40, new Vector2(0, 175), new Vector2(900, 60), new Color(ink.r, ink.g, ink.b, 0.75f));
        MakeText(card.transform, goal, font, 62, new Vector2(0, 85), new Vector2(980, 100), ink);
        var line = MakeImage(card.transform, "Line", new Color(ink.r, ink.g, ink.b, 0.5f));
        line.rectTransform.anchoredPosition = new Vector2(0, 20);
        line.rectTransform.sizeDelta = new Vector2(520, 2);
        guideText = MakeText(card.transform, VRRig.IsActiveNow ? guideVR : guideDesktop, font, 32, new Vector2(0, -60), new Vector2(980, 120), new Color(ink.r, ink.g, ink.b, 0.9f));

        var btn = MakeImage(card.transform, startLabel, ink);
        btn.raycastTarget = true;
        btn.rectTransform.anchoredPosition = new Vector2(0, -190);
        btn.rectTransform.sizeDelta = new Vector2(300, 84);
        var b = btn.gameObject.AddComponent<Button>();
        b.targetGraphic = btn;
        b.onClick.AddListener(Close);
        MakeText(btn.transform, startLabel, font, 36, Vector2.zero, new Vector2(300, 84), new Color(0.95f, 0.91f, 0.84f, 1f));
    }

    static Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static Text MakeText(Transform parent, string text, Font font, int size, Vector2 pos, Vector2 box, Color color)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.text = text;
        t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;
        t.rectTransform.anchoredPosition = pos;
        t.rectTransform.sizeDelta = box;
        // 폰트가 가늘어서 같은 색 외곽선으로 굵게
        var outline = go.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(size / 50f, -size / 50f);
        return t;
    }
}
