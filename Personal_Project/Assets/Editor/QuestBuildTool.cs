using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

// 메타 퀘스트 2 원클릭 빌드
//  Tools > Quest 2 > Patch and Run (Ctrl+Shift+Q) : 바뀐 스크립트·에셋만 기기에 덮어쓰고 바로 실행 (빠름)
//  Tools > Quest 2 > Full Build and Run           : APK 전체를 새로 빌드·설치·실행
//  Tools > Quest 2 > Build Window                 : 기기 상태와 버튼이 있는 창
// 처음 한 번은 자동으로 전체 빌드를 하고, 이후부터 패치로 빠르게 넣는다.
// 빌드 대상이 Android가 아니면 전환한 뒤(스크립트 다시 불러오기 후) 이어서 진행한다.
public class QuestBuildTool : EditorWindow
{
    const string OutputPath = "C:/QB/gallery.apk";      // 짧은 경로 (긴 경로는 윈도우 260자 제한에 걸림)
    const string PendingKey = "QuestBuild.Pending";      // 0 없음, 1 전체, 2 패치 (플랫폼 전환 뒤 이어서)
    const string FullDoneKey = "QuestBuild.FullBuildDone.";
    const string RestorePcKey = "QuestBuild.RestorePc";

    // ---------- 메뉴 ----------

    [MenuItem("Tools/Quest 2/Patch and Run %#q", priority = 0)]
    static void PatchAndRunMenu() => Run(patch: true);

    [MenuItem("Tools/Quest 2/Full Build and Run", priority = 1)]
    static void FullBuildMenu() => Run(patch: false);

    // 개발 빌드(패치용)는 최적화가 꺼져 프레임이 떨어진다 — 성능 확인·시연용은 이걸로
    [MenuItem("Tools/Quest 2/Release Build and Run (성능 확인용)", priority = 2)]
    static void ReleaseBuildMenu() => Run(patch: false, release: true);

    [MenuItem("Tools/Quest 2/Build Window", priority = 20)]
    static void OpenWindow() => GetWindow<QuestBuildTool>("Quest 2 Build").minSize = new Vector2(320, 230);

    // 플랫폼 전환으로 스크립트가 다시 불러와진 뒤, 하던 빌드를 이어서
    [InitializeOnLoadMethod]
    static void ResumeAfterSwitch() => EditorApplication.delayCall += TryResume;

    // 전환이 끝났으면 예약된 빌드를 한 번만 실행 (스크립트가 다시 불러와져도, 아니어도 둘 중 먼저 온 쪽이)
    static void TryResume()
    {
        int pending = SessionState.GetInt(PendingKey, 0);
        if (pending == 0 || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) return;
        SessionState.SetInt(PendingKey, 0);
        Run(pending == 2);
    }

    // ---------- 빌드 ----------

    static void Run(bool patch, bool release = false)
    {
        if (release) patch = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Fail("Play 모드를 끄고 다시 눌러 주세요."); return; }
        if (Lightmapping.isRunning) { Fail("라이트맵을 굽는 중입니다. 끝난 뒤 다시 눌러 주세요."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // 1) 빌드 대상 Android로
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            if (!EditorUtility.DisplayDialog("Quest 2 빌드", "빌드 대상을 Android로 바꿔야 합니다. (처음 한 번은 몇 분 걸릴 수 있어요)\n바꾼 뒤 자동으로 이어서 빌드합니다.", "바꾸고 빌드", "취소")) return;
            SessionState.SetInt(PendingKey, patch ? 2 : 1);
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            // 스크립트가 다시 불러와지면 ResumeAfterSwitch가, 아니면 이 예약이 이어서 진행
            EditorApplication.delayCall += TryResume;
            return;
        }

        // 2) 기기 확인
        var state = DeviceState(out string serial);
        if (state == "unauthorized") { Fail("퀘스트에서 'USB 디버깅 허용' 창을 확인해 주세요.\n헤드셋을 쓰고 [허용]을 누른 뒤 다시 눌러 주세요."); return; }
        if (state != "device") { Fail("퀘스트 2가 연결되어 있지 않습니다.\nUSB로 연결하고 헤드셋의 USB 디버깅을 켜 주세요."); return; }

        // 3) 처음이면 전체 빌드 (패치는 개발 빌드 APK가 이미 설치돼 있어야 함)
        string pkg = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
        if (patch && (!EditorPrefs.GetBool(FullDoneKey + pkg, false) || !EditorPrefs.GetBool(FullDoneKey + pkg + ".dev", true) || !File.Exists(OutputPath)))
        {
            Debug.Log("[Quest Build] 첫 빌드라 전체 빌드로 진행합니다. 다음부터는 패치로 빠르게 들어갑니다.");
            patch = false;
        }

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) { Fail("Build Profiles에 켜진 씬이 없습니다."); return; }

        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.development = !release;   // 패치는 개발 빌드에서만 된다

        var options = BuildOptions.AutoRunPlayer;
        if (!release) options |= BuildOptions.Development;
        if (patch) options |= BuildOptions.PatchPackage;

        var sw = Stopwatch.StartNew();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = OutputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = options,
        });
        sw.Stop();

        var s = report.summary;
        if (s.result == BuildResult.Succeeded)
        {
            if (!patch) { EditorPrefs.SetBool(FullDoneKey + pkg, true); EditorPrefs.SetBool(FullDoneKey + pkg + ".dev", !release); }   // 릴리스로 덮었으면 다음 패치 전에 개발 빌드부터
            Debug.Log($"[Quest Build] {(patch ? "패치" : release ? "릴리스 빌드" : "전체 빌드")} 완료 — {sw.Elapsed.TotalSeconds:F0}초, 퀘스트에서 실행됩니다. ({serial})");
            ShowNotification($"{(patch ? "패치" : "빌드")} 완료 ({sw.Elapsed.TotalSeconds:F0}초) — 헤드셋에서 실행 중");
            if (EditorPrefs.GetBool(RestorePcKey, false))
                EditorApplication.delayCall += () => EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        }
        else if (patch && s.result == BuildResult.Failed &&
                 EditorUtility.DisplayDialog("Quest 2 빌드", "패치에 실패했습니다. 전체 빌드로 다시 할까요?\n(앱이 지워졌거나 설정이 바뀌면 패치가 안 될 수 있어요)", "전체 빌드", "닫기"))
        {
            EditorPrefs.SetBool(FullDoneKey + pkg, false);
            Run(patch: false);
        }
        else if (s.result != BuildResult.Cancelled)
        {
            Fail($"빌드 실패 (에러 {s.totalErrors}개). Console 창을 확인해 주세요.");
        }
    }

    // ---------- adb ----------

    static string AdbPath => Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe");

    // "device" / "unauthorized" / "offline" / "none"
    static string DeviceState(out string serial)
    {
        serial = null;
        try
        {
            var psi = new ProcessStartInfo(AdbPath, "devices") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                foreach (var line in output.Split('\n').Skip(1))
                {
                    var parts = line.Trim().Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;
                    serial = parts[0];
                    return parts[1];
                }
            }
        }
        catch (Exception e) { Debug.LogWarning("[Quest Build] adb 실행 실패: " + e.Message); }
        return "none";
    }

    static void Fail(string msg)
    {
        Debug.LogWarning("[Quest Build] " + msg);
        EditorUtility.DisplayDialog("Quest 2 빌드", msg, "확인");
    }

    static void ShowNotification(string msg)
    {
        foreach (var w in Resources.FindObjectsOfTypeAll<SceneView>()) w.ShowNotification(new GUIContent(msg), 4f);
    }

    // ---------- 창 ----------

    string deviceState, deviceSerial;
    double nextPoll;

    void OnEnable() => Poll();

    void Poll()
    {
        deviceState = DeviceState(out deviceSerial);
        nextPoll = EditorApplication.timeSinceStartup + 3;
        Repaint();
    }

    void Update()
    {
        if (EditorApplication.timeSinceStartup > nextPoll && !BuildPipeline.isBuildingPlayer) Poll();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(6);
        string label = deviceState == "device" ? $"연결됨  ({deviceSerial})"
                     : deviceState == "unauthorized" ? "헤드셋에서 USB 디버깅 [허용]을 눌러 주세요"
                     : deviceState == "offline" ? "오프라인 — 케이블을 다시 꽂아 주세요"
                     : "연결 안 됨";
        var prev = GUI.color;
        GUI.color = deviceState == "device" ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.8f, 0.5f);
        EditorGUILayout.HelpBox("Quest 2: " + label, MessageType.None);
        GUI.color = prev;

        EditorGUILayout.LabelField("빌드 대상", EditorUserBuildSettings.activeBuildTarget.ToString());
        EditorGUILayout.LabelField("패키지", PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android));
        EditorGUILayout.LabelField("APK", OutputPath);
        EditorGUILayout.Space(6);

        using (new EditorGUI.DisabledScope(BuildPipeline.isBuildingPlayer || Lightmapping.isRunning))
        {
            if (GUILayout.Button("Patch and Run  (Ctrl+Shift+Q)", GUILayout.Height(36))) EditorApplication.delayCall += () => Run(true);
            if (GUILayout.Button("Full Build and Run", GUILayout.Height(26))) EditorApplication.delayCall += () => Run(false);
            if (GUILayout.Button("Release Build and Run (성능 확인용)", GUILayout.Height(26))) EditorApplication.delayCall += () => Run(false, true);
        }
        if (Lightmapping.isRunning) EditorGUILayout.HelpBox("라이트맵을 굽는 중이라 빌드할 수 없어요.", MessageType.Info);

        EditorGUILayout.Space(4);
        bool restore = EditorPrefs.GetBool(RestorePcKey, false);
        bool next = EditorGUILayout.ToggleLeft("빌드 후 빌드 대상을 PC로 되돌리기", restore);
        if (next != restore) EditorPrefs.SetBool(RestorePcKey, next);
        if (GUILayout.Button("기기 다시 확인", EditorStyles.miniButton)) Poll();
    }
}
