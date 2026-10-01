using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 층별 그림 매터리얼 생성 + 공중 앨범 Quad 적용.
// 생성: @Texture/floor_N/picture_N_K → @Matrial/floor_N/Draw_N_K (_BaseMap)
// 적용: y > MinY 인 앨범 중 다른 층 매터리얼이 없는 것에, 세로/가로 비율과 번호 순서대로 1:1 할당
public static class FloorMaterialTools
{
    const float MinY = 12f;

    [MenuItem("Tools/Floor2/Generate Materials")] static void Generate2() => Generate(2);
    [MenuItem("Tools/Floor2/Assign Materials To Albums")] static void Assign2() => Assign(2);
    [MenuItem("Tools/Floor3/Generate Materials")] static void Generate3() => Generate(3);
    [MenuItem("Tools/Floor3/Assign Materials To Albums")] static void Assign3() => Assign(3);

    static string MaterialFolder(int floor) => $"Assets/@Matrial/floor_{floor}";
    static string TextureFolder(int floor) => $"Assets/@Texture/floor_{floor}";

    public static void Generate(int floor)
    {
        Directory.CreateDirectory(MaterialFolder(floor));
        AssetDatabase.Refresh();

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        int created = 0, updated = 0;

        foreach (string guid in AssetDatabase.FindAssets($"picture_{floor}_ t:Texture2D", new[] { TextureFolder(floor) }))
        {
            string texPath = AssetDatabase.GUIDToAssetPath(guid);
            var match = Regex.Match(Path.GetFileNameWithoutExtension(texPath), $@"^picture_{floor}_(\d+)$");
            if (!match.Success) continue;

            string matPath = $"{MaterialFolder(floor)}/Draw_{floor}_{match.Groups[1].Value}.mat";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
                created++;
            }
            else
            {
                updated++;
            }

            mat.SetTexture("_BaseMap", tex);
            mat.mainTexture = tex;
            EditorUtility.SetDirty(mat);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Floor{floor}] 매터리얼 완료: 생성 {created}개, 갱신 {updated}개");
    }

    public static void Assign(int floor)
    {
        // 매터리얼을 원본 텍스처 비율로 세로/가로 분류 (번호 순)
        var verticalMats = new List<Material>();
        var horizontalMats = new List<Material>();
        foreach (var mat in LoadMaterials(floor))
        {
            var tex = mat.GetTexture("_BaseMap");
            if (tex == null) continue;
            // 임포트 시 리사이즈될 수 있으므로 원본 크기로 판별
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
            importer.GetSourceTextureWidthAndHeight(out int w, out int h);
            (h > w ? verticalMats : horizontalMats).Add(mat);
        }

        int applied = Apply(floor, "세로", FindAlbumQuads(floor, "Album_Vertical"), verticalMats)
                    + Apply(floor, "가로", FindAlbumQuads(floor, "Album_Horizontal"), horizontalMats);

        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[Floor{floor}] 앨범 적용 완료: {applied}개 (세로 매터리얼 {verticalMats.Count}, 가로 매터리얼 {horizontalMats.Count})");
    }

    static IEnumerable<Material> LoadMaterials(int floor)
    {
        return AssetDatabase.FindAssets($"Draw_{floor}_ t:Material", new[] { MaterialFolder(floor) })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Material>)
            .Where(m => m != null)
            .OrderBy(m => Number(m.name));
    }

    static List<MeshRenderer> FindAlbumQuads(int floor, string prefix)
    {
        return Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(mf => mf.sharedMesh != null && mf.sharedMesh.name == "Quad")
            .Where(mf => mf.transform.parent != null && mf.transform.parent.name.StartsWith(prefix))
            .Where(mf => mf.transform.position.y > MinY)
            .OrderBy(mf => Number(mf.transform.parent.name))
            .Select(mf => mf.GetComponent<MeshRenderer>())
            .Where(r => r != null && !UsedByOtherFloor(r, floor))
            .ToList();
    }

    // 2층 이상 다른 층 그림(Draw_M_K, M >= 2, M != floor)이 이미 걸린 앨범은 건너뛴다
    static bool UsedByOtherFloor(MeshRenderer r, int floor)
    {
        if (r.sharedMaterial == null) return false;
        var m = Regex.Match(r.sharedMaterial.name, @"^Draw_(\d+)_\d+$");
        if (!m.Success) return false;
        int f = int.Parse(m.Groups[1].Value);
        return f >= 2 && f != floor;
    }

    static int Apply(int floor, string label, List<MeshRenderer> quads, List<Material> mats)
    {
        int n = Mathf.Min(quads.Count, mats.Count);
        for (int i = 0; i < n; i++)
        {
            Undo.RecordObject(quads[i], $"Assign Floor{floor} Material");
            quads[i].sharedMaterial = mats[i];
        }
        if (mats.Count != quads.Count)
            Debug.LogWarning($"[Floor{floor}] {label} 개수 불일치: 매터리얼 {mats.Count}개, 앨범 {quads.Count}개 → {n}개만 적용");
        return n;
    }

    // "Album_Vertical (12)" → 12, "Draw_2_7" → 7, 번호 없으면 0
    static int Number(string name)
    {
        var m = Regex.Match(name, @"(\d+)\)?$");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }
}
