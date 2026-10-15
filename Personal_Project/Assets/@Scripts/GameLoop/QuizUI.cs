using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// 실행 시 코드로 만드는 퀴즈 UI (프리팹 불필요)
//  - 가운데 퀴즈 창: 제목 / 본문 / 내용 영역(버튼·입력칸·라벨)
//  - 위쪽 알림(Toast), 화면 가운데 성공 연출(Celebrate), 조준점
// 한글 표시를 위해 OS 폰트(맑은 고딕)를 동적으로 사용한다.
public class QuizUI : MonoBehaviour
{
    public bool IsOpen => panel.activeSelf;
    public bool IsTyping => activeInput != null && activeInput.isFocused;
    public FloorQuiz CurrentQuiz { get; private set; }
    public event Action<bool> OnVisibilityChanged;

    Font font;
    GameObject panel;
    Text titleText, bodyText, toastText, celebrateText, promptText;
    GameObject promptBox, crosshair;
    Image fade;
    RectTransform content;
    InputField activeInput;
    Coroutine toastRoutine, celebrateRoutine;

    static readonly Color PanelColor = new Color(0.07f, 0.07f, 0.08f, 0.92f);
    static readonly Color ButtonColor = new Color(0.93f, 0.9f, 0.84f, 1f);
    static readonly Color Accent = new Color(1f, 0.86f, 0.6f, 1f);

    public static QuizUI Create(Transform parent)
    {
        var go = new GameObject("QuizUI");
        go.transform.SetParent(parent, false);
        var ui = go.AddComponent<QuizUI>();
        ui.Build();
        return ui;
    }

    // ---------- 공개 API ----------

    public void Open(FloorQuiz quiz)
    {
        CurrentQuiz = quiz;
        panel.SetActive(true);
        Rebuild();
        OnVisibilityChanged?.Invoke(true);
    }

    public void Close()
    {
        if (!panel.activeSelf) return;
        panel.SetActive(false);
        activeInput = null;
        OnVisibilityChanged?.Invoke(false);
    }

    public void Rebuild()
    {
        // 먼저 숨겨서 같은 프레임에 새로 만든 요소와 섞이지 않게 한 뒤 삭제
        foreach (Transform c in content) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
        activeInput = null;
        titleText.text = $"{CurrentQuiz.Stage.floorNumber}F  ·  {CurrentQuiz.Title}";
        bodyText.text = "";
        CurrentQuiz.BuildUI(this);
    }

    public void SetBody(string text) => bodyText.text = text;

    public void AddLabel(string text) => MakeText(content, text, 26, TextAnchor.MiddleLeft, Color.white, 40);

    public void AddHint(string text) => MakeText(content, text, 20, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.55f), 40);

    public void AddButton(string label, Action onClick)
    {
        var go = MakeBox(content, "Button", ButtonColor, 58);
        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(() => onClick());
        var t = MakeText(go.transform, label, 24, TextAnchor.MiddleCenter, new Color(0.1f, 0.1f, 0.1f), 0);
        Stretch(t.rectTransform);
    }

    public InputField AddInput(string placeholder, string value, Action<string> onChanged)
    {
        var go = MakeBox(content, "Input", Color.white, 64);
        var input = go.AddComponent<InputField>();
        var text = MakeText(go.transform, "", 30, TextAnchor.MiddleCenter, Color.black, 0); Stretch(text.rectTransform);
        var ph = MakeText(go.transform, placeholder, 24, TextAnchor.MiddleCenter, new Color(0, 0, 0, 0.35f), 0); Stretch(ph.rectTransform);
        input.textComponent = text;
        input.placeholder = ph;
        input.characterLimit = 12;
        input.text = value;
        input.onValueChanged.AddListener(v => onChanged(v));
        activeInput = input;
        input.Select(); input.ActivateInputField();
        return input;
    }

    // 화면 전환용 검은 화면 (alpha 0 = 투명, 1 = 완전히 검정)
    public IEnumerator Fade(float to, float seconds)
    {
        fade.gameObject.SetActive(true);
        float from = fade.color.a;
        for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(0.01f, seconds))
        {
            fade.color = new Color(0, 0, 0, Mathf.Lerp(from, to, t));
            yield return null;
        }
        fade.color = new Color(0, 0, 0, to);
        if (to <= 0f) fade.gameObject.SetActive(false);
    }

    // 문 앞 상호작용 안내 (화면 아래)
    public void ShowPrompt(string message)
    {
        promptText.text = message;
        promptBox.SetActive(true);
    }

    public void HidePrompt() => promptBox.SetActive(false);

    // VR: 화면 오버레이는 헤드셋에 보이지 않으므로 눈앞에 떠 있는 판(World Space)으로 바꾼다
    public void AttachToXRCamera(Camera cam, float distance = 1f, float width = 1.1f)
    {
        crosshair.SetActive(false);   // 조준점 대신 손 레이저 사용
        transform.SetParent(cam.transform, false);
        XRCanvasAdapter.ToWorldSpace(GetComponent<Canvas>(), cam, width);
        transform.localPosition = new Vector3(0f, 0f, distance);
        transform.localRotation = Quaternion.identity;
        // 화면 전환 검은 판은 시야 전체를 덮도록 크게
        fade.rectTransform.offsetMin = new Vector2(-6000f, -6000f);
        fade.rectTransform.offsetMax = new Vector2(6000f, 6000f);
    }

    public void Toast(string message, float seconds)
    {
        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(ShowFor(toastText, message, seconds));
    }

    public void Celebrate(string message)
    {
        if (celebrateRoutine != null) StopCoroutine(celebrateRoutine);
        celebrateRoutine = StartCoroutine(CelebrateRoutine(message));
    }

    // 한글 폰트: 프로젝트에 넣은 Resources/Fonts/NotoSansKR (Quest 등 OS 폰트가 없는 기기용), 없으면 OS 폰트
    public static Font LoadKoreanFont(int size)
    {
        var bundled = Resources.Load<Font>("Fonts/NotoSansKR");
        return bundled != null ? bundled : Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, size);
    }

    // ---------- 내부 ----------

    void Build()
    {
        font = LoadKoreanFont(32);

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(transform, false);
        }

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        // 조준점
        crosshair = MakeBox(transform, "Crosshair", new Color(1, 1, 1, 0.8f), 0);
        var dot = crosshair.GetComponent<RectTransform>();
        dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0.5f); dot.sizeDelta = new Vector2(8, 8);
        dot.GetComponent<Image>().raycastTarget = false;

        // 퀴즈 창
        panel = MakeBox(transform, "QuizPanel", PanelColor, 0);
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f); prt.sizeDelta = new Vector2(980, 760);
        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(48, 48, 36, 36); layout.spacing = 14;
        layout.childForceExpandHeight = false; layout.childControlHeight = true; layout.childControlWidth = true;

        titleText = MakeText(panel.transform, "", 38, TextAnchor.MiddleLeft, Accent, 56);
        titleText.fontStyle = FontStyle.Bold;
        bodyText = MakeText(panel.transform, "", 26, TextAnchor.UpperLeft, Color.white, 0);
        bodyText.gameObject.AddComponent<LayoutElement>().minHeight = 150;

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(panel.transform, false);
        content = contentGo.GetComponent<RectTransform>();
        var cl = contentGo.AddComponent<VerticalLayoutGroup>();
        cl.spacing = 10; cl.childForceExpandHeight = false; cl.childControlHeight = true; cl.childControlWidth = true;
        contentGo.AddComponent<LayoutElement>().flexibleHeight = 1;

        MakeText(panel.transform, "Tab / Q : 창 닫기", 18, TextAnchor.MiddleRight, new Color(1, 1, 1, 0.4f), 28);
        panel.SetActive(false);

        // 알림
        toastText = MakeText(transform, "", 28, TextAnchor.MiddleCenter, Color.white, 0);
        var trt = toastText.rectTransform; trt.anchorMin = new Vector2(0.1f, 0.86f); trt.anchorMax = new Vector2(0.9f, 0.95f); trt.offsetMin = trt.offsetMax = Vector2.zero;
        toastText.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);
        toastText.raycastTarget = false;

        // 상호작용 안내 (화면 아래 가운데)
        promptBox = MakeBox(transform, "Prompt", new Color(0, 0, 0, 0.65f), 0);
        var prt2 = promptBox.GetComponent<RectTransform>();
        prt2.anchorMin = prt2.anchorMax = new Vector2(0.5f, 0.2f); prt2.sizeDelta = new Vector2(820, 70);
        promptBox.GetComponent<Image>().raycastTarget = false;
        promptText = MakeText(promptBox.transform, "", 30, TextAnchor.MiddleCenter, Color.white, 0);
        Stretch(promptText.rectTransform);
        promptText.raycastTarget = false;
        promptBox.SetActive(false);

        // 화면 전환용 검은 화면 (퀴즈 창 위, 성공 문구 아래)
        fade = MakeBox(transform, "Fade", new Color(0, 0, 0, 0), 0).GetComponent<Image>();
        Stretch(fade.rectTransform);
        fade.raycastTarget = false;
        fade.gameObject.SetActive(false);

        // 성공 연출
        celebrateText = MakeText(transform, "", 72, TextAnchor.MiddleCenter, Accent, 0);
        Stretch(celebrateText.rectTransform);
        celebrateText.fontStyle = FontStyle.Bold;
        celebrateText.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f);
        celebrateText.raycastTarget = false;
    }

    IEnumerator ShowFor(Text t, string message, float seconds)
    {
        t.text = message;
        t.color = new Color(t.color.r, t.color.g, t.color.b, 1f);
        yield return new WaitForSecondsRealtime(seconds);
        for (float a = 1f; a > 0f; a -= Time.unscaledDeltaTime * 2f)
        {
            t.color = new Color(t.color.r, t.color.g, t.color.b, a);
            yield return null;
        }
        t.text = "";
    }

    IEnumerator CelebrateRoutine(string message)
    {
        celebrateText.text = message;
        for (float s = 0f; s < 1f; s += Time.unscaledDeltaTime * 3f)
        {
            float k = 1f + Mathf.Sin(s * Mathf.PI) * 0.15f;   // 살짝 커졌다 돌아오는 연출
            celebrateText.rectTransform.localScale = Vector3.one * k;
            yield return null;
        }
        celebrateText.rectTransform.localScale = Vector3.one;
        yield return ShowFor(celebrateText, message, 2.5f);
    }

    GameObject MakeBox(Transform parent, string name, Color color, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        if (height > 0) go.AddComponent<LayoutElement>().preferredHeight = height;
        return go;
    }

    Text MakeText(Transform parent, string text, int size, TextAnchor anchor, Color color, float height)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.alignment = anchor; t.color = color; t.text = text;
        t.supportRichText = true; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        if (height > 0) go.AddComponent<LayoutElement>().preferredHeight = height;
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
