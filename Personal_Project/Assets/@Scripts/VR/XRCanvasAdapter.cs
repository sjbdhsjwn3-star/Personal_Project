using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// 화면 오버레이 캔버스는 VR 헤드셋에 보이지 않는다.
// 이 컴포넌트를 캔버스에 붙이면 VR 모드가 켜질 때 눈앞 distance 거리에 width 폭의 판(World Space)으로 바꿔 세운다. (메인 메뉴 등)
[RequireComponent(typeof(Canvas))]
public class XRCanvasAdapter : MonoBehaviour
{
    [Tooltip("판을 세울 거리 (m)")]
    [Range(0.5f, 10f)] public float distance = 2.2f;
    [Tooltip("판의 가로 폭 (m)")]
    [Range(0.3f, 10f)] public float width = 2.6f;
    [Tooltip("눈높이 기준 위아래 오프셋 (m)")]
    public float heightOffset = 0f;

    bool applied;
    static Material onTopMaterial;

    void OnEnable()
    {
        VRRig.Activated += Apply;
        if (VRRig.IsActiveNow) Apply(VRRig.Current);
    }

    void OnDisable() => VRRig.Activated -= Apply;

    void Apply(VRRig rig)
    {
        if (applied) return;
        applied = true;
        var fwd = Vector3.ProjectOnPlane(rig.transform.forward, Vector3.up).normalized;
        ToWorldSpace(GetComponent<Canvas>(), rig.head, width, false);
        transform.SetPositionAndRotation(rig.ExpectedHeadPosition + fwd * distance + Vector3.up * heightOffset, Quaternion.LookRotation(fwd));
    }

    // 캔버스를 World Space로 바꾸고 가로 폭이 width(m)가 되게 크기를 맞춘다.
    // alwaysOnTop이면 벽 등에 가려지지 않게 깊이 테스트를 끈다 (머리에 붙는 안내 UI 등)
    public static void ToWorldSpace(Canvas canvas, Camera cam, float width, bool alwaysOnTop = true)
    {
        var rt = (RectTransform)canvas.transform;
        var scaler = canvas.GetComponent<CanvasScaler>();
        var size = scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
            ? scaler.referenceResolution
            : rt.sizeDelta;

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one * (width / size.x);

        if (!alwaysOnTop) return;
        foreach (var g in canvas.GetComponentsInChildren<Graphic>(true)) g.material = OnTopMaterial;
    }

    // 깊이 테스트를 끈 UI 재질 (항상 맨 앞에 그려짐)
    public static Material OnTopMaterial
    {
        get
        {
            if (onTopMaterial != null) return onTopMaterial;
            onTopMaterial = new Material(Canvas.GetDefaultCanvasMaterial()) { name = "UI_OnTop" };
            onTopMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            return onTopMaterial;
        }
    }
}
