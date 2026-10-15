using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// 천장 레일 + 와이어 + 앨범을 한 몸으로 제어하는 갤러리 디스플레이.
// 이 오브젝트(Parent)의 위치 = 벽면 위 천장 레일 연결점(Pivot).
// 로컬 축: +Z = 벽 안쪽(앨범 뒷면 방향), -Y = 아래.
//   Parent
//   ├─ RailBar    : 천장에 붙은 가로 레일 (Y 0 ~ -railThickness)
//   ├─ Wire(2)    : 레일 하단 ~ 앨범 상단
//   └─ AlbumFrame : 앨범 (상단 = -heightDistance, 뒷면 = 벽에서 wallOffset)
[ExecuteAlways]
[DisallowMultipleComponent]
public class GalleryDisplay : MonoBehaviour
{
    public enum AlbumOrientation { Horizontal, Vertical }

    [Header("Album")]
    public Transform album;
    public AlbumOrientation orientation = AlbumOrientation.Vertical;
    [Tooltip("세로 모드 앨범 크기 (가로 폭, 세로 높이). 가로 모드는 이 값을 뒤집어 사용")]
    public Vector2 verticalSize = new Vector2(3.1f, 4.9f);

    [Header("Placement")]
    [Tooltip("천장(레일 상단)에서 앨범 상단까지 거리")]
    [Range(0.2f, 9f)] public float heightDistance = 2.25f;
    [Tooltip("벽면에서 앨범 뒷면까지 거리")]
    [Range(0f, 0.5f)] public float wallOffset = 0.04f;

    [Header("Wire")]
    [Range(0.002f, 0.05f)] public float wireThickness = 0.015f;
    [Tooltip("와이어 2줄 간격 = 앨범 폭 x 이 비율")]
    [Range(0f, 1f)] public float wireSpacingRatio = 0.7f;

    [Header("Rail")]
    [Range(0.01f, 0.2f)] public float railThickness = 0.04f;
    [Tooltip("레일이 앨범 양옆으로 더 나가는 길이")]
    [Range(0f, 2f)] public float railMargin = 0.3f;

    [Header("Material (비워두면 어두운 메탈 자동 적용)")]
    public Material metalMaterial;

    [HideInInspector] public Transform railBar, wire, wire2;

    static Material defaultMaterial;

    public Vector2 AlbumSize => orientation == AlbumOrientation.Vertical
        ? verticalSize
        : new Vector2(verticalSize.y, verticalSize.x);

    // 컴포넌트를 처음 붙일 때만 자동 구성 (씬 로드·재컴파일 때는 위치를 건드리지 않음)
    void Reset() => Apply();

#if UNITY_EDITOR
    // OnValidate 안에서 Transform을 바로 바꾸면 경고가 나므로 다음 에디터 틱에 반영
    void OnValidate()
    {
        EditorApplication.delayCall -= DelayedApply;
        EditorApplication.delayCall += DelayedApply;
    }

    void DelayedApply()
    {
        if (this != null) Apply();
    }
#endif

    public void Apply()
    {
        EnsureParts();
        Vector2 size = AlbumSize;
        float albumTop = -heightDistance;
        float albumCenterZ = 0f;

        if (album != null)
        {
            // 앨범 원본(스케일 1) 크기를 측정해 목표 크기가 되도록 X, Y 스케일 계산
            album.localRotation = Quaternion.identity;
            Bounds native = MeasureNativeBounds(album);
            var s = album.localScale;
            s.x = size.x / Mathf.Max(native.size.x, 0.0001f);
            s.y = size.y / Mathf.Max(native.size.y, 0.0001f);
            album.localScale = s;

            Vector3 scaledCenter = Vector3.Scale(native.center, s);
            float depth = native.size.z * s.z;
            albumCenterZ = -wallOffset - depth * 0.5f;

            // 상단 = -heightDistance, 가로 중앙 = 0, 뒷면 = -wallOffset
            var target = new Vector3(0f, albumTop - size.y * 0.5f, albumCenterZ);
            album.localPosition = target - scaledCenter;
        }

        // 레일: 천장(0)에 붙은 가로 바
        float railLength = size.x + railMargin * 2f;
        railBar.localPosition = new Vector3(0f, -railThickness * 0.5f, albumCenterZ);
        railBar.localRotation = Quaternion.identity;
        railBar.localScale = new Vector3(railLength, railThickness, railThickness);

        // 와이어: 레일 하단 ~ 앨범 상단
        float wireLength = Mathf.Max(heightDistance - railThickness, 0.001f);
        float spacing = size.x * wireSpacingRatio;
        bool twoWires = spacing > 0.001f;
        FitWire(wire, twoWires ? -spacing * 0.5f : 0f, wireLength, albumCenterZ);
        wire2.gameObject.SetActive(twoWires);
        if (twoWires) FitWire(wire2, spacing * 0.5f, wireLength, albumCenterZ);

        var mat = metalMaterial != null ? metalMaterial : GetDefaultMaterial();
        foreach (var t in new[] { railBar, wire, wire2 })
        {
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
        }
    }

    void FitWire(Transform w, float x, float length, float z)
    {
        // Cylinder(높이 2)·Cube(높이 1) 어느 메쉬든 실제 길이가 length가 되도록 메쉬 높이로 나눈다
        var mf = w.GetComponent<MeshFilter>();
        float meshHeight = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size.y : 2f;

        w.localPosition = new Vector3(x, -railThickness - length * 0.5f, z);
        w.localRotation = Quaternion.identity;
        w.localScale = new Vector3(wireThickness, length / meshHeight, wireThickness);
    }

    // 앨범 하위 메쉬들의 bounds를 앨범 자신의 로컬 공간(스케일 제거)에서 계산
    static Bounds MeasureNativeBounds(Transform root)
    {
        bool has = false;
        var b = new Bounds();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!has) { b = new Bounds(p, Vector3.zero); has = true; }
                else b.Encapsulate(p);
            }
        }
        return has ? b : new Bounds(Vector3.zero, Vector3.one);
    }

    void EnsureParts()
    {
        if (railBar == null) railBar = transform.Find("RailBar");
        if (railBar == null) railBar = CreatePart(PrimitiveType.Cube, "RailBar");
        if (wire == null) wire = transform.Find("Wire");
        if (wire == null) wire = CreatePart(PrimitiveType.Cylinder, "Wire");
        if (wire2 == null) wire2 = transform.Find("Wire (2)");
        if (wire2 == null) wire2 = CreatePart(PrimitiveType.Cylinder, "Wire (2)");
    }

    Transform CreatePart(PrimitiveType type, string name)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    // #3A3A3A, Metallic 0.9, Smoothness 0.7 (URP Lit, 없으면 Standard)
    static Material GetDefaultMaterial()
    {
        if (defaultMaterial != null) return defaultMaterial;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var color = new Color32(0x3A, 0x3A, 0x3A, 0xFF);
        defaultMaterial = new Material(shader) { name = "GalleryWire_DarkMetal", hideFlags = HideFlags.DontSave };
        defaultMaterial.SetColor("_BaseColor", color);   // URP
        defaultMaterial.SetColor("_Color", color);       // Standard
        defaultMaterial.SetFloat("_Metallic", 0.9f);
        defaultMaterial.SetFloat("_Smoothness", 0.7f);   // URP
        defaultMaterial.SetFloat("_Glossiness", 0.7f);   // Standard
        return defaultMaterial;
    }

    // Scene 뷰: 노란 구 = 천장 연결점, 빨간 선 = 벽면
    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.06f);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, transform.position - transform.up * heightDistance);
    }
}
