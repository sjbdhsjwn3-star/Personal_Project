using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 3층 GallerySpots 아래 조명(Spot_<앨범 이름>)을 각 앨범의 현재 위치에 맞춰 재배치·재조준한다.
// - 위치: 그림 중앙에서 벽 반대쪽으로 Distance 만큼 떨어진 천장 지점 (모든 벽 동일)
// - 회전: 조명기구 전체를 돌려 내부 Spot Light가 그림 중앙을 정확히 향하게 함
// 3층(Structure_02/Interior/3rd  Floor) 밖의 오브젝트는 찾지도, 바꾸지도 않는다.
public static class GallerySpotAligner
{
    const string FloorPath = "Structure_02/Interior/3rd  Floor";
    // 3층 천장 높이 = 3층 벽 상단 (층고를 바꿔도 자동으로 따라감)
    static float CeilingY
    {
        get
        {
            var wall = GameObject.Find(FloorPath + "/Apartment_01/Floor3_Wall");
            return wall != null ? wall.GetComponent<Renderer>().bounds.max.y : 30f;
        }
    }
    const float Distance = 3.5f;   // 그림 면에서 조명까지 수평 거리

    [MenuItem("Tools/Gallery/Re-aim Floor 3 Spotlights")]
    public static void Align()
    {
        var floor = GameObject.Find(FloorPath)?.transform;
        var spots = floor != null ? floor.Find("GallerySpots") : null;
        if (spots == null)
        {
            Debug.LogWarning("[Spots] 3층 GallerySpots를 찾을 수 없습니다.");
            return;
        }

        int aligned = 0;
        foreach (var light in spots.GetComponentsInChildren<Light>(true))
        {
            Transform fixture = light.transform.parent;
            string albumName = fixture.name.Replace("Spot_", "");
            Transform album = FindInFloor(floor, albumName);
            if (album == null)
            {
                Debug.LogWarning($"[Spots] {fixture.name}: 3층에서 '{albumName}'을 찾지 못해 건너뜀");
                continue;
            }

            var quad = album.Find("Quad");
            var picture = quad != null ? quad.GetComponent<Renderer>() : album.GetComponentInChildren<Renderer>();
            Vector3 target = picture.bounds.center;   // 그림 중앙

            Vector3 into = album.forward;              // 앨범 뒷면 → 벽 방향
            into.y = 0f;
            into.Normalize();

            Undo.RecordObject(fixture, "Re-aim Floor 3 Spotlights");
            Vector3 mount = target - into * Distance;
            mount.y = CeilingY;
            fixture.position = mount;

            // 조명기구 로컬 기준 빔 방향을 목표 방향으로 돌린다.
            // 렌즈 위치가 회전에 따라 조금 움직이므로 몇 번 반복해서 오차 제거
            Vector3 beamLocal = fixture.InverseTransformDirection(light.transform.forward);
            for (int i = 0; i < 3; i++)
            {
                Vector3 from = i == 0 ? mount : light.transform.position;
                fixture.rotation = Quaternion.FromToRotation(beamLocal, (target - from).normalized);
            }
            aligned++;
        }

        EditorSceneManager.MarkSceneDirty(floor.gameObject.scene);
        Debug.Log($"[Spots] 3층 조명 {aligned}개 재정렬 완료");
    }

    static Transform FindInFloor(Transform floor, string name)
    {
        foreach (var t in floor.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
