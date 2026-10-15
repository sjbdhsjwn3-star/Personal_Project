using System.Collections;
using UnityEngine;

// 문짝을 경첩(피벗) 기준으로 플레이어 반대쪽(안쪽)으로 회전시킨다. FloorDoor·InteractDoor 공용.
public static class DoorSwing
{
    // 열린 회전값: center(문짝 중심)가 플레이어에게서 멀어지는 방향으로 angle만큼
    public static Quaternion AwayFrom(Transform leaf, Vector3 center, Vector3 player, float angle)
    {
        var hinge = leaf.position;
        var toCenter = center - hinge; toCenter.y = 0f;
        var toPlayer = player - hinge; toPlayer.y = 0f;
        float sign = Vector3.Dot(Quaternion.AngleAxis(angle, Vector3.up) * toCenter, toPlayer) < 0f ? 1f : -1f;
        return Quaternion.AngleAxis(angle * sign, Vector3.up) * leaf.rotation;
    }

    public static IEnumerator Rotate(Transform leaf, Quaternion to, float duration)
    {
        var from = leaf.rotation;
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, duration))
        {
            leaf.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        leaf.rotation = to;
    }

    // 문짝이 움직이는 동안 플레이어(CharacterController)를 밀지 않게 콜라이더를 끄고 켠다
    public static void SetColliders(Transform leaf, bool on)
    {
        foreach (var c in leaf.GetComponentsInChildren<Collider>()) c.enabled = on;
    }
}
