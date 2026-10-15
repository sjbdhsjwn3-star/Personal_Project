using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 그림 크게 보기
//  - VR: 레이저로 그림을 가리키고 트리거 / 데스크톱: 조준점으로 그림 클릭
//  - 그림이 벽에서 눈앞으로 날아오며 확대되고, 주변은 어둡게 가려진다
//  - 닫기: VR 트리거 / B·Y 버튼, 데스크톱 클릭 / E / Q
// 실행 시 texturePrefix로 시작하는 텍스처를 쓴 물체를 모두 찾아 클릭 가능하게 만든다 (그림을 새로 걸어도 자동 적용).
public class PaintingViewer : MonoBehaviour
{
    public static PaintingViewer Instance { get; private set; }
    // 열려 있거나 방금 닫힌 프레임 (닫는 클릭이 문·다른 그림까지 눌러 버리지 않게)
    public static bool IsBusy => Instance != null && (Instance.IsOpen || Time.frameCount <= Instance.closedFrame + 1);

    [Header("Find paintings")]
    [Tooltip("이 이름으로 시작하는 텍스처를 쓴 물체를 그림으로 본다")]
    public string texturePrefix = "picture";

    [Header("Zoom")]
    [Tooltip("눈앞 거리 (m)")]
    [Range(0.5f, 3f)] public float distance = 1.1f;
    [Tooltip("확대된 그림의 최대 높이 (m)")]
    [Range(0.2f, 3f)] public float maxHeight = 0.9f;
    [Tooltip("확대된 그림의 최대 폭 (m)")]
    [Range(0.2f, 3f)] public float maxWidth = 1.3f;
    [Range(0.05f, 2f)] public float zoomTime = 0.35f;
    [Range(0f, 1f)] public float backdropAlpha = 0.8f;

    [Header("Text")]
    public string hintVR = "트리거 / B : 닫기";
    public string hintDesktop = "클릭 / E : 닫기";

    public bool IsOpen { get; private set; }

    const float Unit = 1000f;   // 캔버스 1000px = 그림 높이
    Canvas canvas;
    RawImage image;
    Image border, backdrop;
    Text hint;
    InputAction closeAction;
    int openedFrame, closedFrame = -10;
    Vector3 startPos, endPos;
    float startScale, endScale;
    Coroutine anim;
    bool controlLocked;

    void Awake()
    {
        Instance = this;
        BuildUI();
        closeAction = new InputAction("ClosePainting", InputActionType.Button);
        closeAction.AddBinding("<XRController>/{TriggerButton}");
        closeAction.AddBinding("<XRController>/{SecondaryButton}");   // B / Y
        closeAction.AddBinding("<Mouse>/leftButton");
        closeAction.AddBinding("<Keyboard>/e");
        closeAction.AddBinding("<Keyboard>/q");
        closeAction.Enable();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        closeAction?.Dispose();
    }

    void Start() => RegisterPaintings();

    void Update()
    {
        if (IsOpen && Time.frameCount > openedFrame && closeAction.WasPressedThisFrame()) Close();
    }

    public void RegisterPaintings()
    {
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var tex = r.sharedMaterial != null ? r.sharedMaterial.mainTexture : null;
            if (tex == null || !tex.name.StartsWith(texturePrefix)) continue;
            if (r.GetComponentInParent<PaintingEscape>() != null || r.name == "PaintingRipple") continue;   // 그림 탈출 연출의 물결 판은 그림이 아님

            var go = r.gameObject;
            if (go.GetComponent<Collider>() == null)
            {
                if (go.GetComponent<MeshFilter>() != null) go.AddComponent<MeshCollider>();
                else go.AddComponent<BoxCollider>();
            }
            var target = go.GetComponent<PaintingTarget>();
            if (target == null) target = go.AddComponent<PaintingTarget>();
            target.picture = r;
            PointerTargetLink.Attach(go, target);
        }
    }

    public void Show(PaintingTarget target)
    {
        if (IsBusy || target == null || target.Texture == null) return;
        var cam = ViewCamera();
        if (cam == null) return;

        IsOpen = true;
        openedFrame = Time.frameCount;

        var ws = target.WorldSize;
        float aspect = ws.y > 0.0001f ? ws.x / ws.y : 1f;
        float height = Mathf.Min(maxHeight, maxWidth / aspect);

        image.texture = target.Texture;
        image.uvRect = target.UVRect;
        var size = new Vector2(Unit * aspect, Unit);
        ((RectTransform)canvas.transform).sizeDelta = size;
        image.rectTransform.sizeDelta = size;
        border.rectTransform.sizeDelta = size + Vector2.one * 24f;
        hint.rectTransform.anchoredPosition = new Vector2(0f, -Unit * 0.5f - 60f);
        hint.text = VRRig.IsActiveNow ? hintVR : hintDesktop;

        // 벽의 그림 자리 → 지금 바라보는 방향 눈앞
        var fwd = cam.transform.forward;
        startPos = target.picture.bounds.center;
        startScale = ws.y / Unit;
        endPos = cam.transform.position + fwd * distance;
        endScale = height / Unit;
        canvas.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        SetDesktopControl(false);
        canvas.gameObject.SetActive(true);
        Play(true);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        closedFrame = Time.frameCount;
        SetDesktopControl(true);
        Play(false);
    }

    Camera ViewCamera()
    {
        if (VRRig.IsActiveNow) return VRRig.Current.head;
        var m = GameLoopManager.Instance;
        return m != null && m.playerCamera != null ? m.playerCamera : Camera.main;
    }

    // 데스크톱: 보는 동안 마우스 시점·키보드 이동 정지 (VR은 VRRig가 IsBusy를 보고 멈춘다)
    void SetDesktopControl(bool on)
    {
        var m = GameLoopManager.Instance;
        if (VRRig.IsActiveNow || m == null) return;
        if (!on == controlLocked) return;
        controlLocked = !on;
        foreach (var b in m.disableWhileUIOpen) if (b != null) b.enabled = on;
    }

    void Play(bool zoomIn)
    {
        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(Animate(zoomIn));
    }

    IEnumerator Animate(bool zoomIn)
    {
        float duration = zoomIn ? zoomTime : zoomTime * 0.6f;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(0.01f, duration))
        {
            Apply(Mathf.SmoothStep(0f, 1f, zoomIn ? t : 1f - t));
            yield return null;
        }
        Apply(zoomIn ? 1f : 0f);
        if (!zoomIn) canvas.gameObject.SetActive(false);
        anim = null;
    }

    // k: 0 = 벽의 그림 자리, 1 = 눈앞 확대
    void Apply(float k)
    {
        canvas.transform.position = Vector3.Lerp(startPos, endPos, k);
        canvas.transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, k);
        backdrop.color = new Color(0f, 0f, 0f, backdropAlpha * k);
        hint.color = new Color(1f, 1f, 1f, k);
    }

    // ---------- UI ----------

    void BuildUI()
    {
        var go = new GameObject("PaintingViewerCanvas", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 200;

        backdrop = MakeGraphic<Image>(go.transform, "Backdrop", new Vector2(40000f, 40000f));
        border = MakeGraphic<Image>(go.transform, "Border", Vector2.one * Unit);
        border.color = new Color(0.93f, 0.89f, 0.8f);
        image = MakeGraphic<RawImage>(go.transform, "Picture", Vector2.one * Unit);

        hint = MakeGraphic<Text>(go.transform, "Hint", new Vector2(1600f, 80f));
        hint.font = QuizUI.LoadKoreanFont(40);
        hint.fontSize = 40;
        hint.alignment = TextAnchor.MiddleCenter;

        go.SetActive(false);
    }

    static T MakeGraphic<T>(Transform parent, string name, Vector2 size) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var g = go.AddComponent<T>();
        g.rectTransform.sizeDelta = size;
        g.raycastTarget = false;
        g.material = XRCanvasAdapter.OnTopMaterial;   // 벽·사람에 가려지지 않게
        return g;
    }
}
