using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// 하위의 모든 GalleryWall(→ GalleryDisplay)에 같은 높이·벽 간격·앨범 간격을 일괄 적용한다.
// GalleryWires 같은 최상위 묶음 오브젝트에 붙인다.
[ExecuteAlways]
[DisallowMultipleComponent]
public class GallerySettings : MonoBehaviour
{
    [Range(0.2f, 9f)] public float verticalHeightDistance = 2.25f;
    [Range(0.2f, 9f)] public float horizontalHeightDistance = 3.15f;
    [Tooltip("벽면에서 앨범 뒷면까지 거리 (레일·와이어도 같이 이동)")]
    [Range(0f, 0.5f)] public float wallOffset = 0.12f;
    [Range(0f, 5f)] public float gap = 0.6f;

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
        foreach (var wall in GetComponentsInChildren<GalleryWall>(true))
        {
            wall.verticalHeightDistance = verticalHeightDistance;
            wall.horizontalHeightDistance = horizontalHeightDistance;
            wall.wallOffset = wallOffset;
            wall.gap = gap;
            wall.Apply();
        }
    }
}
