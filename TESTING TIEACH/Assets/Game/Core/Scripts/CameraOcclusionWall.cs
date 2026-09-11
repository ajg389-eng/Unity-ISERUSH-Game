using UnityEngine;

/// <summary>
/// Marks a wall (or wall segment) that can lower when the camera views the store from that side.
/// </summary>
public class CameraOcclusionWall : MonoBehaviour
{
    [Tooltip("Direction pointing outside the kitchen (away from store center). Auto-filled if left zero.")]
    public Vector3 outwardNormal = Vector3.zero;

    [Tooltip("How far down to lower when ducked (world units).")]
    public float lowerDistance = 4.5f;

    [Tooltip("Optional: also fade renderers while ducked.")]
    public bool fadeRenderers = false;

    Vector3 restLocalPos;
    bool restCaptured;
    float duckAmount; // 0 = up, 1 = fully down
    float targetDuck;
    Renderer[] renderers;
    float[] restAlphas;

    public float DuckAmount => duckAmount;

    void Awake()
    {
        CaptureRestPose();
        CacheRenderers();
    }

    void OnEnable()
    {
        CameraWallCutaway.Register(this);
    }

    void OnDisable()
    {
        CameraWallCutaway.Unregister(this);
        // Snap back up when disabled so edit mode / rebuilds don't leave walls down
        ApplyDuck(0f);
    }

    public void SnapUp()
    {
        targetDuck = 0f;
        duckAmount = 0f;
        restLocalPos = transform.localPosition;
        restCaptured = true;
        ApplyDuck(0f);
    }

    public void CaptureRestPose()
    {
        restLocalPos = transform.localPosition;
        restCaptured = true;
    }

    public void SetOutwardFromKitchen(Vector3 kitchenFocus)
    {
        Vector3 n = transform.position - kitchenFocus;
        n.y = 0f;
        if (n.sqrMagnitude < 0.0001f)
            n = transform.forward;
        n.Normalize();
        outwardNormal = n;
    }

    public void SetOutward(Vector3 worldNormal)
    {
        worldNormal.y = 0f;
        if (worldNormal.sqrMagnitude < 0.0001f) return;
        outwardNormal = worldNormal.normalized;
    }

    public void SetDuckTarget(float target01)
    {
        targetDuck = Mathf.Clamp01(target01);
    }

    void LateUpdate()
    {
        if (!restCaptured) CaptureRestPose();

        float speed = CameraWallCutaway.Instance != null
            ? CameraWallCutaway.Instance.duckSpeed
            : 8f;
        duckAmount = Mathf.MoveTowards(duckAmount, targetDuck, speed * Time.unscaledDeltaTime);
        ApplyDuck(duckAmount);
    }

    void ApplyDuck(float amount)
    {
        Vector3 p = restLocalPos;
        p.y = restLocalPos.y - lowerDistance * amount;
        transform.localPosition = p;

        if (!fadeRenderers || renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            // Soft hide near full duck without fighting opaque brick shaders hard
            r.enabled = amount < 0.92f;
        }
    }

    void CacheRenderers()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
    }

    public Vector3 GetOutward(Vector3 kitchenFocus)
    {
        if (outwardNormal.sqrMagnitude > 0.01f)
            return outwardNormal.normalized;

        Vector3 n = transform.position - kitchenFocus;
        n.y = 0f;
        if (n.sqrMagnitude < 0.0001f)
            n = transform.forward;
        return n.normalized;
    }
}
