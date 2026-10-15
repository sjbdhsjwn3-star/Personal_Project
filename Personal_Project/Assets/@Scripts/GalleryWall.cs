using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// 한 벽면에 걸린 GalleryDisplay들을 같은 규칙으로 정렬한다.
// 이 오브젝트의 위치 = 벽면 위 천장 높이, 로컬 +Z = 벽 안쪽, 로컬 X = 벽을 따라가는 방향.
// - 세로/가로 앨범별 천장 거리, 벽과의 간격, 앨범 사이 간격을 자식 전체에 통일 적용
// - autoLayout을 켜면 자식 순서(Hierarchy) 그대로 왼쪽부터 같은 간격으로 배치, 묶음 전체는 중앙 정렬
//   끄면 직접 옮긴 위치를 유지하고 높이·벽 간격만 적용
// 값을 바꿀 때만 적용되며, 씬을 열거나 스크립트가 다시 컴파일될 때는 아무것도 바꾸지 않는다.
[ExecuteAlways]
[DisallowMultipleComponent]
public class GalleryWall : MonoBehaviour
{
    [Tooltip("세로 앨범: 천장에서 앨범 상단까지 거리")]
    [Range(0.2f, 9f)] public float verticalHeightDistance = 2.25f;
    [Tooltip("가로 앨범: 천장에서 앨범 상단까지 거리 (기본값은 세로 앨범과 중심 높이가 같도록 맞춤)")]
    [Range(0.2f, 9f)] public float horizontalHeightDistance = 3.15f;
    [Range(0f, 0.5f)] public float wallOffset = 0.04f;
    [Tooltip("앨범과 앨범 사이 간격 (액자 가장자리 기준)")]
    [Range(0f, 5f)] public float gap = 1f;
    [Tooltip("켜면 gap 간격으로 자동 정렬, 끄면 직접 옮긴 위치 유지")]
    public bool autoLayout = false;

#if UNITY_EDITOR
    void OnValidate()
    {
        EditorApplication.delayCall -= DelayedApply;
        EditorApplication.delayCall += DelayedApply;
    }

    void DelayedApply()
    {
        // 편집용 배치 도구 — 플레이 중에는 돌리지 않는다 (정적 배칭으로 메시가 합쳐져 크기를 잘못 재고 그림이 엉뚱한 곳으로 감)
        if (this != null && !EditorApplication.isPlayingOrWillChangePlaymode) Apply();
    }
#endif

    public void Apply()
    {
        var displays = new System.Collections.Generic.List<GalleryDisplay>();
        foreach (Transform child in transform)
        {
            var d = child.GetComponent<GalleryDisplay>();
            if (d != null && child.gameObject.activeSelf) displays.Add(d);
        }
        if (displays.Count == 0) return;

        float total = -gap;
        foreach (var d in displays) total += d.AlbumSize.x + gap;

        float x = -total * 0.5f;
        foreach (var d in displays)
        {
            d.heightDistance = d.orientation == GalleryDisplay.AlbumOrientation.Vertical
                ? verticalHeightDistance
                : horizontalHeightDistance;
            d.wallOffset = wallOffset;

            float w = d.AlbumSize.x;
            if (autoLayout)
            {
                d.transform.localPosition = new Vector3(x + w * 0.5f, 0f, 0f);
                d.transform.localRotation = Quaternion.identity;
            }
            d.Apply();
            x += w + gap;
        }
    }
}
