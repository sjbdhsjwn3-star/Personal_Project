using System.Collections.Generic;
using UnityEngine;

// 조명 한도 관리 (원본 조명 값은 건드리지 않는다)
// 안드로이드(퀘스트) 대상에서는 URP가 한 화면에 실시간 조명을 32개까지만 처리해서,
// 넘으면 조명이 깜빡이거나 네모 모양으로 잘린다. 그래서
//  1) 플레이어가 있는 층의 조명만 켠다 — 다른 층 조명은 바닥·천장에 막혀 어차피 안 보인다
//  2) 그래도 한도를 넘는 층(2층)은 큰 조명은 항상 켜고, 범위가 작은 조명(천장 간접조명 등)만
//     플레이어에게 가까운 것부터 남은 자리만큼 켠다 — 멀리 있는 작은 조명은 원래도 거의 안 보인다
public class FloorLights : MonoBehaviour
{
    [Tooltip("한 번에 켤 실시간 조명 수 (모바일 한도 32보다 조금 적게)")]
    [Range(8, 32)] public int budget = 31;
    [Tooltip("이 범위(m) 이하인 조명은 '작은 조명'으로 보고 가까운 것부터 켠다")]
    public float smallLightRange = 4f;
    [Tooltip("작은 조명을 다시 고르는 간격 (초)")]
    public float refreshInterval = 0.3f;
    [Tooltip("층 판정에서 빼고 항상 켜 둘 조명")]
    public List<Light> alwaysOn = new List<Light>();

    readonly Dictionary<FloorStage, List<Light>> byFloor = new Dictionary<FloorStage, List<Light>>();
    readonly List<Light> small = new List<Light>();
    FloorStage active;
    float nextRefresh;
    bool initialized;

    void Start()
    {
        var m = GameLoopManager.Instance;
        if (m == null || m.stages.Count == 0) { enabled = false; return; }

        foreach (var s in m.stages) byFloor[s] = new List<Light>();
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            // 원래 꺼져 있던 조명(컴포넌트·오브젝트 비활성)은 그대로 둔다
            if (l.type == LightType.Directional || alwaysOn.Contains(l) || !l.enabled) continue;
            byFloor[FloorOf(m, l.transform.position.y)].Add(l);
        }
        initialized = true;
    }

    // 높이로 층 찾기 (범위 밖이면 가장 가까운 층 — 천장 위 조명은 위층, 지하는 1층)
    static FloorStage FloorOf(GameLoopManager m, float y)
    {
        FloorStage best = m.stages[0];
        float bestDist = float.MaxValue;
        foreach (var s in m.stages)
        {
            if (y >= s.minY && y < s.maxY) return s;
            float d = Mathf.Min(Mathf.Abs(y - s.minY), Mathf.Abs(y - s.maxY));
            if (d < bestDist) { bestDist = d; best = s; }
        }
        return best;
    }

    void Update()
    {
        var m = GameLoopManager.Instance;
        if (!initialized || m == null || m.player == null) return;
        var now = m.CurrentStage != null ? m.CurrentStage : FloorOf(m, m.player.position.y);
        if (now != active) { SwitchFloor(now); nextRefresh = 0f; }
        if (Time.unscaledTime >= nextRefresh) { RefreshSmall(m.player.position); nextRefresh = Time.unscaledTime + refreshInterval; }
    }

    void SwitchFloor(FloorStage floor)
    {
        active = floor;
        small.Clear();
        int big = 0;
        foreach (var kv in byFloor)
            foreach (var l in kv.Value)
            {
                if (l == null) continue;
                bool here = kv.Key == floor;
                bool isSmall = l.range <= smallLightRange;
                if (here && isSmall) small.Add(l);
                if (here && !isSmall) big++;
                l.enabled = here && !isSmall;
            }
        smallSlots = Mathf.Max(0, budget - big - alwaysOn.Count);
    }

    int smallSlots;

    // 작은 조명: 플레이어에게 가까운 순으로 남은 자리만큼
    void RefreshSmall(Vector3 p)
    {
        if (small.Count == 0) return;
        if (small.Count <= smallSlots) { foreach (var l in small) l.enabled = true; return; }
        small.Sort((a, b) => (a.transform.position - p).sqrMagnitude.CompareTo((b.transform.position - p).sqrMagnitude));
        for (int i = 0; i < small.Count; i++) small[i].enabled = i < smallSlots;
    }
}
