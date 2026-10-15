using UnityEngine;

// 그림 앞의 관찰 지점: 플레이어가 가까이서(radius) 바라보고(viewAngle) 잠시(dwellTime) 머물면 '관찰함'
public class ObservationPoint : MonoBehaviour
{
    [Tooltip("바라봐야 할 지점 (비우면 이 오브젝트 위치)")]
    public Transform lookTarget;
    [Range(0.5f, 10f)] public float radius = 4f;
    [Range(5f, 60f)] public float viewAngle = 25f;
    [Range(0f, 5f)] public float dwellTime = 1.2f;

    public bool Observed { get; private set; }

    FloorStage stage;
    GameLoopManager manager;
    float timer;

    public void Bind(FloorStage s, GameLoopManager m) { stage = s; manager = m; }

    void Update()
    {
        if (Observed || stage == null || !stage.IsUnlocked || manager.playerCamera == null || manager.UI.IsOpen) return;

        var cam = manager.playerCamera.transform;
        var target = lookTarget != null ? lookTarget.position : transform.position;
        var toTarget = target - cam.position;
        bool looking = toTarget.magnitude <= radius && Vector3.Angle(cam.forward, toTarget) <= viewAngle;

        timer = looking ? timer + Time.deltaTime : 0f;
        if (timer < dwellTime) return;

        Observed = true;
        stage.OnPointObserved(this);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Observed ? Color.green : Color.cyan;
        Gizmos.DrawWireSphere(lookTarget != null ? lookTarget.position : transform.position, radius);
    }
}
