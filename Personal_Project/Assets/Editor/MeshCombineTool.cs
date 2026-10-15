using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// 선택한 오브젝트(와 그 하위)의 메쉬들을 하나의 메쉬로 합친다.
// - 기본: 매터리얼별로 서브메쉬를 나눠 원래 재질을 유지
// - One Material: 서브메쉬 1개 + 전용 매터리얼 복사본 (층별 텍스처 교체용)
// - 합친 메쉬는 Assets/@Meshes 에 저장, MeshCollider 추가
// - 원본은 삭제하지 않고 비활성화 (Ctrl+Z 또는 다시 켜서 복구 가능)
public static class MeshCombineTool
{
    const string MeshFolder = "Assets/@Meshes";

    const string MaterialFolder = "Assets/@Matrial";

    [MenuItem("Tools/Gallery/Combine Selected Meshes")]
    static void CombineSelected() => CombineSelection(false);

    // 매터리얼 슬롯 1개로 합치고 전용 매터리얼 복사본을 만든다 (층별로 텍스처를 따로 넣을 때)
    [MenuItem("Tools/Gallery/Combine Selected Meshes (One Material)")]
    static void CombineSelectedOneMaterial() => CombineSelection(true);

    static void CombineSelection(bool oneMaterial)
    {
        var roots = Selection.gameObjects.Where(g => g.scene.IsValid()).ToArray();
        if (roots.Length == 0)
        {
            EditorUtility.DisplayDialog("Combine Meshes", "Hierarchy에서 합칠 오브젝트를 선택하세요.", "OK");
            return;
        }
        Combine(roots, roots[0].name + "_Combined", oneMaterial);
    }

    public static GameObject Combine(GameObject[] roots, string name, bool oneMaterial)
    {
        // 한 매터리얼 모드: 가장 많이 쓰인 매터리얼을 복사해 이 메쉬 전용으로 사용
        Material single = null;
        if (oneMaterial)
        {
            var mostUsed = roots.SelectMany(r => r.GetComponentsInChildren<MeshRenderer>(false))
                .SelectMany(r => r.sharedMaterials).Where(m => m != null)
                .GroupBy(m => m).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            if (mostUsed != null)
            {
                Directory.CreateDirectory(MaterialFolder);
                single = new Material(mostUsed);
                AssetDatabase.CreateAsset(single, AssetDatabase.GenerateUniqueAssetPath($"{MaterialFolder}/{name}.mat"));
            }
        }

        Transform parent = roots[0].transform.parent;
        Matrix4x4 toParent = parent != null ? parent.worldToLocalMatrix : Matrix4x4.identity;

        // 매터리얼별로 CombineInstance 모으기
        var byMaterial = new Dictionary<Material, List<CombineInstance>>();
        GameObject staticSource = null;
        int pieces = 0;
        foreach (var root in roots)
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(false))
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || !mr.enabled) continue;
            staticSource ??= mr.gameObject;
            pieces++;
            var mats = mr.sharedMaterials;
            for (int i = 0; i < mf.sharedMesh.subMeshCount; i++)
            {
                var mat = single != null ? single : mats[Mathf.Min(i, mats.Length - 1)];
                if (!byMaterial.TryGetValue(mat, out var list)) byMaterial[mat] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = i, transform = toParent * mf.transform.localToWorldMatrix });
            }
        }
        if (pieces == 0) return null;

        // 매터리얼마다 1개 서브메쉬로 합친 뒤, 서브메쉬들을 최종 메쉬로
        var materials = new List<Material>();
        var parts = new List<CombineInstance>();
        foreach (var kv in byMaterial)
        {
            var part = new Mesh { indexFormat = IndexFormat.UInt32 };
            part.CombineMeshes(kv.Value.ToArray(), true, true);
            parts.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
            materials.Add(kv.Key);
        }
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(parts.ToArray(), false, false);
        mesh.RecalculateBounds();
        foreach (var p in parts) Object.DestroyImmediate(p.mesh);

        Directory.CreateDirectory(MeshFolder);
        string path = AssetDatabase.GenerateUniqueAssetPath($"{MeshFolder}/{name}.asset");
        AssetDatabase.CreateAsset(mesh, path);
        AssetDatabase.SaveAssets();

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Combine Meshes");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        go.layer = staticSource.layer;
        GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(staticSource));
        go.transform.SetSiblingIndex(roots[0].transform.GetSiblingIndex());

        foreach (var root in roots)
        {
            Undo.RecordObject(root, "Combine Meshes");
            root.SetActive(false);
        }

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log($"[Combine] {pieces}개 조각 → 1개 메쉬 ({mesh.vertexCount} verts, 매터리얼 {materials.Count}개): {path}");
        return go;
    }
}
