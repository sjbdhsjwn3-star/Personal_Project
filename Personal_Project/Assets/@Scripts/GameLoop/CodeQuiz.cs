using System.Linq;
using UnityEngine;

// 3층: 그림 속 숨은 숫자를 모두 찾아 순서대로 조합한 비밀번호 입력
public class CodeQuiz : FloorQuiz
{
    [TextArea(1, 3)] public string orderHint = "먼저 오르고, 가장 많이 읽히고, 가장 멀리 떠나고, 마지막에 핀다.";
    [Tooltip("비워 두면 숫자 단서의 digitOrder 순서로 자동 생성")]
    public string answerOverride = "";

    QuizClickTarget[] clues;
    string typed = "";
    int wrong;

    public override void Bind(FloorStage stage)
    {
        base.Bind(stage);
        clues = GetComponentsInChildren<QuizClickTarget>(true).Where(c => c.digit >= 0).ToArray();
    }

    public string Answer => !string.IsNullOrEmpty(answerOverride)
        ? answerOverride
        : string.Concat(clues.OrderBy(c => c.digitOrder).Select(c => c.digit.ToString()));

    public override void BuildUI(QuizUI ui)
    {
        var found = clues.Where(c => c.Found).Select(c => $"{c.displayName} = {c.digit}");
        ui.SetBody($"{Question}\n\n<i>{orderHint}</i>\n\n찾은 숫자: " + (found.Any() ? string.Join(",  ", found) : "없음")
                   + $"  ({clues.Count(c => c.Found)} / {clues.Length})");

        var input = ui.AddInput("비밀번호 " + Answer.Length + "자리", typed, v => typed = v);
        ui.AddButton("입력", () =>
        {
            if (typed.Trim() == Answer) { Solve(); return; }
            wrong++;
            UI.Toast(wrong >= 3 ? "힌트: 시의 구절을 그림 하나씩에 짝지어 보세요. '오르는 것'은 계단이에요." : "틀렸어요. 숫자 순서를 다시 생각해 보세요.", 3f);
            typed = "";
            Refresh();
        });
        ui.AddHint("창을 닫고(Tab) 그림 속 숫자를 클릭하면 여기에 기록돼요.");
    }

    public override void OnTargetFound(QuizClickTarget target)
    {
        UI.Toast($"숫자 발견!  {target.displayName} = {target.digit}", 2f);
        Refresh();
    }
}
