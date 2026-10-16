using UnityEngine;

// 나비 한 마리: 그림 표면에 붙어 은은히 빛나다가(Rest) → 그림 밖으로 빠져나와(Escape) → 전시실 공중을 부드럽게 날아다니고(Fly)
// → 시간이 되면 그림으로 돌아가 다시 붙는다(Return). PaintingEscape가 생성·제어한다.
// 프리팹 구조: 루트(이 컴포넌트, +Z가 머리·+Y가 등) / Model(Animation) / Trail(ParticleSystem)
public class ButterflyFlight : MonoBehaviour
{
    public enum State { Rest, Escape, Fly, Return, Vanish, Gone }

    [Header("Flight")]
    [Range(0.3f, 4f)] public float speed = 1.4f;
    [Tooltip("초당 방향을 틀 수 있는 각도")]
    [Range(30f, 360f)] public float turnRate = 140f;
    [Tooltip("날갯짓에 맞춘 위아래 출렁임")]
    [Range(0f, 0.5f)] public float bobAmount = 0.12f;
    [Range(0.5f, 6f)] public float bobSpeed = 3.2f;

    [Header("Wing")]
    [Tooltip("그림에 붙어 있을 때 쓸 날갯짓 클립 시간 (날개가 가장 활짝 펴진 순간)")]
    public float restPoseTime = 0.72f;
    [Tooltip("그림에 붙어 있을 때 날개를 아주 살짝 여닫는 폭 (클립 시간, 초)")]
    [Range(0f, 0.3f)] public float restSway = 0.06f;
    [Range(0.05f, 3f)] public float restFlapSpeed = 0.4f;
    [Range(0.5f, 4f)] public float flyFlapSpeed = 1.6f;

    public State Current { get; private set; } = State.Rest;
    public bool IsHome => Current == State.Rest;

    // PaintingEscape가 지정
    [HideInInspector] public Vector3 restPosition;
    [HideInInspector] public Vector3 surfaceNormal;
    [HideInInspector] public Bounds flyArea;
    [HideInInspector] public Transform followTarget;   // 가끔 이 주변(플레이어 머리)을 맴돈다
    [HideInInspector] public bool followPlayer;        // 켜면 날아다니는 내내 플레이어 머리 주변을 따라다닌다

    [Header("Follow Player")]
    [Tooltip("플레이어 머리 주변을 도는 반경 (m)")]
    [Range(0.3f, 3f)] public float followRadius = 0.9f;
    [Tooltip("머리 기준 높이 (m) — 음수면 눈 아래")]
    public float followHeight = -0.2f;
    [Tooltip("주변을 도는 빠르기 (도/초)")]
    [Range(10f, 180f)] public float orbitSpeed = 70f;
    [Tooltip("플레이어에게 다가갈 때 최고 속도 (m/s)")]
    [Range(1f, 8f)] public float followMaxSpeed = 4f;
    float orbitAngle;

    Animation anim;
    ParticleSystem trail;
    Vector3 pos, vel, target, escapeFrom, escapeTo;
    float t, phase, stateTime, retargetAt;

    void Awake()
    {
        anim = GetComponentInChildren<Animation>();
        trail = GetComponentInChildren<ParticleSystem>();
        phase = Random.value * 10f;
    }

    public void PlaceAtRest(Vector3 position, Vector3 normal)
    {
        restPosition = position;
        surfaceNormal = normal;
        pos = position;
        transform.SetPositionAndRotation(position, RestRotation());
        SetState(State.Rest);
    }

    // 그림에 붙어 있는 자세: 등이 그림 밖(normal)을, 머리가 위를 향한다
    Quaternion RestRotation() => Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.up, surfaceNormal).normalized, surfaceNormal);

    public void EscapeOut()
    {
        if (Current != State.Rest) return;
        escapeFrom = restPosition;
        escapeTo = restPosition + surfaceNormal * 1.2f + Vector3.down * 0.4f + Random.insideUnitSphere * 0.3f;
        SetState(State.Escape);
    }

    public void GoHome()
    {
        if (Current == State.Fly) SetState(State.Return);
    }

    public bool IsGone => Current == State.Gone;

    // 날던 자리에서 빛 가루로 흩어지며 사라진다
    public void Vanish()
    {
        if (Current != State.Fly && Current != State.Escape) return;
        vanishScale = transform.localScale;
        if (Current == State.Escape) { pos = transform.position; target = escapeTo + (escapeTo - escapeFrom).normalized; vel = (escapeTo - escapeFrom).normalized * speed; }
        if (trail != null) trail.Emit(25);   // 마지막으로 반짝 흩어지는 빛 가루
        SetState(State.Vanish);
    }

    // 사라진 나비를 그림 위 원래 자리로 되돌린다 (플레이어가 멀리 있을 때)
    public void Respawn()
    {
        if (Current != State.Gone) return;
        transform.localScale = vanishScale;
        SetModelVisible(true);
        PlaceAtRest(restPosition, surfaceNormal);
    }

    Vector3 vanishScale;

    void SetModelVisible(bool on)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (!(r is ParticleSystemRenderer)) r.enabled = on;
    }

    void SetState(State s)
    {
        Current = s;
        stateTime = 0f;
        bool flying = s == State.Escape || s == State.Fly || s == State.Return;
        if (anim != null) foreach (AnimationState a in anim) a.speed = flying ? flyFlapSpeed : 0f;   // 쉴 때는 Update에서 직접 시간 지정
        if (trail != null)
        {
            var e = trail.emission;
            e.enabled = flying;
        }
        if (s == State.Fly) { vel = (escapeTo - escapeFrom).normalized * speed; PickTarget(); }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        t += dt;
        stateTime += dt;

        switch (Current)
        {
            case State.Rest:
                // 그림 위에 날개를 활짝 펴고 붙어서, 아주 살짝 숨 쉬듯 여닫는다
                transform.position = restPosition + surfaceNormal * 0.06f;   // 날개가 캔버스에 파묻히지 않게
                if (anim != null)
                    foreach (AnimationState a in anim)
                        a.time = restPoseTime + Mathf.Sin((t + phase) * restFlapSpeed * Mathf.PI * 2f) * restSway;
                break;

            case State.Escape:
            {
                // 그림 표면에서 스르륵 떨어져 나온다 (처음엔 표면을 따라 미끄러지듯 천천히)
                float k = Mathf.Clamp01(stateTime / 1.6f);
                float e = k * k * (3f - 2f * k);
                pos = Vector3.Lerp(escapeFrom, escapeTo, e);
                var fwd = Vector3.Slerp(Vector3.ProjectOnPlane(Vector3.up, surfaceNormal), (escapeTo - escapeFrom).normalized, e);
                var up = Vector3.Slerp(surfaceNormal, Vector3.up, e);
                transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, up));
                if (k >= 1f) SetState(State.Fly);
                break;
            }

            case State.Fly:
                if (followPlayer && followTarget != null) { Steer(FollowPoint(dt), dt); break; }
                if (Time.time >= retargetAt || (target - pos).sqrMagnitude < 0.36f) PickTarget();
                Steer(target, dt);
                break;

            case State.Vanish:
            {
                // 계속 날면서 0.7초 동안 작아져 사라진다
                Steer(followPlayer && followTarget != null ? FollowPoint(dt) : target, dt);
                float k = Mathf.Clamp01(stateTime / 0.7f);
                transform.localScale = vanishScale * (1f - k * k);
                if (k < 1f) break;
                SetModelVisible(false);
                Current = State.Gone;
                break;
            }

            case State.Return:
            {
                if (landing) { Land(); break; }
                var approach = restPosition + surfaceNormal * 0.8f;
                if ((pos - approach).sqrMagnitude > 0.09f && stateTime < 12f) { Steer(approach, dt); break; }
                // 그림 앞에 오면 표면으로 스르륵 내려앉는다
                landing = true;
                landFrom = pos;
                landRot = transform.rotation;
                stateTime = 0f;
                break;
            }
        }
    }

    bool landing;
    Vector3 landFrom;
    Quaternion landRot;

    void Land()
    {
        float k = Mathf.Clamp01(stateTime / 1.4f);
        float e = k * k * (3f - 2f * k);
        pos = Vector3.Lerp(landFrom, restPosition, e);
        transform.SetPositionAndRotation(pos, Quaternion.Slerp(landRot, RestRotation(), e));
        if (k < 1f) return;
        landing = false;
        SetState(State.Rest);
    }

    void Steer(Vector3 goal, float dt)
    {
        // 플레이어를 따라갈 때는 멀수록 빠르게 (짧은 비행 시간 안에 관람객 곁까지 오도록)
        float s = followPlayer && followTarget != null ? Mathf.Clamp((goal - pos).magnitude * 1.2f, speed, followMaxSpeed) : speed;
        var desired = (goal - pos).normalized * s;
        float turn = followPlayer && followTarget != null ? turnRate * 2f : turnRate;
        vel = Vector3.RotateTowards(vel, desired, turn * Mathf.Deg2Rad * dt, s * 2f * dt);
        if (vel.sqrMagnitude < 0.01f) vel = desired;
        pos += vel * dt;

        // 날갯짓 출렁임 + 진행 방향으로 살짝 기울기
        var shown = pos + Vector3.up * Mathf.Sin((t + phase) * bobSpeed) * bobAmount;
        var flat = Vector3.ProjectOnPlane(vel, Vector3.up);
        var fwd = flat.sqrMagnitude > 1e-4f ? Vector3.Slerp(flat.normalized, vel.normalized, 0.4f) : transform.forward;
        float bank = Vector3.SignedAngle(transform.forward, fwd, Vector3.up) / Mathf.Max(dt, 1e-4f) * -0.05f;
        var rot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Clamp(bank, -30f, 30f));
        transform.SetPositionAndRotation(shown, Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-6f * dt)));
    }

    // 플레이어 머리 주변 원을 따라 도는 목표점 (나비마다 phase로 출발 각도가 다름)
    Vector3 FollowPoint(float dt)
    {
        orbitAngle += orbitSpeed * dt;
        float a = (orbitAngle + phase * 36f) * Mathf.Deg2Rad;
        var h = followTarget.position;
        return new Vector3(h.x + Mathf.Cos(a) * followRadius, h.y + followHeight + Mathf.Sin(a * 1.7f) * 0.15f, h.z + Mathf.Sin(a) * followRadius);
    }

    void PickTarget()
    {
        retargetAt = Time.time + Random.Range(3f, 6f);
        // 가끔은 관람객 주변을 한 바퀴 — 시선을 끌어 준다
        if (followTarget != null && Random.value < 0.35f)
        {
            var off = Random.insideUnitSphere * 1.4f;
            off.y = Mathf.Abs(off.y) * 0.5f + 0.2f;
            target = followTarget.position + off;
            target = flyArea.ClosestPoint(target);
            return;
        }
        target = new Vector3(
            Random.Range(flyArea.min.x, flyArea.max.x),
            Random.Range(flyArea.min.y, flyArea.max.y),
            Random.Range(flyArea.min.z, flyArea.max.z));
    }
}
