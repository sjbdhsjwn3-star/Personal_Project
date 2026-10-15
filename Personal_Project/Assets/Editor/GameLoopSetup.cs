using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools → Gallery → Setup Game Loop
// 씬에 게임 루프 오브젝트를 만든다: 매니저, 층별 스테이지, 관찰 지점, 숨은 물건/숫자, 계단 문 잠금.
// 다시 실행하면 기존 GameLoop를 지우고 새로 만든다.
public static class GameLoopSetup
{
    static readonly float[] Levels = { 0f, 5.84f, 15.84f, 25.84f };

    // 그림 매터리얼 → 숨은 대상 (u: 왼쪽→오른쪽, v: 위→아래, 0~1)
    struct Spot { public string mat, name; public float u, v; public int digit, order; public bool overlay; }

    static readonly Spot[] Floor1Spots =
    {
        new Spot { mat = "Draw_1_11", name = "어항 자갈 속 작은 열쇠", u = 0.49f, v = 0.83f, digit = -1 },
        new Spot { mat = "Draw_1_8",  name = "산 위의 작은 두 번째 달", u = 0.85f, v = 0.20f, digit = -1 },
        new Spot { mat = "Draw_1_16", name = "풀숲 속 무당벌레", u = 0.86f, v = 0.68f, digit = -1 },
        new Spot { mat = "Draw_1_18", name = "꽃밭 구석의 뼈다귀", u = 0.88f, v = 0.90f, digit = -1 },
        new Spot { mat = "Draw_1_17", name = "연못가 풀숲의 반지", u = 0.64f, v = 0.54f, digit = -1 },
    };

    static readonly Spot[] Floor3Spots =
    {
        new Spot { mat = "Draw_3_18", name = "기묘한 계단", u = 0.30f, v = 0.62f, digit = 2, order = 0, overlay = true },
        new Spot { mat = "Draw_3_5",  name = "책장 / 도서관", u = 0.51f, v = 0.09f, digit = 7, order = 1 },
        new Spot { mat = "Draw_3_6",  name = "밤바다와 돛단배", u = 0.48f, v = 0.17f, digit = 3, order = 2 },
        new Spot { mat = "Draw_3_16", name = "꽃무늬 반복 패턴", u = 0.50f, v = 0.50f, digit = 5, order = 3, overlay = true },
    };

    [MenuItem("Tools/Gallery/Setup Game Loop")]
    public static void Setup()
    {
        var old = GameObject.Find("GameLoop");
        if (old != null) Undo.DestroyObjectImmediate(old);

        var root = new GameObject("GameLoop");
        Undo.RegisterCreatedObjectUndo(root, "Setup Game Loop");
        var manager = root.AddComponent<GameLoopManager>();

        var player = GameObject.Find("Base Scene/First Person Player");
        manager.player = player != null ? player.transform : null;
        manager.playerCamera = player != null ? player.GetComponentInChildren<Camera>() : Camera.main;
        if (player != null)
            foreach (var b in player.GetComponentsInChildren<Behaviour>(true))
                if (b != null && (b.GetType().Name == "MouseLook" || b.GetType().Name == "PlayerMovement"))
                    manager.disableWhileUIOpen.Add(b);

        var quads = FindPictureQuads();

        // 1층: 관찰 + 숨은 물건 찾기
        var s1 = MakeStage(root, 1, quads);
        var q1 = MakeQuiz<FindObjectsQuiz>(s1, "숨은 물건 찾기", "그림 속에 숨겨진 물건 5개를 모두 찾아 클릭하세요.");
        foreach (var sp in Floor1Spots) MakeTarget(q1.transform, quads, sp);

        // 2층: 기억 퀴즈
        var s2 = MakeStage(root, 2, quads);
        var q2 = MakeQuiz<MemoryQuiz>(s2, "기억 퀴즈", "2층에 전시된 그림들을 떠올리며, 무엇이 달라졌는지 맞히세요.");
        q2.passCount = 4;
        q2.questions = new List<MemoryQuiz.QuizItem>
        {
            Q("바나나 그림에서 달라진 것은?", 0, "바나나 잎이 초록색에서 노란색이 되었다", "인물의 표정이 웃는 얼굴이 되었다", "바나나 개수가 줄었다"),
            Q("노란 우산을 든 여자 그림에서 달라진 것은?", 1, "우산 색이 파란색이 되었다", "뒤쪽 배경 색이 바뀌었다", "달이 사라졌다"),
            Q("백조 그림에서 달라진 것은?", 2, "백조가 세 마리가 되었다", "호수가 얼어붙었다", "한 마리가 반대쪽을 보고 있다"),
            Q("비눗방울 그림에서 달라진 것은?", 0, "동그란 비눗방울이 사라졌다", "강아지 무늬가 바뀌었다", "아이의 모자가 사라졌다"),
            Q("호숫가의 옆모습 여자 그림에서 달라진 것은?", 1, "정면을 보게 되었다", "풀어 내린 머리가 반묶음이 되었다", "호수에 배가 생겼다"),
        };

        // 3층: 숫자 조합 비밀번호
        var s3 = MakeStage(root, 3, quads);
        var q3 = MakeQuiz<CodeQuiz>(s3, "탈출 비밀번호", "각 그림 속에 숨은 숫자를 찾아 조합한 뒤 비밀번호를 입력하세요.");
        foreach (var sp in Floor3Spots) MakeTarget(q3.transform, quads, sp);

        // 계단실 문 막이 (문을 열기 전까지 통로를 막음)
        MakeBlocker(s2.transform, "Blocker_StairDoor_F2", Levels[1]);
        MakeBlocker(s3.transform, "Blocker_StairDoor_F3", Levels[2]);

        manager.stages = new List<FloorStage> { s1, s2, s3 };
        SetupDoors();

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log($"[GameLoop] 설정 완료 — 관찰 지점 1층 {Count(s1)}, 2층 {Count(s2)}, 3층 {Count(s3)} / 숨은 물건 {q1.GetComponentsInChildren<QuizClickTarget>().Length} / 숫자 단서 {q3.GetComponentsInChildren<QuizClickTarget>().Length}");
    }

    // Tools → Gallery → Setup Interact Doors
    // 기존 GameLoop에 상호작용 문(E/F)을 추가한다. 계단 문 막이는 스테이지 대신 문이 관리한다.
    [MenuItem("Tools/Gallery/Setup Interact Doors")]
    public static void SetupDoors()
    {
        var root = GameObject.Find("GameLoop");
        if (root == null) { Debug.LogWarning("[GameLoop] 먼저 Setup Game Loop를 실행하세요."); return; }
        var m = root.GetComponent<GameLoopManager>();
        var stages = m.stages;

        var old = root.transform.Find("Doors");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var doors = new GameObject("Doors");
        Undo.RegisterCreatedObjectUndo(doors, "Setup Doors");
        doors.transform.SetParent(root.transform, false);

        GameObject Blocker(string name)
        {
            foreach (var b in root.GetComponentsInChildren<BoxCollider>(true)) if (b.name == name) return b.gameObject;
            return null;
        }

        var made = new List<string>();
        void Make(string name, string leafPath, FloorStage required, string blockerName, bool exit, string openText, string openedToast)
        {
            var leaf = GameObject.Find(leafPath);
            if (leaf == null) { Debug.LogWarning($"[GameLoop] 문짝을 찾지 못함: {leafPath}"); return; }
            var go = new GameObject(name);
            go.transform.SetParent(doors.transform, false);
            go.transform.position = leaf.transform.position;
            var d = go.AddComponent<InteractDoor>();
            d.doorLeaf = leaf.transform;
            d.requiredStage = required;
            d.isExit = exit;
            d.promptOpen = openText;
            d.openedToast = openedToast;
            // 키트의 클릭식 문 스크립트가 있으면: 그 애니메이션을 쓰고, 스크립트는 꺼서 퀴즈 없이 열리지 않게
            foreach (var mb in leaf.GetComponents<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "opencloseDoor") continue;
                d.animator = mb.GetType().GetField("openandclose")?.GetValue(mb) as Animator;
                Undo.RecordObject(mb, "Setup Doors");
                mb.enabled = false;
            }
            var blocker = blockerName != null ? Blocker(blockerName) : null;
            if (blocker != null)
            {
                d.blockers.Add(blocker);
                blocker.SetActive(true);
                foreach (var s in stages) s.blockersWhileLocked.Remove(blocker);   // 문이 직접 관리
            }
            made.Add(name);
        }

        // 계단 문 막이는 항상 켜 둠 (층 이동은 순간이동으로만, 계단으로 우회 불가)
        foreach (var bn in new[] { "Blocker_StairDoor_F2", "Blocker_StairDoor_F3" })
        {
            var b = Blocker(bn);
            if (b == null) continue;
            b.SetActive(true);
            foreach (var s in stages) s.blockersWhileLocked.Remove(b);
        }

        // 층마다 같은 자리(1층 계단실 서쪽 문과 같은 x·z)에 퀴즈 문
        EnsureUpperQuizDoors();
        string[] leafPaths =
        {
            "Structure_02/Staricases_01_Setup/Staircase_Base/Doors Stairs/DoorFrame_Apt_04/Door",
            "Structure_02/Staricases_01_Setup/Stairs_Rebuilt/QuizDoorFrame_F2/Door",
            "Structure_02/Staricases_01_Setup/Stairs_Rebuilt/QuizDoorFrame_F3/Door",
        };
        for (int i = 0; i < 3; i++)
        {
            var leaf = GameObject.Find(leafPaths[i]);
            if (leaf == null) { Debug.LogWarning("[GameLoop] 문짝 없음: " + leafPaths[i]); continue; }
            foreach (var mb in leaf.GetComponents<MonoBehaviour>())
                if (mb != null && mb.GetType().Name == "opencloseDoor") { Undo.RecordObject(mb, "Setup Doors"); mb.enabled = false; }
            var go = new GameObject($"QuizDoor_F{i + 1}");
            go.transform.SetParent(doors.transform, false);
            go.transform.position = leaf.transform.position;
            var qd = go.AddComponent<QuizDoor>();
            qd.stage = stages[i];
            qd.focus = leaf.transform;
            made.Add(go.name);
        }

        // 스폰 위치 (플레이어 몸 중심 = 바닥 + 1.0)
        SetSpawn(stages[0], "Spawn_F1", new Vector3(9.78f, 1.00f, -2.61f), 270f);     // 시작 위치 (출입문 밖)
        SetSpawn(stages[1], "Spawn_F2", new Vector3(2.00f, 6.94f, -5.90f), 270f);     // 2층 동쪽 벽 앞, 돌 의자 옆
        SetSpawn(stages[2], "Spawn_F3", new Vector3(-8.00f, 16.94f, 3.00f), 0f);      // 3층 전시실 가운데, 북쪽 벽을 바라봄
        var fin = new GameObject("Spawn_Final");
        fin.transform.SetParent(doors.transform, false);
        fin.transform.SetPositionAndRotation(new Vector3(4.20f, 1.10f, -2.50f), Quaternion.Euler(0, 90f, 0));   // 1층 출입문 안쪽
        m.finalSpawnPoint = fin.transform;
        // 1층 출입문: 처음부터 E로 열림 (입장용)
        Make("Door_Entrance", "Structure_02/Ext Into Int/IntExt_apt_DoorFrame_02_01/Glass.003", null, null, false,
             "[ E ]  문 열기", "미술관에 들어왔어요. 그림을 둘러보세요.");

        // 탈출: 3층 퀴즈를 푼 뒤 출입문 밖(건물 동쪽 바깥)으로 나가면 클리어
        var exit = new GameObject("ExitZone");
        exit.transform.SetParent(doors.transform, false);
        exit.transform.position = new Vector3(9.5f, 1.5f, -2.5f);
        exit.AddComponent<BoxCollider>().size = new Vector3(5f, 4f, 8f);   // x 7~12 (출입문 바로 바깥)
        exit.AddComponent<ExitZone>().requiredStage = stages[2];
        made.Add("ExitZone");

        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("[GameLoop] 상호작용 문 설정: " + string.Join(", ", made));
    }

    static void SetSpawn(FloorStage stage, string name, Vector3 pos, float yaw)
    {
        var old = stage.transform.Find(name);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var go = new GameObject(name);
        go.transform.SetParent(stage.transform, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        stage.spawnPoint = go.transform;
    }

    // 1층 계단실 서쪽 문(Staircase_Base)을 2층·3층 같은 x·z에 복제한다. 이미 있으면 그대로 둔다.
    static void EnsureUpperQuizDoors()
    {
        var stairs = GameObject.Find("Structure_02/Staricases_01_Setup/Stairs_Rebuilt");
        var f1 = GameObject.Find("Structure_02/Staricases_01_Setup/Staircase_Base/Doors Stairs/DoorFrame_Apt_04");
        if (stairs == null || f1 == null) { Debug.LogWarning("[GameLoop] 1층 계단실 문을 찾지 못함"); return; }

        // 예전 방식(1층 동쪽 문)으로 만든 오브젝트가 있으면 정리
        var oldDoor = stairs.transform.Find("StairDoor_0.00"); if (oldDoor != null) Undo.DestroyObjectImmediate(oldDoor.gameObject);
        var oldWall = stairs.transform.Find("Stairwell_Walls/F1_StairDoor"); if (oldWall != null) Undo.DestroyObjectImmediate(oldWall.gameObject);

        foreach (var (name, levelY, layers) in new[] { ("QuizDoorFrame_F2", Levels[1], 1u | 4u), ("QuizDoorFrame_F3", Levels[2], 1u | 8u) })
        {
            if (stairs.transform.Find(name) != null) continue;
            var copy = Object.Instantiate(f1, stairs.transform);
            copy.name = name;
            copy.transform.SetPositionAndRotation(f1.transform.position + Vector3.up * levelY, f1.transform.rotation);
            Undo.RegisterCreatedObjectUndo(copy, "Quiz doors");
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask = layers;   // 층별 조명 레이어
        }
    }

    static int Count(FloorStage s) => s.GetComponentsInChildren<ObservationPoint>().Length;

    static MemoryQuiz.QuizItem Q(string text, int answer, params string[] choices)
        => new MemoryQuiz.QuizItem { text = text, choices = choices, answerIndex = answer };

    // 건물 안(x < 6.6)에 걸린 앨범의 그림(Quad)
    static List<Renderer> FindPictureQuads()
    {
        return Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(r => r.name == "Quad" && r.transform.parent != null && r.transform.parent.name.StartsWith("Album_"))
            .Where(r => r.bounds.center.x < 6.6f)
            .Cast<Renderer>()
            .ToList();
    }

    static FloorStage MakeStage(GameObject root, int floor, List<Renderer> quads)
    {
        var go = new GameObject($"Stage_F{floor}");
        go.transform.SetParent(root.transform, false);
        var stage = go.AddComponent<FloorStage>();
        stage.floorNumber = floor;
        stage.minY = Levels[floor - 1] - 0.5f;
        stage.maxY = Levels[floor] - 0.5f;
        stage.requiredRatio = 0.8f;

        var points = new GameObject("ObservationPoints").transform;
        points.SetParent(go.transform, false);
        foreach (var q in quads.Where(q => q.bounds.center.y >= Levels[floor - 1] && q.bounds.center.y < Levels[floor]))
        {
            var p = new GameObject("Observe_" + q.transform.parent.name);
            p.transform.SetParent(points, false);
            p.transform.position = q.bounds.center;
            var op = p.AddComponent<ObservationPoint>();
            float size = Mathf.Max(q.bounds.size.x, q.bounds.size.y, q.bounds.size.z);
            op.radius = Mathf.Clamp(size * 2.2f, 3f, 9f);   // 큰 그림은 멀리서도 관찰 인정
        }
        return stage;
    }

    static T MakeQuiz<T>(FloorStage stage, string title, string question) where T : FloorQuiz
    {
        var go = new GameObject("Quiz");
        go.transform.SetParent(stage.transform, false);
        var quiz = go.AddComponent<T>();
        var so = new SerializedObject(quiz);
        so.FindProperty("title").stringValue = title;
        so.FindProperty("question").stringValue = question;
        so.ApplyModifiedPropertiesWithoutUndo();
        stage.quiz = quiz;
        return quiz;
    }

    static void MakeTarget(Transform parent, List<Renderer> quads, Spot sp)
    {
        var quad = quads.FirstOrDefault(q => q.sharedMaterial != null && q.sharedMaterial.name == sp.mat);
        if (quad == null) { Debug.LogWarning($"[GameLoop] '{sp.mat}' 그림을 찾지 못함: {sp.name}"); return; }

        // 그림 위 UV 위치 → 월드 좌표 (Quad 로컬 -0.5~0.5), 그림 앞으로 조금 띄움
        var t = quad.transform;
        var world = t.TransformPoint(new Vector3(sp.u - 0.5f, 0.5f - sp.v, 0f));
        var front = -t.forward;   // Quad의 보이는 면(-Z)이 방 쪽

        var go = new GameObject((sp.digit >= 0 ? "Digit_" : "Hidden_") + sp.name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(world + front * 0.15f, Quaternion.LookRotation(t.forward, t.up));
        float w = Mathf.Max(quad.bounds.size.x, quad.bounds.size.z);
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(Mathf.Clamp(w * 0.14f, 0.2f, 0.6f), Mathf.Clamp(w * 0.14f, 0.2f, 0.6f), 0.3f);

        var target = go.AddComponent<QuizClickTarget>();
        target.displayName = sp.name;
        target.digit = sp.digit;
        target.digitOrder = sp.order;

        // 그림에 원래 숫자가 없는 경우, 음각처럼 옅은 숫자를 그림 위에 얹는다
        if (sp.overlay)
        {
            var txt = new GameObject("EngravedDigit");
            txt.transform.SetParent(go.transform, false);
            txt.transform.position = world + front * 0.004f;
            txt.transform.rotation = Quaternion.LookRotation(t.forward, t.up);
            var tm = txt.AddComponent<TextMesh>();
            tm.text = sp.digit.ToString();
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.fontSize = 120; tm.characterSize = w * 0.006f;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.25f, 0.25f, 0.25f, 0.55f);
            txt.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        }
    }

    // 계단실 동쪽 문(층 도착 지점)을 막는 보이지 않는 벽
    static GameObject MakeBlocker(Transform parent, string name, float floorY)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(-6.25f, floorY + 1.6f, -5.0f);
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(0.6f, 3.2f, 2.2f);
        return go;
    }
}
