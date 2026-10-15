using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

// 한 층의 스테이지: 관찰 지점 + 퀴즈 + 잠금 장치
public class FloorStage : MonoBehaviour
{
    [Header("Floor")]
    public int floorNumber = 1;
    [Tooltip("플레이어 발 높이가 이 범위에 있으면 이 층에 있는 것으로 본다")]
    public float minY = 0f;
    public float maxY = 5.84f;

    [Header("Exploration")]
    [Tooltip("비워 두면 자식의 ObservationPoint를 자동으로 사용")]
    public List<ObservationPoint> observationPoints = new List<ObservationPoint>();
    [Range(0.1f, 1f)] public float requiredRatio = 1f;

    [Header("Quiz (자식 또는 같은 오브젝트의 FloorQuiz)")]
    public FloorQuiz quiz;

    [Header("BGM")]
    [Tooltip("이 층에 들어오면 BGMManager가 재생할 곡")]
    public AudioClip bgm;

    [Header("Spawn")]
    [Tooltip("이전 층 퀴즈를 맞히면 플레이어가 순간이동할 위치·방향 (플레이어 몸 중심 기준)")]
    public Transform spawnPoint;
    [Tooltip("계단실 문으로 이 층에 왔을 때 도착 위치 (비우면 spawnPoint) — 1층처럼 시작 위치와 계단 도착 위치가 다를 때")]
    public Transform stairArrival;

    [Header("Lock")]
    [Tooltip("이 층이 잠겨 있는 동안 켜 둘 오브젝트 (계단 문 막이 등)")]
    public List<GameObject> blockersWhileLocked = new List<GameObject>();
    public UnityEvent onUnlocked;
    public UnityEvent onSolved;

    public bool IsUnlocked { get; private set; }
    public bool IsSolved { get; private set; }
    public bool ExplorationDone { get; private set; }
    public FloorQuiz Quiz => quiz;
    public GameLoopManager Manager { get; private set; }

    public int ObservedCount => observationPoints.Count(p => p != null && p.Observed);
    public int RequiredCount => Mathf.CeilToInt(observationPoints.Count * requiredRatio);

    public void Init(GameLoopManager manager, int number)
    {
        Manager = manager;
        floorNumber = number;
        if (observationPoints.Count == 0) observationPoints = GetComponentsInChildren<ObservationPoint>(true).ToList();
        if (!manager.useQuiz) quiz = null;   // 퀴즈 끔: 문만 열면 이동
        else if (quiz == null) quiz = GetComponentInChildren<FloorQuiz>(true);
        foreach (var p in observationPoints) p.Bind(this, manager);
        if (quiz != null) quiz.Bind(this);
    }

    public void SetUnlocked(bool unlocked)
    {
        IsUnlocked = unlocked;
        foreach (var b in blockersWhileLocked) if (b != null) b.SetActive(!unlocked);
        if (unlocked) onUnlocked?.Invoke();
    }

    // ObservationPoint가 관찰되었을 때
    public void OnPointObserved(ObservationPoint point)
    {
        if (!IsUnlocked || ExplorationDone || quiz == null) return;
        Manager.UI.Toast($"관찰 {ObservedCount} / {RequiredCount}", 1.5f);
        if (ObservedCount < RequiredCount) return;
        ExplorationDone = true;
        Manager.NotifyExplorationComplete(this);
    }

    // 퀴즈 없이 문으로 통과했을 때 (ExitZone 등 '이 층 완료' 조건을 만족시킨다)
    public void MarkSolved()
    {
        if (IsSolved) return;
        IsSolved = true;
        onSolved?.Invoke();
    }

    // FloorQuiz가 정답 판정을 내렸을 때
    public void OnQuizSolved()
    {
        if (IsSolved) return;
        MarkSolved();
        Manager.UI.Close();
        Manager.NotifyStageSolved(this);
    }
}
