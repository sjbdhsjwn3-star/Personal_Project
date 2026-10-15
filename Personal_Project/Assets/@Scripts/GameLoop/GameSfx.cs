using UnityEngine;

// 효과음: 문 여는 소리 + 발걸음 소리
//  - 문: InteractDoor(입구)가 열릴 때, 계단실 문으로 층을 옮길 때 GameSfx.PlayDoor 호출
//  - 발걸음: 플레이어가 땅 위를 움직이는 동안 stepInterval 간격으로 한 걸음씩 재생 (키보드·VR 스틱 이동 모두, 순간이동은 제외)
public class GameSfx : MonoBehaviour
{
    public static GameSfx Instance { get; private set; }

    [Header("Door")]
    public AudioClip doorSound;
    [Range(0f, 1f)] public float doorVolume = 0.8f;

    [Header("Footsteps")]
    public AudioClip walkSound;
    [Range(0f, 1f)] public float walkVolume = 0.5f;
    [Tooltip("걷는 동안 한 걸음 소리 간격 (초) — 클수록 천천히 걷는 느낌")]
    [Range(0.25f, 1.5f)] public float stepInterval = 0.6f;
    [Tooltip("이 속도(m/s) 이상으로 움직일 때만 걷는 것으로 본다")]
    public float minSpeed = 0.3f;
    [Tooltip("걸음마다 음높이를 조금씩 바꿔 기계적으로 들리지 않게")]
    [Range(0f, 0.3f)] public float pitchVariation = 0.08f;

    AudioSource feet, ui;
    CharacterController body;
    Vector3 lastPos;
    float walked;

    void Awake()
    {
        Instance = this;
        feet = gameObject.AddComponent<AudioSource>();
        feet.playOnAwake = false;
        feet.spatialBlend = 0f;   // 내 발소리는 2D
        ui = gameObject.AddComponent<AudioSource>();   // 순간이동해도 끊기지 않는 2D 효과음 (계단실 문)
        ui.playOnAwake = false;
        ui.spatialBlend = 0f;
    }

    void Start()
    {
        var m = GameLoopManager.Instance;
        if (m != null && m.player != null)
        {
            body = m.player.GetComponent<CharacterController>();
            lastPos = m.player.position;
        }
    }

    // pos가 있으면 문 위치에서 3D로, 없으면 플레이어 귀에 2D로 재생
    // (계단실 문은 소리가 시작되자마자 다른 층으로 순간이동하므로 2D로 따라가게 한다)
    public static void PlayDoor(Vector3? pos = null)
    {
        var s = Instance;
        if (s == null || s.doorSound == null) return;
        if (pos.HasValue) AudioSource.PlayClipAtPoint(s.doorSound, pos.Value, s.doorVolume);
        else s.ui.PlayOneShot(s.doorSound, s.doorVolume);
    }

    void Update()
    {
        var m = GameLoopManager.Instance;
        if (m == null || m.player == null || walkSound == null) return;

        var p = m.player.position;
        var delta = p - lastPos;
        lastPos = p;
        delta.y = 0f;
        float d = delta.magnitude;

        // 순간이동·층 이동(한 프레임에 크게 움직임)이나 공중에 있을 때는 세지 않는다
        bool grounded = body == null || body.isGrounded || Physics.Raycast(p, Vector3.down, 1.2f, ~0, QueryTriggerInteraction.Ignore);
        if (m.IsTeleporting || d > 1.5f || !grounded) { walked = 0f; return; }

        // 움직이는 동안 일정한 박자로 (속도와 상관없이 느긋한 관람 걸음)
        if (d < minSpeed * Time.deltaTime) { walked = Mathf.Min(walked, stepInterval * 0.5f); return; }   // 멈췄다 다시 걸으면 반 박자 뒤 첫 걸음
        walked += Time.deltaTime;
        if (walked < stepInterval) return;
        walked = 0f;
        feet.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        feet.PlayOneShot(walkSound, walkVolume);
    }
}
