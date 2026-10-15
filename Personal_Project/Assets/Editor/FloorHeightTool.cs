using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 층고 통일: 모든 층을 3층 층고(10m)에 맞춘다.
//  - 바닥·가구·앨범 등: 자기 층 바닥 이동량만큼 이동 (바닥 접지·벽 밀착 유지, 크기 그대로)
//  - 천장·지붕·매다는 조명: 자기 층 천장 이동량만큼 이동 (천장과의 간격 유지)
//  - 천장까지 닿는 벽 조각: 이동 후, 같은 조각을 위로 쌓아 늘어난 높이를 채움
//    (늘리지 않으므로 텍스처 비율 유지, 마지막 조각은 UV를 함께 잘라 비율 유지)
//  - 문·창이 뚫린 조각은 같은 크기의 막힌 조각으로 쌓음
//  - 계단(Staricases_01_Setup)은 건드리지 않음
public static class FloorHeightTool
{
    // 기존 층 경계 (바닥 높이) → 새 경계
    static readonly float[] OldLevels = { 0f, 4f, 11.19f, 21.19f };
    static readonly float[] NewLevels = { 0f, 10f, 20f, 30f };
    const float CeilingTolerance = 0.7f;   // 벽 상단이 천장에서 이 이내면 '천장까지 닿는 벽'
    const string CropFolder = "Assets/@Meshes/Cropped";
    const string Floor1TopKey = "Gallery.Floor1Top";   // 마지막으로 적용한 1층 천장 높이

    // 최초 1회 실행용 (이미 적용됨 — 다시 실행하면 층이 또 올라가므로 메뉴에서 뺌)
    public static void Run()
    {
        var structure = GameObject.Find("Structure_02").transform;
        var interior = structure.Find("Interior");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        var log = new List<string>();

        // 1) 3층은 층고가 기준이므로 통째로 이동
        var floor3 = interior.Find("3rd  Floor");
        Move(floor3, NewLevels[2] - OldLevels[2]);
        log.Add($"3rd Floor 전체 +{NewLevels[2] - OldLevels[2]:0.00}");

        // 2) 1·2층 구성 요소 (각 컨테이너의 직계 자식 단위)
        var containers = CollectFloor12Parts(structure);

        var plainByShape = BuildPlainMeshTable();
        int moved = 0, stacked = 0, skipped = 0;
        foreach (var t in containers)
        {
            if (!TryGetBounds(t, out var b))
            {
                skipped++;
                continue;
            }
            // 건물 밖(공중 전시 등)은 제외
            if (b.center.x > 6.6f || b.center.y >= OldLevels[2]) { skipped++; continue; }

            int floor = b.center.y < OldLevels[1] ? 0 : 1;
            float baseDelta = NewLevels[floor] - OldLevels[floor];
            float topDelta = NewLevels[floor + 1] - OldLevels[floor + 1];
            string path = PathOf(t);

            if (IsCeilingAttached(t, path))
            {
                Move(t, topDelta);
                moved++;
                continue;
            }

            Move(t, baseDelta);
            moved++;

            float oldTop = b.max.y;
            bool reachesCeiling = Mathf.Abs(oldTop - OldLevels[floor + 1]) < CeilingTolerance;
            if (IsWall(t, path) && reachesCeiling && t.gameObject.activeInHierarchy)
            {
                float extra = (NewLevels[floor + 1] - NewLevels[floor]) - (OldLevels[floor + 1] - OldLevels[floor]);
                stacked += Stack(t, extra, plainByShape);
            }
        }

        EditorSceneManager.MarkSceneDirty(structure.gameObject.scene);
        Undo.CollapseUndoOperations(group);
        log.Add($"1·2층: 이동 {moved}개, 쌓은 벽 조각 {stacked}개, 제외 {skipped}개");
        Debug.Log("[FloorHeight] " + string.Join(" / ", log));
    }

    // 1층 층고를 선택한 오브젝트(예: 가벽)의 상단 높이에 맞춘다.
    //  - 1층에 쌓았던 'Upper' 벽 조각을 지우고, 원래 4m 벽 위에 새 높이만큼 다시 쌓음
    //  - 1층 천장 부착물, 2·3층 전체를 높이 차이만큼 이동
    [MenuItem("Tools/Gallery/Set Floor 1 Height To Selected Top")]
    static void SetFloor1HeightFromSelection()
    {
        var sel = Selection.activeTransform;
        if (sel == null || !TryGetBounds(sel, out var b))
        {
            EditorUtility.DisplayDialog("Floor 1 Height", "기준이 될 1층 오브젝트(가벽 등)를 선택하세요.", "OK");
            return;
        }
        SetFloor1Height(b.max.y);
    }

    public static void SetFloor1Height(float newTop)
    {
        const float originalWallTop = 4f;   // 키트 원래 1층 벽 높이
        var structure = GameObject.Find("Structure_02").transform;
        float curTop = EditorPrefs.GetFloat(Floor1TopKey, NewLevels[1]);   // 현재 1층 천장(= 2층 바닥 기준선)
        float d = newTop - curTop;

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();

        Move(structure.Find("Interior/3rd  Floor"), d);

        var plainByShape = BuildPlainMeshTable();
        var restack = new List<Transform>();
        int moved = 0, removed = 0, stacked = 0;
        foreach (var t in CollectFloor12Parts(structure))
        {
            if (t == null || !TryGetBounds(t, out var b) || b.center.x > 6.6f) continue;
            string path = PathOf(t);
            bool floor1 = b.center.y < curTop - 0.01f;

            if (floor1 && IsWall(t, path) && (t.name.Contains(" Upper") || t.name.StartsWith("IntExt_Above")))
            {
                Undo.DestroyObjectImmediate(t.gameObject);
                removed++;
                continue;
            }
            if (!floor1 || IsCeilingAttached(t, path))
            {
                Move(t, d);
                moved++;
                continue;
            }
            if (IsWall(t, path) && t.gameObject.activeInHierarchy && Mathf.Abs(b.max.y - originalWallTop) < CeilingTolerance)
                restack.Add(t);
        }
        foreach (var t in restack) stacked += Stack(t, newTop - originalWallTop, plainByShape);

        EditorPrefs.SetFloat(Floor1TopKey, newTop);
        EditorSceneManager.MarkSceneDirty(structure.gameObject.scene);
        Undo.CollapseUndoOperations(group);
        Debug.Log($"[FloorHeight] 1층 층고 {curTop:0.00} → {newTop:0.00} (차이 {d:+0.00;-0.00}): 이동 {moved}, 삭제 {removed}, 다시 쌓음 {stacked}");
    }

    // 1·2층 구성 요소 (각 컨테이너의 직계 자식 단위) + 씬 최상위의 낱개 오브젝트
    static List<Transform> CollectFloor12Parts(Transform structure)
    {
        var list = new List<Transform>();
        void AddChildrenOf(Transform t) { if (t != null) foreach (Transform c in t) list.Add(c); }
        var interior = structure.Find("Interior");
        var lobby = interior.Find("Lobby");
        foreach (Transform g in lobby) if (g.name != "Arts") AddChildrenOf(g);
        AddChildrenOf(lobby.Find("Arts"));
        foreach (Transform part in interior.Find("2nd Floor")) foreach (Transform g in part) AddChildrenOf(g);
        AddChildrenOf(structure.Find("Exterior/Walls/1st Floor"));
        AddChildrenOf(structure.Find("Exterior/Base Roof"));
        AddChildrenOf(structure.Find("Ext Into Int"));
        foreach (var root in structure.gameObject.scene.GetRootGameObjects())
            if (root.transform != structure && root.name != "Base Scene") list.Add(root.transform);
        return list;
    }

    static void Move(Transform t, float dy)
    {
        if (Mathf.Abs(dy) < 1e-4f) return;
        Undo.RecordObject(t, "Unify Floor Heights");
        t.position += Vector3.up * dy;
    }

    static string PathOf(Transform t)
    {
        string p = t.name;
        for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
        return p;
    }

    static bool IsCeilingAttached(Transform t, string path)
    {
        string n = t.name.ToLowerInvariant();
        return path.Contains("/Ceiling/") || path.Contains("/Base Roof/") || path.Contains("/Lighting/") || path.Contains("/Lights/")
               || n.Contains("ceiling") || n.StartsWith("light");
    }

    static bool IsWall(Transform t, string path)
    {
        return path.Contains("/Walls/") || path.Contains("Ext Into Int/");
    }

    // 비활성 오브젝트도 포함해 메쉬 기준 월드 bounds 계산 (렌더러가 없으면 위치만)
    static bool TryGetBounds(Transform t, out Bounds b)
    {
        b = default;
        bool has = false;
        foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = mf.transform.TransformPoint(c);
                if (!has) { b = new Bounds(p, Vector3.zero); has = true; } else b.Encapsulate(p);
            }
        }
        if (!has && (t.GetComponentInChildren<Light>(true) != null))
        {
            b = new Bounds(t.position, Vector3.zero);
            has = true;
        }
        return has;
    }

    // 같은 크기의 메쉬 중 꼭짓점이 가장 적은 것 = 문·창 없는 막힌 조각
    static Dictionary<string, Mesh> BuildPlainMeshTable()
    {
        var table = new Dictionary<string, Mesh>();
        foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var m = mf.sharedMesh;
            if (m == null || !m.name.Contains("Wall")) continue;
            string key = ShapeKey(m);
            if (!table.TryGetValue(key, out var cur) || m.vertexCount < cur.vertexCount) table[key] = m;
        }
        return table;
    }

    static string ShapeKey(Mesh m) => m.bounds.size.ToString("0.00");

    // t의 메쉬를 위로 쌓아 extra 높이만큼 채운다. 생성한 조각 수 반환
    static int Stack(Transform t, float extra, Dictionary<string, Mesh> plainByShape)
    {
        var mf = t.GetComponent<MeshFilter>();
        var mr = t.GetComponent<MeshRenderer>();
        if (mf == null || mf.sharedMesh == null || mr == null || extra <= 0.01f) return 0;

        var mesh = mf.sharedMesh;
        if (t.name.Contains("Door") || t.name.StartsWith("IntExt"))
        {
            Debug.LogWarning($"[FloorHeight] 문 조각은 쌓지 않음 (직접 확인 필요): {PathOf(t)}");
            return 0;
        }
        if (plainByShape.TryGetValue(ShapeKey(mesh), out var plain) && plain.vertexCount < mesh.vertexCount) mesh = plain;

        // 월드 위쪽과 가장 가까운 로컬 축 (회전된 조각도 처리)
        Vector3 localUp = t.InverseTransformDirection(Vector3.up);
        int axis = 0;
        for (int i = 1; i < 3; i++) if (Mathf.Abs(localUp[i]) > Mathf.Abs(localUp[axis])) axis = i;
        if (Mathf.Abs(localUp.normalized[axis]) < 0.99f) return 0;   // 기울어진 조각은 건너뜀
        int sign = localUp[axis] > 0 ? 1 : -1;

        float pieceHeight = mesh.bounds.size[axis] * Mathf.Abs(t.lossyScale[axis]);
        if (pieceHeight < 0.1f) return 0;

        int count = 0;
        float filled = 0f;
        while (filled < extra - 0.001f)
        {
            float h = Mathf.Min(pieceHeight, extra - filled);
            var piece = new GameObject(t.name + " Upper" + (count == 0 ? "" : " " + (count + 1)));
            Undo.RegisterCreatedObjectUndo(piece, "Unify Floor Heights");
            piece.transform.SetParent(t.parent, false);
            piece.transform.SetPositionAndRotation(t.position + Vector3.up * (pieceHeight + filled), t.rotation);
            piece.transform.localScale = t.localScale;
            piece.layer = t.gameObject.layer;
            GameObjectUtility.SetStaticEditorFlags(piece, GameObjectUtility.GetStaticEditorFlags(t.gameObject));

            var pieceMesh = h < pieceHeight - 0.001f ? CropTop(mesh, axis, sign, h / pieceHeight) : mesh;
            piece.AddComponent<MeshFilter>().sharedMesh = pieceMesh;
            piece.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
            if (t.GetComponent<Collider>() != null) piece.AddComponent<BoxCollider>();

            filled += h;
            count++;
        }
        return count;
    }

    // 메쉬 높이를 ratio만큼만 남기고 위를 자른다. UV도 같은 비율로 잘라 텍스처가 늘어나지 않게 함
    static readonly Dictionary<string, Mesh> cropCache = new Dictionary<string, Mesh>();
    // axis: 월드 위쪽에 해당하는 로컬 축, sign: 그 축이 위를 향하면 +1, 아래를 향하면 -1
    public static Mesh CropTop(Mesh src, int axis, int sign, float ratio)
    {
        string key = $"{src.name}_{"XYZ"[axis]}{(sign > 0 ? "+" : "-")}_{ratio:0.000}";
        if (cropCache.TryGetValue(key, out var cached) && cached != null) return cached;

        string path = $"{CropFolder}/{key}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return cropCache[key] = existing;

        var m = Object.Instantiate(src);
        m.name = key;
        var v = m.vertices;
        var uv = m.uv;
        // 높이 좌표 h = 축 값 x sign (아래 → 위로 증가)
        float lo = sign > 0 ? src.bounds.min[axis] : -src.bounds.max[axis];
        float cut = lo + src.bounds.size[axis] * ratio;

        // 세로면(법선이 높이 축과 수직)에서 UV.v가 높이에 따라 변하는 기울기를 최소제곱으로 구함
        var n = m.normals;
        double sy = 0, sv = 0, syy = 0, syv = 0; int cnt = 0;
        for (int i = 0; i < v.Length; i++)
        {
            if (uv.Length == 0 || Mathf.Abs(n[i][axis]) > 0.5f) continue;
            float hgt = v[i][axis] * sign;
            sy += hgt; sv += uv[i].y; syy += hgt * hgt; syv += hgt * uv[i].y; cnt++;
        }
        double denom = cnt * syy - sy * sy;
        float slope = cnt > 1 && System.Math.Abs(denom) > 1e-9 ? (float)((cnt * syv - sy * sv) / denom) : 0f;

        for (int i = 0; i < v.Length; i++)
        {
            float hgt = v[i][axis] * sign;
            if (hgt <= cut) continue;
            float d = hgt - cut;
            var p = v[i];
            p[axis] = cut * sign;
            v[i] = p;
            if (uv.Length > 0 && Mathf.Abs(n[i][axis]) <= 0.5f) uv[i].y -= d * slope;
        }
        m.vertices = v;
        if (uv.Length > 0) m.uv = uv;
        m.RecalculateBounds();

        Directory.CreateDirectory(CropFolder);
        AssetDatabase.CreateAsset(m, path);
        return cropCache[key] = m;
    }
}
