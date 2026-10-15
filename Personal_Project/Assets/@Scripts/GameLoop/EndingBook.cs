using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 엔딩 책(3층 나가는 문 앞의 책 받침대): 가까이 가서 E/F(데스크톱 조준점 클릭) 또는 VR 레이저 + 트리거로 상호작용하면
// '관람 끝' 엔딩 패널을 띄우고 게임 클리어 처리 (BGM 페이드아웃, onGameCleared 호출)
public class EndingBook : MonoBehaviour, IPointerTarget
{
    [Header("Interaction")]
    public Key interactKey = Key.E;
    public Key altInteractKey = Key.F;
    [Tooltip("이 거리(수평) 안에서만 상호작용 — VR 레이저도 동일")]
    [Range(0.5f, 6f)] public float interactRadius = 2.5f;
    [Range(0.5f, 5f)] public float maxHeightDiff = 2.5f;
    [Tooltip("이 층에 와 있어야 상호작용 가능 (비우면 항상)")]
    public FloorStage requiredStage;

    [Header("Text")]
    public string promptDesktop = "[ E ]  방명록 펼치기";
    public string promptVR = "트리거  —  방명록 펼치기";
    public string title = "게임 종료";
    [TextArea(2, 4)] public string message = "Monochrome Gallery의 모든 관람을 마쳤습니다.\n찾아 주셔서 감사합니다.";
    public string mainMenuLabel = "메인 메뉴로";
    public string quitLabel = "종료하기";

    [Header("Ending")]
    [Tooltip("'메인 메뉴로' 버튼이 불러올 씬 (Build Profiles에 등록되어 있어야 함)")]
    public string mainMenuScene = "MainMenu";
    [Tooltip("패널 배경 종이 (시작 안내와 같은 ui_ticket_blank). 비우면 단색 종이")]
    public Sprite ticket;
    [Tooltip("엔딩 화면이 뜰 때 나오는 음악 (층 BGM은 멈춘다)")]
    public AudioClip endingMusic;
    [Range(0f, 1f)] public float endingVolume = 0.8f;
    [Tooltip("켜면 엔딩 음악을 반복 재생")]
    public bool loopEndingMusic = false;
    public UnityEvent onEnding;

    public static bool IsShowing { get; private set; }

    static readonly Color Paper = new Color(0.93f, 0.9f, 0.84f, 1f);
    static readonly Color Ink = new Color(0.1f, 0.07f, 0.05f, 1f);   // 시작 안내(IntroPanel)와 같은 잉크색

    Canvas canvas;
    CanvasGroup group;
    bool promptShown, done;

    void Awake()
    {
        // 레이저·조준점이 맞을 콜라이더 (없으면 전체 크기로 하나 만든다)
        var col = GetComponentInChildren<Collider>();
        if (col == null)
        {
            var b = new Bounds(transform.position, Vector3.zero);
            foreach (var r in GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = transform.InverseTransformPoint(b.center);
            var s = transform.lossyScale;
            box.size = new Vector3(b.size.x / s.x, b.size.y / s.y, b.size.z / s.z);
            col = box;
        }
        PointerTargetLink.Attach(col.gameObject, this);
    }

    void OnDestroy() => IsShowing = false;

    Vector3 Center
    {
        get
        {
            var r = GetComponentInChildren<Renderer>();
            return r != null ? r.bounds.center : transform.position;
        }
    }

    bool IsNear(Vector3 p)
    {
        var c = Center;
        return new Vector2(p.x - c.x, p.z - c.z).magnitude <= interactRadius && Mathf.Abs(p.y - c.y) <= maxHeightDiff;
    }

    bool Available(GameLoopManager m) =>
        !done && !m.UI.IsOpen && !m.IsTeleporting && !PaintingViewer.IsBusy &&
        (requiredStage == null || m.CurrentStage == requiredStage);

    void Update()
    {
        var m = GameLoopManager.Instance;
        if (m == null || m.player == null) return;

        bool near = Available(m) && IsNear(m.player.position);
        if (near != promptShown)
        {
            promptShown = near;
            if (near) m.UI.ShowPrompt(VRRig.IsActiveNow ? promptVR : promptDesktop);
            else m.UI.HidePrompt();
        }
        if (!near) return;

        var kb = Keyboard.current;
        if (kb != null && (kb[interactKey].wasPressedThisFrame || kb[altInteractKey].wasPressedThisFrame)) ShowEnding();
    }

    public void OnPointerClick(Vector3 playerPosition)
    {
        var m = GameLoopManager.Instance;
        if (m == null || !Available(m)) return;
        if (!IsNear(m.player.position)) { m.UI.Toast("책 가까이 가서 펼쳐 주세요", 2f); return; }
        ShowEnding();
    }

    public void ShowEnding()
    {
        if (done) return;
        done = true;
        IsShowing = true;
        var m = GameLoopManager.Instance;
        if (promptShown) { promptShown = false; m.UI.HidePrompt(); }

        if (BGMManager.Instance != null) BGMManager.Instance.StopAll();
        if (endingMusic != null)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 0f;   // 2D
            if (loopEndingMusic) { src.clip = endingMusic; src.volume = endingVolume; src.loop = true; src.Play(); }
            else src.PlayOneShot(endingMusic, endingVolume);
        }
        if (m != null)
        {
            m.onGameCleared?.Invoke();
            // 데스크톱: 이동·시점을 멈추고 커서를 보여 줘 버튼을 누를 수 있게
            if (!VRRig.IsActiveNow)
            {
                foreach (var b in m.disableWhileUIOpen) if (b != null) b.enabled = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
        onEnding?.Invoke();

        BuildUI();
        StartCoroutine(FadeIn());
    }

    System.Collections.IEnumerator FadeIn()
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.8f)
        {
            group.alpha = t;
            yield return null;
        }
        group.alpha = 1f;
    }

    void GoToMainMenu()
    {
        IsShowing = false;
        SceneManager.LoadScene(mainMenuScene);
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------- UI ----------

    void BuildUI()
    {
        var go = new GameObject("EndingCanvas", typeof(RectTransform));
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        var font = QuizUI.LoadKoreanFont(64);

        var backdrop = MakeImage(go.transform, "Backdrop", new Color(0f, 0f, 0f, 0.85f));
        var brt = backdrop.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;

        Image panel;
        if (ticket != null)
        {
            // 시작 안내와 같은 티켓 종이 (원본 914x520 비율)
            panel = MakeImage(go.transform, "Ticket", Color.white);
            panel.sprite = ticket;
            panel.preserveAspect = true;
            panel.rectTransform.sizeDelta = new Vector2(1280, 728);
        }
        else
        {
            panel = MakeImage(go.transform, "Panel", Paper);
            panel.rectTransform.sizeDelta = new Vector2(1100, 620);
            var line = MakeImage(panel.transform, "Line", Ink);
            line.rectTransform.sizeDelta = new Vector2(1040, 560);
            var inner = MakeImage(line.transform, "Inner", Paper);
            inner.rectTransform.sizeDelta = new Vector2(1032, 552);
        }

        MakeText(panel.transform, title, font, 96, new Vector2(0, 120), new Vector2(1000, 140));
        MakeText(panel.transform, message, font, 34, new Vector2(0, -15), new Vector2(980, 120));
        MakeButton(panel.transform, mainMenuLabel, font, new Vector2(-180, -180), GoToMainMenu);
        MakeButton(panel.transform, quitLabel, font, new Vector2(180, -180), Quit);

        // VR: 화면 오버레이는 헤드셋에 안 보이므로 눈앞 판으로 세운다 (VRRig 레이저로 버튼 클릭)
        if (VRRig.IsActiveNow)
        {
            var head = VRRig.Current.head;
            // 벽보다 가까이 세워야 레이저가 벽에 막히지 않고 버튼에 닿는다
            XRCanvasAdapter.ToWorldSpace(canvas, head, 1.6f);
            var fwd = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
            go.transform.SetPositionAndRotation(head.transform.position + fwd * 1.2f, Quaternion.LookRotation(fwd));
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(40000f, 40000f);   // 주변을 어둡게
        }
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

    static Text MakeText(Transform parent, string text, Font font, int size, Vector2 pos, Vector2 box)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.text = text;
        t.font = font;
        t.fontSize = size;
        t.color = Ink;
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;
        t.rectTransform.anchoredPosition = pos;
        t.rectTransform.sizeDelta = box;
        // 폰트가 가늘어서 같은 색 외곽선으로 굵게
        var outline = go.AddComponent<Outline>();
        outline.effectColor = Ink;
        outline.effectDistance = new Vector2(size / 50f, -size / 50f);
        return t;
    }

    static void MakeButton(Transform parent, string label, Font font, Vector2 pos, UnityAction onClick)
    {
        var img = MakeImage(parent, label, Ink);
        img.raycastTarget = true;
        img.rectTransform.anchoredPosition = pos;
        img.rectTransform.sizeDelta = new Vector2(320, 90);
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(onClick);
        var t = MakeText(img.transform, label, font, 36, Vector2.zero, new Vector2(320, 90));
        t.color = Paper;
        t.GetComponent<Outline>().effectColor = Paper;
    }
}
