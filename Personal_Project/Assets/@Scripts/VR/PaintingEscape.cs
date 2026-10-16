using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 그림 프레임 탈출 연출
//  1) 대기: 그림 표면에 나비가 붙어 은은히 빛나고, 그림 주변에 빛 가루가 아른거린다 (시선 유도)
//  2) 플레이어가 가까이 와서 그림 쪽을 바라보면 → 캔버스에 물결이 퍼지고 나비가 차례로 그림 밖으로 빠져나온다
//  3) 나비는 빛 가루를 흘리며 2~3초 날다가 빛 가루로 흩어지며 사라진다 (vanishAfterFlight를 끄면 그림으로 돌아감)
//  4) 플레이어가 멀어지면 나비가 그림 위로 되돌아가 있고, 다시 오면 반복
public class PaintingEscape : MonoBehaviour
{
    [Header("Painting")]
    [Tooltip("나비가 빠져나올 그림 (캔버스 Quad)")]
    public Renderer painting;
    [Tooltip("그림 위 나비가 붙어 있을 위치 (그림 UV 0~1)")]
    public List<Vector2> restSpots = new List<Vector2> { new Vector2(0.38f, 0.32f), new Vector2(0.64f, 0.45f) };

    [Header("Butterfly")]
    public ButterflyFlight butterflyPrefab;
    [Range(0.3f, 4f)] public float butterflyScale = 1.8f;
    [Tooltip("두 번째 나비부터 이만큼 늦게 출발")]
    [Range(0f, 3f)] public float escapeStagger = 0.7f;
    [Tooltip("켜면 그림에서 나온 나비가 잠깐 날다가 빛 가루로 흩어지며 사라진다 (끄면 flightDuration 뒤 그림으로 돌아감)")]
    public bool vanishAfterFlight = true;
    [Tooltip("그림에서 빠져나온 뒤 날아다니는 시간 (초) — 이 범위에서 무작위")]
    public Vector2 vanishAfter = new Vector2(2f, 3f);
    [Tooltip("켜면 그림에서 나온 나비가 플레이어 머리 주변을 따라다닌다")]
    public bool followPlayer = true;
    [Tooltip("vanishAfterFlight를 껐을 때: 날아다니는 시간 (초) — 지나면 그림으로 돌아간다. 0이면 계속")]
    public float flightDuration = 35f;

    [Header("Fly Area (그림 앞 공간)")]
    [Tooltip("그림 앞으로 이만큼 떨어진 곳이 비행 구역 중심")]
    public float areaDistance = 3.5f;
    [Tooltip("그림 기준 벽을 따라 옆으로 미는 거리 (그림을 바라볼 때 오른쪽이 +)")]
    public float areaSideOffset = 0f;
    [Tooltip("x = 벽을 따라 폭, y = 높이, z = 그림 앞쪽 깊이")]
    public Vector3 areaSize = new Vector3(7f, 2.6f, 5f);
    [Tooltip("비행 구역 중심 높이 (바닥 기준)")]
    public float areaHeight = 3.0f;

    [Header("Trigger")]
    [Tooltip("그림에서 이 거리(수평) 안으로 들어오면")]
    [Range(1f, 12f)] public float triggerRadius = 6f;
    [Tooltip("그림 쪽을 바라봐야 시작 (0 = 상관없음, 1 = 정면)")]
    [Range(0f, 1f)] public float lookThreshold = 0.35f;
    [Tooltip("돌아온 뒤 다시 반응하려면 플레이어가 이 거리 밖으로 나갔다 와야 한다")]
    public float resetRadius = 9f;
    [Tooltip("탈출할 때 잠깐 띄울 안내 (비우면 없음)")]
    public string escapeToast = "그림 속 나비가 빠져나왔어요…";

    [Header("Ripple")]
    public Material rippleMaterial;
    [Range(0.3f, 4f)] public float rippleDuration = 1.6f;

    [Header("Shimmer")]
    public Material shimmerMaterial;
    public Color shimmerColor = new Color(1f, 0.92f, 0.7f, 1f);

    readonly List<ButterflyFlight> butterflies = new List<ButterflyFlight>();
    Renderer rippleQuad;
    Material rippleInstance;
    ParticleSystem idleSparkle;
    Vector3 normal, floorPoint;
    bool armed = true, out_ = true;
    float flyingSince = -1f;

    void Start()
    {
        if (painting == null || butterflyPrefab == null) { enabled = false; return; }

        // 그림 앞면 방향: Quad는 -forward 쪽에서 보인다
        normal = -painting.transform.forward;
        var b = painting.bounds;
        floorPoint = Physics.Raycast(b.center + normal * 1.5f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point : b.center + Vector3.down * b.extents.y;

        BuildRipple();
        BuildIdleSparkle();
        for (int i = 0; i < restSpots.Count; i++) SpawnButterfly(restSpots[i]);
    }

    void OnDestroy() { if (rippleInstance != null) Destroy(rippleInstance); }

    Vector3 PointOnPainting(Vector2 uv)
    {
        var t = painting.transform;
        return t.TransformPoint(new Vector3(uv.x - 0.5f, uv.y - 0.5f, 0f));   // Quad 로컬 -0.5~0.5
    }

    Bounds FlyArea()
    {
        // 그림을 바라보는 사람 기준 오른쪽
        var right = Vector3.Cross(normal, Vector3.up);
        var c = painting.bounds.center + normal * areaDistance + right * areaSideOffset;
        c.y = floorPoint.y + areaHeight;
        // 그림 벽과 나란히 넓게
        var size = new Vector3(Mathf.Abs(right.x) * areaSize.x + Mathf.Abs(normal.x) * areaSize.z, areaSize.y,
                               Mathf.Abs(right.z) * areaSize.x + Mathf.Abs(normal.z) * areaSize.z);
        return new Bounds(c, size);
    }

    void SpawnButterfly(Vector2 uv)
    {
        var bf = Instantiate(butterflyPrefab, transform);
        bf.transform.localScale = Vector3.one * butterflyScale;
        bf.flyArea = FlyArea();
        bf.PlaceAtRest(PointOnPainting(uv), normal);
        butterflies.Add(bf);
    }

    void Update()
    {
        var m = GameLoopManager.Instance;
        var player = m != null ? m.player : null;
        var cam = VRRig.IsActiveNow ? VRRig.Current.head : (m != null ? m.playerCamera : Camera.main);
        if (player == null || cam == null) return;

        var c = painting.bounds.center;
        float dist = new Vector2(player.position.x - c.x, player.position.z - c.z).magnitude;
        bool sameFloor = Mathf.Abs(player.position.y - floorPoint.y) < 3f;
        bool inFront = Vector3.Dot(player.position - c, normal) > 0f;
        if (dist > resetRadius || !sameFloor) out_ = true;

        // 사라진 나비는 플레이어가 멀어졌을 때 그림 위로 조용히 되돌려 둔다 (다시 오면 연출 반복)
        if (out_ && !armed && butterflies.TrueForAll(b => b.IsGone))
        {
            foreach (var b in butterflies) b.Respawn();
            armed = true;
        }

        foreach (var b in butterflies) { b.followTarget = cam.transform; b.followPlayer = followPlayer; }

        bool allHome = butterflies.TrueForAll(b => b.IsHome);
        if (idleSparkle != null)
        {
            var e = idleSparkle.emission;
            e.enabled = allHome;
        }

        if (armed && out_ && allHome && sameFloor && inFront && dist <= triggerRadius)
        {
            var toPainting = (c - cam.transform.position).normalized;
            if (Vector3.Dot(cam.transform.forward, toPainting) >= lookThreshold) StartCoroutine(Escape(m));
        }

        if (!vanishAfterFlight && flyingSince >= 0f && flightDuration > 0f && Time.time - flyingSince > flightDuration)
        {
            flyingSince = -1f;
            StartCoroutine(ReturnHome());
        }
    }

    IEnumerator Escape(GameLoopManager m)
    {
        armed = false;
        out_ = false;
        if (m != null && !string.IsNullOrEmpty(escapeToast)) m.UI.Toast(escapeToast, 2.5f);

        for (int i = 0; i < butterflies.Count; i++)
        {
            StartCoroutine(Ripple(restSpots[i]));
            yield return new WaitForSeconds(0.35f);   // 물결이 먼저 일고 나비가 따라 나온다
            butterflies[i].EscapeOut();
            if (vanishAfterFlight) StartCoroutine(VanishLater(butterflies[i]));
            yield return new WaitForSeconds(escapeStagger);
        }
        flyingSince = Time.time;
    }

    // 그림 밖으로 빠져나오는 동작(약 1.6초)이 끝난 뒤 2~3초 날다가 사라진다
    IEnumerator VanishLater(ButterflyFlight b)
    {
        yield return new WaitForSeconds(1.6f + Random.Range(vanishAfter.x, vanishAfter.y));
        b.Vanish();
    }

    IEnumerator ReturnHome()
    {
        foreach (var b in butterflies) b.GoHome();
        while (!butterflies.TrueForAll(b => b.IsHome)) yield return null;
        for (int i = 0; i < restSpots.Count; i++) StartCoroutine(Ripple(restSpots[i]));
        armed = true;
    }

    // ---------- 물결 ----------

    void BuildRipple()
    {
        if (rippleMaterial == null) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "PaintingRipple";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(painting.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, -0.003f);   // 캔버스 바로 앞 (Quad 앞면 쪽)
        rippleQuad = go.GetComponent<Renderer>();
        rippleQuad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rippleInstance = new Material(rippleMaterial);
        var pm = painting.sharedMaterial;
        rippleInstance.SetTexture("_BaseMap", pm.mainTexture);
        rippleInstance.SetTextureScale("_BaseMap", pm.mainTextureScale);
        rippleInstance.SetTextureOffset("_BaseMap", pm.mainTextureOffset);
        var s = painting.transform.lossyScale;
        rippleInstance.SetFloat("_Aspect", Mathf.Abs(s.x / Mathf.Max(0.001f, s.y)));
        rippleQuad.sharedMaterial = rippleInstance;
        go.SetActive(false);
    }

    IEnumerator Ripple(Vector2 center)
    {
        if (rippleQuad == null) yield break;
        rippleQuad.gameObject.SetActive(true);
        rippleInstance.SetVector("_Center", center);
        for (float t = 0f; t < 1f; t += Time.deltaTime / rippleDuration)
        {
            rippleInstance.SetFloat("_Progress", t);
            yield return null;
        }
        rippleInstance.SetFloat("_Progress", 1f);
        rippleQuad.gameObject.SetActive(false);
    }

    // ---------- 대기 중 빛 가루 (그림 앞에 아른거림) ----------

    void BuildIdleSparkle()
    {
        if (shimmerMaterial == null) return;
        var go = new GameObject("IdleSparkle");
        go.transform.SetParent(transform, false);
        var b = painting.bounds;
        go.transform.SetPositionAndRotation(b.center + normal * 0.15f, Quaternion.LookRotation(normal));
        idleSparkle = go.AddComponent<ParticleSystem>();
        idleSparkle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = idleSparkle.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
        main.startColor = shimmerColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.01f;
        main.maxParticles = 60;

        var em = idleSparkle.emission;
        em.rateOverTime = 7f;

        var shape = idleSparkle.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        var right = Vector3.Cross(Vector3.up, normal);
        shape.scale = new Vector3(Mathf.Abs(Vector3.Dot(b.size, right)) * 0.85f, b.size.y * 0.85f, 0.1f);

        var col = idleSparkle.colorOverLifetime;
        col.enabled = true;
        col.color = FadeInOut(shimmerColor);

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = shimmerMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        idleSparkle.Play();
    }

    public static Gradient FadeInOut(Color c)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    void OnDrawGizmosSelected()
    {
        if (painting == null) return;
        normal = -painting.transform.forward;
        Gizmos.color = new Color(1f, 0.9f, 0.5f, 0.6f);
        var c = painting.bounds.center;
        Gizmos.DrawWireSphere(new Vector3(c.x, c.y - painting.bounds.extents.y, c.z), triggerRadius);
        if (floorPoint == Vector3.zero) floorPoint = c + Vector3.down * painting.bounds.extents.y * 2f;
        var a = FlyArea();
        Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.6f);
        Gizmos.DrawWireCube(a.center, a.size);
    }
}
