using System.Linq;
using UnityEngine;

// 1층: 그림 속 숨은 물건 N개를 모두 찾아 클릭
public class FindObjectsQuiz : FloorQuiz
{
    QuizClickTarget[] targets;

    public override void Bind(FloorStage stage)
    {
        base.Bind(stage);
        targets = GetComponentsInChildren<QuizClickTarget>(true);
    }

    int FoundCount => targets.Count(t => t.Found);

    public override void BuildUI(QuizUI ui)
    {
        ui.SetBody($"{Question}\n\n찾은 물건  {FoundCount} / {targets.Length}");
        foreach (var t in targets)
            ui.AddLabel(t.Found ? $"✔  {t.displayName}" : "?  ???");
        ui.AddHint("창을 닫고(Tab) 화면 가운데 점으로 그림 속을 클릭하세요.");
    }

    public override void OnTargetFound(QuizClickTarget target)
    {
        UI.Toast($"찾았다! {target.displayName}  ({FoundCount} / {targets.Length})", 2f);
        Refresh();
        if (FoundCount >= targets.Length) Solve();
    }
}
