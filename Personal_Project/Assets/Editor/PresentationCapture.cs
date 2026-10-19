using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

// 발표 자료 캡처: 고해상도 스크린샷(화면 UI 포함)과 플레이 투어 MP4 영상.
// Play 모드에서 플레이어 카메라로 직접 렌더링하므로 게임 창 크기와 상관없이 지정한 해상도로 뽑힌다.
// 결과는 프로젝트 폴더의 Presentation/ 에 저장된다.
public static class PresentationCapture
{
    public static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Presentation"));

    // ---------- 스크린샷 ----------

    // 화면 오버레이 UI(안내 창·층 선택·엔딩·토스트)를 카메라에 같이 그리도록 잠깐 바꾼다
    static List<(Canvas c, RenderMode mode, Camera cam, float dist)> SwapOverlays(Camera cam)
    {
        var list = new List<(Canvas, RenderMode, Camera, float)>();
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay || !c.gameObject.activeInHierarchy) continue;
            list.Add((c, c.renderMode, c.worldCamera, c.planeDistance));
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
            c.planeDistance = cam.nearClipPlane + 0.02f + 0.0005f * Mathf.Clamp(1000 - c.sortingOrder, 0, 1000);   // 정렬 순서가 높을수록 앞 (항상 카메라 앞쪽)
        }
        Canvas.ForceUpdateCanvases();
        return list;
    }

    static void RestoreOverlays(List<(Canvas c, RenderMode mode, Camera cam, float dist)> list)
    {
        foreach (var e in list) { if (e.c == null) continue; e.c.renderMode = e.mode; e.c.worldCamera = e.cam; e.c.planeDistance = e.dist; }
    }

    public static Texture2D Render(Camera cam, int w, int h, bool withUI, Texture2D reuse = null)
    {
        var swapped = withUI ? SwapOverlays(cam) : null;
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 4);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = reuse != null && reuse.width == w && reuse.height == h ? reuse : new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        if (swapped != null) RestoreOverlays(swapped);
        return tex;
    }

    public static string Shot(Camera cam, string name, int w = 1920, int h = 1080, bool withUI = true)
    {
        Directory.CreateDirectory(OutDir);
        var tex = Render(cam, w, h, withUI);
        var path = Path.Combine(OutDir, name + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        return path;
    }

    // ---------- 영상 ----------

    static MediaEncoder encoder;
    static Texture2D frameTex;
    static int vw, vh, frames;
    public static string VideoPath { get; private set; }
    public static int Frames => frames;

    public static void BeginVideo(string name, int w = 1280, int h = 720, int fps = 30)
    {
        EndVideo();
        Directory.CreateDirectory(OutDir);
        vw = w; vh = h; frames = 0;
        VideoPath = Path.Combine(OutDir, name + ".mp4");
        var attr = new VideoTrackAttributes
        {
            frameRate = new MediaRational(fps),
            width = (uint)w,
            height = (uint)h,
            includeAlpha = false,
            bitRateMode = UnityEditor.VideoBitrateMode.High,
        };
        encoder = new MediaEncoder(VideoPath, attr);
        Time.captureFramerate = fps;   // EditorApplication.Step 한 번 = 정확히 1/fps초
    }

    public static void AddFrame(Camera cam, bool withUI = true)
    {
        if (encoder == null) return;
        frameTex = Render(cam, vw, vh, withUI, frameTex);
        encoder.AddFrame(frameTex);
        frames++;
    }

    public static void EndVideo()
    {
        if (encoder != null) { encoder.Dispose(); encoder = null; }
        Time.captureFramerate = 0;
    }

    // ---------- 투어 (플레이어를 경로대로 걷게) ----------

    public struct Key
    {
        public Vector3 pos; public float yaw, pitch, seconds;   // seconds = 이전 지점에서 여기까지 걸리는 시간 (0이면 순간이동)
        public Key(Vector3 p, float yaw, float pitch, float seconds) { pos = p; this.yaw = yaw; this.pitch = pitch; this.seconds = seconds; }
    }

    static List<Key> keys;
    static int seg;
    static float segT;

    public static void BeginTour(List<Key> path) { keys = path; seg = 1; segT = 0f; Place(keys[0].pos, keys[0].yaw, keys[0].pitch); }

    public static bool TourDone => keys == null || seg >= keys.Count;

    // 한 프레임 진행: 위치·시선을 부드럽게 옮기고 → 게임 1프레임 → 녹화
    public static void TourStep(float dt)
    {
        if (TourDone) return;
        var a = keys[seg - 1]; var b = keys[seg];
        segT += dt;
        float k = b.seconds <= 0f ? 1f : Mathf.Clamp01(segT / b.seconds);
        float e = k * k * (3f - 2f * k);
        Place(Vector3.Lerp(a.pos, b.pos, e), Mathf.LerpAngle(a.yaw, b.yaw, e), Mathf.Lerp(a.pitch, b.pitch, e));
        if (k >= 1f) { seg++; segT = 0f; }
    }

    public static void Place(Vector3 pos, float yaw, float pitch)
    {
        var m = GameLoopManager.Instance;
        var cc = m.player.GetComponent<CharacterController>();
        if (cc) cc.enabled = false;
        m.player.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        if (cc) cc.enabled = true;
        var cam = m.playerCamera.transform;
        cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        foreach (var b in m.playerCamera.GetComponents<MonoBehaviour>())
        {
            var f = b != null ? b.GetType().GetField("xRotation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) : null;
            if (f != null && f.FieldType == typeof(float)) f.SetValue(b, pitch);
        }
    }
}
