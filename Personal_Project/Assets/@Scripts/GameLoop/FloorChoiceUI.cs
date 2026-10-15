using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 층 선택 창: 계단실 문에서 갈 수 있는 층이 여럿일 때 "어디로 갈까요?"를 묻는다.
// 시작 안내(IntroPanel)와 같은 티켓 종이·잉크색. 뒤는 어둡게 가린다.
//  - 데스크톱: 버튼 클릭 또는 숫자 키(1, 2 …), 취소는 Esc / 취소 버튼
//  - VR: 눈앞 판으로 떠서 레이저 + 트리거로 버튼 클릭
public class FloorChoiceUI : MonoBehaviour
{
    public struct Option
    {
        public string label;
        public Action onChoose;
        public Option(string label, Action onChoose) { this.label = label; this.onChoose = onChoose; }
    }

    public static bool IsOpen => instance != null && instance.canvas.gameObject.activeSelf;

    static FloorChoiceUI instance;
    static readonly Color Ink = new Color(0.1f, 0.07f, 0.05f, 1f);
    static readonly Color Paper = new Color(0.95f, 0.91f, 0.84f, 1f);

    GameLoopManager manager;
    Canvas canvas;
    RectTransform card, backdrop;
    Text titleText;
    readonly List<GameObject> buttons = new List<GameObject>();
    List<Option> current;
    int openedFrame;

    public static void Show(GameLoopManager m, string title, List<Option> options)
    {
        if (instance == null)
        {
            instance = new GameObject("FloorChoiceUI").AddComponent<FloorChoiceUI>();
            instance.transform.SetParent(m.transform, false);
            instance.Build();
        }
        instance.manager = m;
        instance.Open(title, options);
    }

    void OnDestroy() { if (instance == this) instance = null; }

    void Open(string title, List<Option> options)
    {
        current = options;
        titleText.text = title;
        foreach (var b in buttons) Destroy(b);
        buttons.Clear();

        var font = QuizUI.LoadKoreanFont(40);
        float gap = 110f, top = (options.Count - 1) * gap * 0.5f + 10f;
        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            buttons.Add(MakeButton(options[i].label, font, new Vector2(0, top - i * gap), new Vector2(560, 90), Ink, Paper, () => Choose(index)));
        }
        buttons.Add(MakeButton("취소", font, new Vector2(0, -205), new Vector2(220, 64), new Color(Ink.r, Ink.g, Ink.b, 0.55f), Paper, Close));

        canvas.gameObject.SetActive(true);
        openedFrame = Time.frameCount;
        if (VRRig.IsActiveNow) PlaceInFront(VRRig.Current);
        SetDesktopControl(false);
    }

    void Choose(int index)
    {
        var o = current[index];
        Close();
        o.onChoose?.Invoke();
    }

    void Close()
    {
        if (!canvas.gameObject.activeSelf) return;
        canvas.gameObject.SetActive(false);
        SetDesktopControl(true);
    }

    void Update()
    {
        if (!IsOpen || Time.frameCount == openedFrame) return;   // 창을 연 키 입력은 무시
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.escapeKey.wasPressedThisFrame) { Close(); return; }
        for (int i = 0; i < current.Count && i < 9; i++)
            if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame) { Choose(i); return; }
    }

    // 데스크톱: 창이 떠 있는 동안 이동·시점 정지 + 커서 표시
    void SetDesktopControl(bool on)
    {
        if (manager == null || VRRig.IsActiveNow) return;
        foreach (var b in manager.disableWhileUIOpen) if (b != null) b.enabled = on;
        Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !on;
    }

    void PlaceInFront(VRRig rig)
    {
        if (canvas.renderMode != RenderMode.WorldSpace)
        {
            XRCanvasAdapter.ToWorldSpace(canvas, rig.head, 1.4f);
            backdrop.anchorMin = backdrop.anchorMax = new Vector2(0.5f, 0.5f);
            backdrop.sizeDelta = new Vector2(40000f, 40000f);   // 주변을 어둡게
        }
        var head = rig.head.transform;
        var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        canvas.transform.SetPositionAndRotation(head.position + fwd * 1.1f, Quaternion.LookRotation(fwd));
    }

    // ---------- UI ----------

    void Build()
    {
        var go = new GameObject("FloorChoiceCanvas", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 260;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();

        var bd = MakeImage(go.transform, "Backdrop", new Color(0f, 0f, 0f, 0.8f));
        bd.raycastTarget = true;
        backdrop = bd.rectTransform;
        backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one; backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;

        var intro = FindFirstObjectByType<IntroPanel>();
        var img = MakeImage(go.transform, "Ticket", intro != null && intro.ticket != null ? Color.white : Paper);
        if (intro != null && intro.ticket != null) { img.sprite = intro.ticket; img.preserveAspect = true; }
        card = img.rectTransform;
        card.sizeDelta = new Vector2(1100, 626);

        titleText = MakeText(card, "", QuizUI.LoadKoreanFont(56), 56, new Vector2(0, 170), new Vector2(900, 90), Ink);
        go.SetActive(false);
    }

    GameObject MakeButton(string label, Font font, Vector2 pos, Vector2 size, Color bg, Color fg, Action onClick)
    {
        var img = MakeImage(card, label, bg);
        img.raycastTarget = true;
        img.rectTransform.anchoredPosition = pos;
        img.rectTransform.sizeDelta = size;
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(() => onClick());
        MakeText(img.rectTransform, label, font, Mathf.RoundToInt(size.y * 0.4f), Vector2.zero, size, fg);
        if (canvas.renderMode == RenderMode.WorldSpace)   // VR 판에서는 벽에 가려지지 않게
            foreach (var g in img.GetComponentsInChildren<Graphic>()) g.material = XRCanvasAdapter.OnTopMaterial;
        return img.gameObject;
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
        var outline = go.AddComponent<Outline>();   // 폰트가 가늘어서 같은 색 외곽선으로 굵게
        outline.effectColor = color;
        outline.effectDistance = new Vector2(size / 50f, -size / 50f);
        return t;
    }
}
