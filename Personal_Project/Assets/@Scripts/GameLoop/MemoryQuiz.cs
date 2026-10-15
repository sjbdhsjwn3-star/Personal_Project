using System;
using System.Collections.Generic;
using UnityEngine;

// 2층: 전시된 그림의 변형 요소(색·방향 등)를 맞히는 객관식 퀴즈
// 퀴즈 창이 처음 열릴 때 variants(그림 매터리얼 교체)를 적용해 '달라진 그림'을 만든다.
public class MemoryQuiz : FloorQuiz
{
    [Serializable]
    public class QuizItem
    {
        [TextArea(1, 3)] public string text;
        public string[] choices;
        public int answerIndex;
    }

    [Serializable]
    public class Variant
    {
        [Tooltip("바꿀 그림의 Renderer (앨범의 Quad)")]
        public Renderer target;
        [Tooltip("변형된 그림 매터리얼 (색·방향 등을 바꾼 버전)")]
        public Material changedMaterial;
    }

    public List<QuizItem> questions = new List<QuizItem>();
    [Tooltip("통과에 필요한 정답 수")]
    public int passCount = 4;
    public List<Variant> variants = new List<Variant>();

    int index;          // 현재 문제
    int correct;
    bool variantsApplied;

    public override void BuildUI(QuizUI ui)
    {
        if (!variantsApplied) ApplyVariants();

        if (index >= questions.Count)
        {
            bool pass = correct >= passCount;
            ui.SetBody($"정답 {correct} / {questions.Count}\n\n" + (pass ? "통과!" : $"{passCount}개 이상 맞혀야 해요. 그림을 다시 보고 오세요."));
            if (pass) ui.AddButton("다음 층으로", Solve);
            else ui.AddButton("처음부터 다시", () => { index = 0; correct = 0; Refresh(); });
            return;
        }

        var q = questions[index];
        ui.SetBody($"{Question}\n\n[{index + 1} / {questions.Count}]  {q.text}");
        for (int i = 0; i < q.choices.Length; i++)
        {
            int choice = i;
            ui.AddButton(q.choices[i], () =>
            {
                if (choice == q.answerIndex) correct++;
                index++;
                Refresh();
            });
        }
    }

    void ApplyVariants()
    {
        variantsApplied = true;
        foreach (var v in variants)
            if (v.target != null && v.changedMaterial != null) v.target.sharedMaterial = v.changedMaterial;
    }
}
