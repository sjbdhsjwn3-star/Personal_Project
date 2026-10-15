using UnityEngine;

// 작은 벽 조명(천장 간접조명 등)을 '벽에 맺힌 빛 원' 이미지로 바꿔 그린다.
// 퀘스트는 한 화면 실시간 조명이 32개로 제한돼 2층 간접조명 32개를 다 켤 수 없다.
// 그래서 게임이 시작되면(Awake — FloorLights보다 먼저) 각 조명에서 가장 가까운 벽을 찾아
// 그 자리에 부드러운 가산(additive) 빛 원을 붙이고 실제 조명은 끈다. 에디터에서는 원래 조명 그대로 보인다.
public class LightGlowDecals : MonoBehaviour
{
    [Tooltip("이 아래의 조명들을 빛 원으로 바꾼다 (예: F2_Lighting/Cove_Lights)")]
    public Transform lightsRoot;
    [Tooltip("부드러운 원 텍스처를 쓴 가산 블렌딩 재질")]
    public Material glowMaterial;
    [Tooltip("빛 원 지름 = 조명 범위 × 이 값")]
    [Range(0.2f, 2f)] public float sizeByRange = 0.85f;
    [Tooltip("빛 원 중심을 조명보다 이만큼 아래로 (m)")]
    public float dropBelowLight = 0.15f;
    [Tooltip("벽 표면에서 띄우는 거리 (m) — 깜빡임(z-fighting) 방지")]
    public float wallOffset = 0.01f;

    void Awake()
    {
        if (lightsRoot == null || glowMaterial == null) return;
        foreach (var l in lightsRoot.GetComponentsInChildren<Light>())
        {
            if (!l.enabled || !l.gameObject.activeInHierarchy) continue;
            if (!NearestWall(l, out var hit)) continue;

            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = l.name + "_Glow";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            q.transform.position = new Vector3(hit.point.x, l.transform.position.y - dropBelowLight, hit.point.z) + hit.normal * wallOffset;
            q.transform.rotation = Quaternion.LookRotation(-hit.normal, Vector3.up);   // Quad 앞면이 벽 밖을 보게
            q.transform.localScale = Vector3.one * l.range * sizeByRange;
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = glowMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            l.enabled = false;
        }
    }

    // 조명에서 수평 4방향 중 가장 가까운 벽
    static bool NearestWall(Light l, out RaycastHit best)
    {
        best = default;
        float bestDist = l.range;
        bool found = false;
        foreach (var d in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
        {
            if (!Physics.Raycast(l.transform.position, d, out var h, bestDist, ~0, QueryTriggerInteraction.Ignore)) continue;
            best = h;
            bestDist = h.distance;
            found = true;
        }
        return found;
    }
}
