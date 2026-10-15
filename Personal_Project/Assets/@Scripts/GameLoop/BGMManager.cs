using UnityEngine;

// 층별 배경음악: 플레이어가 있는 층(GameLoopManager.CurrentStage)이 바뀌면
// 이전 곡은 즉시 끊고(Stop) 그 층의 FloorStage.bgm을 바로 반복 재생한다 — 페이드 없음, 항상 한 곡만.
//  - startAfterDoor를 지정하면 그 문이 열리기 전까지는 아무 곡도 틀지 않는다 (입구 문을 열면 1층 곡 시작)
public class BGMManager : MonoBehaviour
{
    public static BGMManager Instance { get; private set; }

    [Range(0f, 1f)] public float volume = 0.5f;
    [Tooltip("이 문이 열리면 그때부터 BGM 시작 (비우면 바로 시작)")]
    public InteractDoor startAfterDoor;

    AudioSource source;
    FloorStage playingStage;
    bool stopped;

    void Awake()
    {
        Instance = this;
        source = gameObject.AddComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;   // 2D — 위치와 상관없이 같은 크기
    }

    void Update()
    {
        if (stopped || (startAfterDoor != null && !startAfterDoor.IsOpen)) return;
        var m = GameLoopManager.Instance;
        if (m == null || m.CurrentStage == null || m.CurrentStage == playingStage) return;
        playingStage = m.CurrentStage;
        Play(playingStage.bgm);
    }

    public void Play(AudioClip clip)
    {
        source.Stop();
        source.clip = clip;
        source.volume = volume;
        if (clip != null) source.Play();
    }

    // 엔딩 등: 즉시 멈추고 더 이상 틀지 않는다
    public void StopAll()
    {
        stopped = true;
        source.Stop();
    }
}
