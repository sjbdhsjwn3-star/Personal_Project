using UnityEngine;

// 레이저(VR 트리거) 또는 조준점 클릭으로 크게 볼 수 있는 그림. PaintingViewer가 실행 시 자동으로 붙인다.
public class PaintingTarget : MonoBehaviour, IPointerTarget
{
    public Renderer picture;

    public void OnPointerClick(Vector3 playerPosition)
    {
        if (PaintingViewer.Instance != null) PaintingViewer.Instance.Show(this);
    }

    // 그림이 실제로 보이는 크기 (m) — 텍스처가 정사각형이어도 액자 비율대로 보이게
    public Vector2 WorldSize
    {
        get
        {
            var mf = picture.GetComponent<MeshFilter>();
            var s = picture.transform.lossyScale;
            var mb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size : Vector3.one;
            return new Vector2(Mathf.Abs(mb.x * s.x), Mathf.Abs(mb.y * s.y));
        }
    }

    public Texture Texture => picture.sharedMaterial != null ? picture.sharedMaterial.mainTexture : null;

    public Rect UVRect
    {
        get
        {
            var m = picture.sharedMaterial;
            return m != null ? new Rect(m.mainTextureOffset, m.mainTextureScale) : new Rect(0, 0, 1, 1);
        }
    }
}
