using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.UI;

// 트레일러 영상 만들기 (Play 모드에서 실행, 여러 번 나눠 호출)
//   TrailerMaker.Begin();  →  while (!TrailerMaker.Done) TrailerMaker.Step(150);  →  TrailerMaker.End();
// 타이틀 카드 → 층별 컷(천천히 움직이는 카메라 + 층 자막) → 엔딩 카드. 층마다 그 층 BGM을 깔고, 끝에 엔딩 효과음.
// 결과: Presentation/트레일러.mp4 (1920x1080, 30fps, 스테레오 48kHz)
public static class TrailerMaker
{
    const int W = 1920, H = 1080, FPS = 30, RATE = 48000;

    class Shot
    {
        public string caption, sub;     // 화면 왼쪽 아래 자막 (층 바뀔 때만)
        public Vector3 p0, p1; public float yaw0, yaw1, pitch0, pitch1, sec;
        public int music;               // 0=1층 1=2층 2=3층 -1=없음
        public bool card;               // 검은 타이틀/엔딩 카드
        public float start;
    }

    static List<Shot> shots;
    static float total;
    static int frame, totalFrames;
    static MediaEncoder enc;
    static Texture2D tex;
    static float[] mix;   // 인터리브 스테레오
    static int audioPos;

    // 오버레이
    static Canvas canvas;
    static Image black;
    static Text title, subtitle, caption, captionSub;
    static Behaviour gameUI;

    public static bool Done => frame >= totalFrames;
    public static string OutPath => Path.Combine(PresentationCapture.OutDir, "트레일러.mp4");

    static Shot S(Vector3 p0, float yaw0, float pitch0, Vector3 p1, float yaw1, float pitch1, float sec, int music, string caption = null, string sub = null)
        => new Shot { p0 = p0, p1 = p1, yaw0 = yaw0, yaw1 = yaw1, pitch0 = pitch0, pitch1 = pitch1, sec = sec, music = music, caption = caption, sub = sub };

    static void BuildShots()
    {
        float f1 = 1f, f2 = 6.94f, f3 = 16.94f;
        shots = new List<Shot>
        {
            new Shot { card = true, sec = 4f, music = 0 },                                                                         // 타이틀
            S(new Vector3(12f, f1, -2.8f), 270, 4,   new Vector3(6.6f, f1, -2.8f), 270, 2,   4.5f, 0, "1F", "파스텔의 방"),   // 입구로 들어가며
            S(new Vector3(0f, f1, 0f), 215, 0,       new Vector3(0f, f1, 0f), 265, 0,        4f, 0),                          // 로비 천천히 둘러보기
            S(new Vector3(-3f, f1, 3f), 200, 0,      new Vector3(-5f, f1, -1.5f), 245, -2,    4f, 0),                          // 곡선 벽 따라
            S(new Vector3(-3f, f2, -2f), 305, 4,     new Vector3(-2f, f2, -1.2f), 280, 2,     4.5f, 1, "2F", "색채의 방"),
            S(new Vector3(1.8f, f2, -1f), 0, -3,     new Vector3(1.5f, f2, 4f), 356, -15,     3f, 1),                          // 붓꽃 그림으로 다가가면
            S(new Vector3(1.5f, f2, 4f), 356, -15,   new Vector3(1.5f, f2, 4f), 356, -8,      4.5f, 1),                        // 나비가 빠져나온다
            S(new Vector3(-0.5f, f2, -1.5f), 85, -12, new Vector3(-0.5f, f2, -1.5f), 95, -6,  4.5f, 1),                        // 여자 그림의 나비
            S(new Vector3(-1.5f, f3, 1.2f), 285, 3,  new Vector3(-9f, f3, 2.2f), 270, 0,      5f, 2, "3F", "흑백의 방"),
            S(new Vector3(-12.5f, f3, 2.5f), 250, -5, new Vector3(-12.5f, f3, 2.5f), 290, -5, 4.5f, 2),
            S(new Vector3(-16.5f, f3, -1.2f), 180, 8, new Vector3(-16.5f, f3, -2.7f), 180, 15, 3.5f, 2),                      // 방명록
            new Shot { card = true, sec = 5f, music = -1 },                                                                        // 엔딩 카드
        };
        float t = 0f;
        foreach (var s in shots) { s.start = t; t += s.sec; }
        total = t;
    }

    // ---------- 시작 / 끝 ----------

    public static string Begin()
    {
        BuildShots();
        totalFrames = Mathf.CeilToInt(total * FPS);
        frame = 0; audioPos = 0;

        var m = GameLoopManager.Instance;
        foreach (var st in m.stages) st.SetUnlocked(true);
        var intro = Object.FindFirstObjectByType<IntroPanel>(); if (intro) intro.Close();
        var door = Object.FindFirstObjectByType<InteractDoor>(); if (door) door.Open(m.player.position);
        if (BGMManager.Instance) BGMManager.Instance.StopAll();     // 게임 BGM 대신 트레일러 음악을 직접 넣는다
        var ui = m.UI.GetComponent<Canvas>(); if (ui) { gameUI = ui; ui.enabled = false; }   // 층 이름·안내 문구·조준점 숨김
        BuildOverlay();
        BuildAudio();

        Directory.CreateDirectory(PresentationCapture.OutDir);
        var v = new VideoTrackAttributes { frameRate = new MediaRational(FPS), width = W, height = H, includeAlpha = false, bitRateMode = VideoBitrateMode.High };
        var a = new AudioTrackAttributes { sampleRate = new MediaRational(RATE), channelCount = 2, language = "ko" };
        enc = new MediaEncoder(OutPath, v, a);
        Time.captureFramerate = FPS;
        return $"{total:F1}s, {totalFrames} frames";
    }

    public static void End()
    {
        if (enc != null) { enc.Dispose(); enc = null; }
        Time.captureFramerate = 0;
        if (canvas) Object.DestroyImmediate(canvas.gameObject);
        if (gameUI) gameUI.enabled = true;
    }

    // ---------- 프레임 ----------

    public static int Step(int count)
    {
        var cam = GameLoopManager.Instance.playerCamera;
        int done = 0;
        while (done < count && !Done)
        {
            float t = frame / (float)FPS;
            var s = Current(t, out int index);
            float k = Mathf.Clamp01((t - s.start) / s.sec);
            float e = k * k * (3f - 2f * k);
            if (!s.card) PresentationCapture.Place(Vector3.Lerp(s.p0, s.p1, e), Mathf.LerpAngle(s.yaw0, s.yaw1, e), Mathf.Lerp(s.pitch0, s.pitch1, e));
            ApplyOverlay(t, s, index);

            EditorApplication.Step();
            tex = PresentationCapture.Render(cam, W, H, true, tex);
            enc.AddFrame(tex);
            AddAudio();
            frame++; done++;
        }
        return frame;
    }

    static Shot Current(float t, out int index)
    {
        for (int i = shots.Count - 1; i >= 0; i--) if (t >= shots[i].start) { index = i; return shots[i]; }
        index = 0; return shots[0];
    }

    // ---------- 오버레이 (타이틀·자막·페이드) ----------

    static void BuildOverlay()
    {
        var go = new GameObject("TrailerOverlay", typeof(RectTransform));
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(W, H);
        scaler.matchWidthOrHeight = 0.5f;
        var font = QuizUI.LoadKoreanFont(96);

        caption = MakeText(go.transform, font, 96, TextAnchor.LowerLeft, new Vector2(110, 150), new Vector2(800, 120), Vector2.zero);
        captionSub = MakeText(go.transform, font, 36, TextAnchor.LowerLeft, new Vector2(116, 92), new Vector2(800, 50), Vector2.zero);   // "1F" 아래 줄
        black = new GameObject("Black", typeof(RectTransform)).AddComponent<Image>();
        black.transform.SetParent(go.transform, false);
        black.color = Color.black; black.raycastTarget = false;
        var brt = black.rectTransform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
        title = MakeText(go.transform, font, 120, TextAnchor.MiddleCenter, new Vector2(0, 40), new Vector2(1800, 180), new Vector2(0.5f, 0.5f));
        subtitle = MakeText(go.transform, font, 38, TextAnchor.MiddleCenter, new Vector2(0, -70), new Vector2(1800, 80), new Vector2(0.5f, 0.5f));
    }

    static Text MakeText(Transform parent, Font font, int size, TextAnchor align, Vector2 pos, Vector2 box, Vector2 anchor)
    {
        var t = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = font; t.fontSize = size; t.alignment = align; t.color = new Color(1, 1, 1, 0); t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        var rt = t.rectTransform; rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor; rt.anchoredPosition = pos; rt.sizeDelta = box;
        var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.6f); sh.effectDistance = new Vector2(3, -3);
        return t;
    }

    static float Ramp(float x, float a, float b) => Mathf.Clamp01((x - a) / Mathf.Max(0.0001f, b - a));

    static void ApplyOverlay(float t, Shot s, int index)
    {
        float local = t - s.start;
        float blackA = 0f, titleA = 0f;
        string titleText = "", subText = "";

        if (s.card && index == 0)
        {
            blackA = 1f - Ramp(local, s.sec - 0.8f, s.sec);                       // 마지막에 장면으로 페이드
            titleA = Ramp(local, 0.4f, 1.6f) * (1f - Ramp(local, s.sec - 1.0f, s.sec - 0.3f));
            titleText = "MONOCHROME GALLERY"; subText = "그림 속을 걷는 VR 미술관";
        }
        else if (s.card)
        {
            blackA = Ramp(local, 0f, 0.8f);
            titleA = Ramp(local, 0.8f, 2.0f) * (1f - Ramp(local, s.sec - 0.6f, s.sec));
            titleText = "MONOCHROME GALLERY"; subText = "Meta Quest 2  ·  VR Personal Project";
        }
        else
        {
            // 층이 바뀌는 컷 앞뒤로 짧게 검게 (같은 층 안에서는 바로 컷)
            bool newFloor = s.caption != null;
            var next = index + 1 < shots.Count ? shots[index + 1] : null;
            bool floorEnds = next != null && (next.caption != null || next.card);
            if (newFloor) blackA = Mathf.Max(blackA, 1f - Ramp(local, 0f, 0.6f));
            if (floorEnds && !next.card) blackA = Mathf.Max(blackA, Ramp(local, s.sec - 0.6f, s.sec));
        }

        black.color = new Color(0, 0, 0, blackA);
        title.text = titleText; title.color = new Color(1, 1, 1, titleA);
        subtitle.text = subText; subtitle.color = new Color(0.85f, 0.85f, 0.85f, titleA);

        // 층 자막: 그 층 첫 컷 시작부터 2.8초 동안
        float capA = 0f; string cap = "", capSub = "";
        for (int i = index; i >= 0; i--)
        {
            if (shots[i].card) break;
            if (shots[i].caption == null) continue;
            float since = t - shots[i].start;
            capA = Ramp(since, 0.5f, 1.1f) * (1f - Ramp(since, 3.2f, 3.8f));
            cap = shots[i].caption; capSub = shots[i].sub;
            break;
        }
        caption.text = cap; caption.color = new Color(1, 1, 1, capA);
        captionSub.text = capSub; captionSub.color = new Color(0.9f, 0.9f, 0.9f, capA * 0.9f);
    }

    // ---------- 오디오 ----------

    static float[] Load(string path)
    {
        var imp = (AudioImporter)AssetImporter.GetAtPath(path);
        var s = imp.defaultSampleSettings; var oldLoad = s.loadType;
        bool changed = oldLoad != AudioClipLoadType.DecompressOnLoad;
        if (changed) { s.loadType = AudioClipLoadType.DecompressOnLoad; imp.defaultSampleSettings = s; imp.SaveAndReimport(); }
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        clip.LoadAudioData();
        int ch = Mathf.Max(1, clip.channels);
        var data = new float[clip.samples * ch];
        if (!clip.GetData(data, 0)) Debug.LogWarning("[Trailer] 오디오를 읽지 못함: " + path);
        int frames = data.Length / ch;
        if (frames == 0) return new float[2];
        // 48kHz 스테레오로
        int outLen = Mathf.CeilToInt(frames * (RATE / (float)clip.frequency));
        var outp = new float[outLen * 2];
        for (int i = 0; i < outLen; i++)
        {
            float src = i * (clip.frequency / (float)RATE);   // int 곱셈은 넘칠 수 있어 float로
            int i0 = Mathf.Min((int)src, frames - 1), i1 = Mathf.Min(i0 + 1, frames - 1); float f = src - i0;
            for (int c = 0; c < 2; c++)
            {
                int cc = Mathf.Min(c, ch - 1);
                outp[i * 2 + c] = Mathf.Lerp(data[i0 * ch + cc], data[i1 * ch + cc], f);
            }
        }
        if (changed) { s.loadType = oldLoad; imp.defaultSampleSettings = s; imp.SaveAndReimport(); }
        return outp;
    }

    static void BuildAudio()
    {
        int n = Mathf.CeilToInt(total * RATE) + RATE;
        mix = new float[n * 2];
        var tracks = new[] { Load("Assets/@Audio/BGM/BGM_F1.mp3"), Load("Assets/@Audio/BGM/BGM_F2.mp3"), Load("Assets/@Audio/BGM/BGM_F3.mp3") };
        var ending = Load("Assets/@Audio/BGM/BGM_Ending.mp3");

        // 곡마다 구간 [시작, 끝] — 1초씩 겹쳐 크로스페이드
        var range = new float[3, 2];
        for (int m = 0; m < 3; m++) { range[m, 0] = float.MaxValue; range[m, 1] = 0; }
        foreach (var s in shots) if (s.music >= 0) { range[s.music, 0] = Mathf.Min(range[s.music, 0], s.start); range[s.music, 1] = Mathf.Max(range[s.music, 1], s.start + s.sec); }
        float endCard = shots[shots.Count - 1].start;
        for (int m = 0; m < 3; m++)
        {
            float a = range[m, 0], b = m == 2 ? endCard + 2.5f : range[m, 1] + 0.5f;   // 마지막 곡은 엔딩 카드에서 서서히
            float inStart = m == 0 ? a : a - 0.5f;
            var tr = tracks[m];
            for (int i = (int)(inStart * RATE); i < (int)(b * RATE) && i < n; i++)
            {
                float t = i / (float)RATE;
                float gain = 0.8f * Ramp(t, inStart, inStart + 1f) * (1f - Ramp(t, b - (m == 2 ? 2.5f : 1f), b));
                int si = (i - (int)(inStart * RATE)) % (tr.Length / 2);
                mix[i * 2] += tr[si * 2] * gain; mix[i * 2 + 1] += tr[si * 2 + 1] * gain;
            }
        }
        int e0 = (int)((endCard + 0.6f) * RATE);
        for (int i = 0; i < ending.Length / 2 && e0 + i < n; i++) { mix[(e0 + i) * 2] += ending[i * 2] * 0.9f; mix[(e0 + i) * 2 + 1] += ending[i * 2 + 1] * 0.9f; }
        // 맨 끝 0.5초는 무음으로
        for (int i = 0; i < mix.Length; i++) mix[i] = Mathf.Clamp(mix[i], -1f, 1f) * (1f - Ramp(i / 2f / RATE, total - 0.5f, total));
    }

    static void AddAudio()
    {
        int per = RATE / FPS;
        using (var buf = new NativeArray<float>(per * 2, Allocator.Temp))
        {
            var b = buf;
            for (int i = 0; i < per * 2; i++) { int idx = audioPos * 2 + i; b[i] = idx < mix.Length ? mix[idx] : 0f; }
            enc.AddSamples(b);
        }
        audioPos += per;
    }
}
