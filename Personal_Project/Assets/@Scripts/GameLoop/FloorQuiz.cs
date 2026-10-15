using UnityEngine;

// 층별 퀴즈의 공통 틀. 새 퀴즈 유형은 이 클래스를 상속해 BuildUI만 구현하면 된다.
public abstract class FloorQuiz : MonoBehaviour
{
    [SerializeField] string title = "퀴즈";
    [TextArea(2, 5)] [SerializeField] string question = "";

    public string Title => title;
    public string Question => question;
    public FloorStage Stage { get; private set; }
    protected QuizUI UI => Stage.Manager.UI;

    public virtual void Bind(FloorStage stage)
    {
        Stage = stage;
        foreach (var t in GetComponentsInChildren<QuizClickTarget>(true)) t.Owner = this;
    }

    // 퀴즈 창이 열릴 때(그리고 Refresh 때) 내용 영역을 채운다
    public abstract void BuildUI(QuizUI ui);

    // 클릭 대상이 발견됐을 때 (1층·3층)
    public virtual void OnTargetFound(QuizClickTarget target) { }

    protected void Solve() => Stage.OnQuizSolved();

    // 열려 있으면 내용 갱신
    protected void Refresh() { if (UI.IsOpen && UI.CurrentQuiz == this) UI.Rebuild(); }
}
