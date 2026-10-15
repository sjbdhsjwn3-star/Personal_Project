using UnityEngine;

// 탈출 구역: requiredStage(마지막 층)의 퀴즈를 푼 뒤 플레이어가 이 상자 안으로 들어오면 게임 클리어
[RequireComponent(typeof(BoxCollider))]
public class ExitZone : MonoBehaviour
{
    public FloorStage requiredStage;

    BoxCollider box;
    bool done;

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true;
    }

    void Update()
    {
        var m = GameLoopManager.Instance;
        if (done || m == null || m.player == null) return;
        if (requiredStage != null && !requiredStage.IsSolved) return;
        if (!box.bounds.Contains(m.player.position)) return;

        done = true;
        m.CompleteGame();
    }
}
