using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 메인 메뉴 레이아웃/버튼 동작 관리.
/// 인스펙터 수치를 바꾸면 에디터에서 즉시 반영된다 (Apply Layout 체크 시).
/// 버튼 클릭은 각 Button의 OnClick에 StartGame / QuitGame을 연결해서 사용.
/// </summary>
[ExecuteAlways]
public class MainMenuManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] RectTransform ticketPanel;
    [SerializeField] RectTransform titleLogo;
    [SerializeField] RectTransform startButton;
    [SerializeField] RectTransform quitButton;
    [Tooltip("게임 목표 문구를 표시할 Text")]
    [SerializeField] Text goalText;

    [Header("Goal")]
    [Tooltip("시작 화면에 보여 줄 게임 목표 (여러 줄은 Enter)")]
    [SerializeField, TextArea(1, 3)] string goalMessage = "게임목표는 3층까지 관람하는 것";
    [SerializeField] Color goalColor = new Color(0.12f, 0.12f, 0.12f, 1f);
    [SerializeField, Range(16, 120)] int goalFontSize = 44;

    [Header("Layout")]
    [Tooltip("끄면 스크립트가 위치/크기를 덮어쓰지 않으므로 씬에서 직접 드래그해서 배치할 수 있다.")]
    [SerializeField] bool applyLayout = true;

    [Tooltip("티켓 패널 크기 (원본 914x520 비율)")]
    [SerializeField] Vector2 ticketSize = new Vector2(1276, 726);
    [Tooltip("화면 중앙 기준 티켓 위치")]
    [SerializeField] Vector2 ticketPosition = Vector2.zero;

    [Tooltip("타이틀 로고 크기 (원본 766x262 비율)")]
    [SerializeField] Vector2 titleSize = new Vector2(1060, 362);
    [Tooltip("티켓 중앙 기준 로고 오프셋")]
    [SerializeField] Vector2 titleOffset = new Vector2(0, 85);

    [Tooltip("버튼 크기 (원본 207x83 비율)")]
    [SerializeField] Vector2 buttonSize = new Vector2(280, 112);
    [Tooltip("티켓 중앙 기준 Start 버튼 위치")]
    [SerializeField] Vector2 startButtonOffset = new Vector2(-175, -215);
    [Tooltip("티켓 중앙 기준 Quit 버튼 위치")]
    [SerializeField] Vector2 quitButtonOffset = new Vector2(175, -215);
    [Tooltip("목표 문구 영역 크기")]
    [SerializeField] Vector2 goalSize = new Vector2(900, 64);
    [Tooltip("티켓 중앙 기준 목표 문구 위치 (로고와 버튼 사이)")]
    [SerializeField] Vector2 goalOffset = new Vector2(0, -125);

    [Header("Scene")]
    [Tooltip("Start 클릭 시 로드할 씬 이름 (Build Profiles에 등록되어 있어야 함)")]
    [SerializeField] string gameSceneName = "Scene_01_Main";

    void OnValidate() { ApplyLayout(); ApplyGoal(); }
    void OnEnable() { ApplyLayout(); ApplyGoal(); }

    [ContextMenu("Apply Layout")]
    public void ApplyLayout()
    {
        if (!applyLayout) return;
        Place(ticketPanel, ticketSize, ticketPosition);
        Place(titleLogo, titleSize, titleOffset);
        Place(startButton, buttonSize, startButtonOffset);
        Place(quitButton, buttonSize, quitButtonOffset);
        if (goalText != null) Place(goalText.rectTransform, goalSize, goalOffset);
    }

    // 문구·색·크기는 applyLayout과 상관없이 항상 반영
    void ApplyGoal()
    {
        if (goalText == null) return;
        goalText.text = goalMessage;
        goalText.color = goalColor;
        goalText.fontSize = goalFontSize;
    }

    static void Place(RectTransform rt, Vector2 size, Vector2 pos)
    {
        if (rt == null) return;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    public void StartGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
