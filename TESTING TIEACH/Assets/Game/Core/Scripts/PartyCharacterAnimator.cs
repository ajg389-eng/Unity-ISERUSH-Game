using UnityEngine;

/// <summary>
/// Faces the character toward movement and drives FREE Party Characters (char_AC)
/// idle/run triggers when an Animator is present.
/// Station / queue facing uses grid-aligned (N/S/E/W) headings from the character's tile.
/// </summary>
[DisallowMultipleComponent]
public class PartyCharacterAnimator : MonoBehaviour
{
    const string DefaultControllerResourceName = "char_AC";

    [Tooltip("Animator on the party character. Auto-found in children if empty.")]
    public Animator animator;

    [Tooltip("Optional: assign Assets/Imported/FREE/Pack_FREE_PartyCharacters/Animations/char_AC")]
    public RuntimeAnimatorController controller;

    [Tooltip("Transform that faces move direction (usually the visible character child).")]
    public Transform visualRoot;

    [Tooltip("World-speed above this counts as moving.")]
    public float moveThreshold = 0.05f;

    [Tooltip("How fast the model turns to face movement / targets.")]
    public float turnSpeed = 10f;

    static readonly int IdleTrigger = Animator.StringToHash("idle");
    static readonly int RunTrigger = Animator.StringToHash("run");

    Vector3 lastPos;
    bool isMoving;
    bool hasIdle;
    bool hasRun;
    int lockFacingFrames;
    Vector3 lockedFaceDir = Vector3.forward;

    /// <summary>Add this component for facing (and optional run/idle animation).</summary>
    public static PartyCharacterAnimator EnsureOn(GameObject go)
    {
        if (go == null) return null;
        var existing = go.GetComponent<PartyCharacterAnimator>();
        if (existing != null) return existing;
        return go.AddComponent<PartyCharacterAnimator>();
    }

    void Awake()
    {
        ResolveRefs();
        lastPos = transform.position;
        if (animator != null && hasIdle)
            animator.SetTrigger(IdleTrigger);
    }

    void ResolveRefs()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (visualRoot == null)
        {
            if (animator != null)
                visualRoot = animator.transform;
            else
                visualRoot = transform;
        }

        if (animator != null)
        {
            EnsureController();
            animator.applyRootMotion = false;
            CacheParams();
        }
    }

    void EnsureController()
    {
        if (animator == null) return;
        if (animator.runtimeAnimatorController != null) return;

        if (controller != null)
        {
            animator.runtimeAnimatorController = controller;
            return;
        }

        var loaded = Resources.Load<RuntimeAnimatorController>(DefaultControllerResourceName);
        if (loaded != null)
            animator.runtimeAnimatorController = loaded;
    }

    void CacheParams()
    {
        hasIdle = false;
        hasRun = false;
        if (animator == null || animator.runtimeAnimatorController == null) return;

        foreach (var p in animator.parameters)
        {
            if (p.nameHash == IdleTrigger && p.type == AnimatorControllerParameterType.Trigger)
                hasIdle = true;
            if (p.nameHash == RunTrigger && p.type == AnimatorControllerParameterType.Trigger)
                hasRun = true;
        }
    }

    void LateUpdate()
    {
        if (visualRoot == null)
            ResolveRefs();

        float dt = Time.deltaTime;
        Vector3 pos = transform.position;

        Vector3 delta = pos - lastPos;
        delta.y = 0f;
        bool movingNow = false;
        if (dt > 0.0001f)
            movingNow = (delta.magnitude / dt) >= moveThreshold;

        // Held look (station / queue / path) — turn toward locked heading, but still
        // drive run/idle from real movement so walking keeps the run animation.
        if (lockFacingFrames > 0)
        {
            lockFacingFrames--;
            FaceDirection(lockedFaceDir, smooth: true);
            UpdateAnimTriggers(movingNow);
            lastPos = pos;
            return;
        }

        if (dt <= 0.0001f)
        {
            lastPos = pos;
            return;
        }

        lastPos = pos;
        UpdateAnimTriggers(movingNow);

        if (movingNow && delta.sqrMagnitude > 0.0001f)
            FaceDirection(GetCardinalDirection(delta), smooth: true);
    }

    void UpdateAnimTriggers(bool movingNow)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            isMoving = movingNow;
            return;
        }

        if (movingNow == isMoving) return;
        isMoving = movingNow;

        if (isMoving)
        {
            if (hasIdle) animator.ResetTrigger(IdleTrigger);
            if (hasRun) animator.SetTrigger(RunTrigger);
        }
        else
        {
            if (hasRun) animator.ResetTrigger(RunTrigger);
            if (hasIdle) animator.SetTrigger(IdleTrigger);
        }
    }

    void FaceDirection(Vector3 dir, bool smooth)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        if (visualRoot == null) visualRoot = transform;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        if (!smooth || turnSpeed <= 0f || Time.deltaTime <= 0.0001f)
        {
            visualRoot.rotation = targetRot;
            return;
        }

        float t = 1f - Mathf.Exp(-turnSpeed * Time.deltaTime);
        visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, targetRot, t);
    }

    /// <summary>
    /// Dominant N/S/E/W direction from one world point toward another,
    /// using grid cells when available so characters face from their own square.
    /// </summary>
    public static Vector3 GetCardinalToward(Vector3 fromWorld, Vector3 toWorld)
    {
        Vector3 from = fromWorld;
        Vector3 to = toWorld;
        GridManager grid = GridManager.Instance;
        if (grid != null)
        {
            from = grid.GetCellCenter(fromWorld);
            to = grid.GetCellCenter(toWorld);
        }

        Vector3 delta = to - from;
        delta.y = 0f;
        if (delta.sqrMagnitude < 0.0001f)
        {
            delta = toWorld - fromWorld;
            delta.y = 0f;
        }
        return GetCardinalDirection(delta);
    }

    public static Vector3 GetCardinalDirection(Vector3 delta)
    {
        delta.y = 0f;
        if (delta.sqrMagnitude < 0.0001f)
            return Vector3.forward;

        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.z))
            return new Vector3(Mathf.Sign(delta.x), 0f, 0f);
        return new Vector3(0f, 0f, Mathf.Sign(delta.z));
    }

    /// <summary>Smooth yaw toward a world point (free aim — used rarely).</summary>
    public void FaceToward(Vector3 worldPoint, bool smooth = true)
    {
        Vector3 from = visualRoot != null ? visualRoot.position : transform.position;
        Vector3 dir = worldPoint - from;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        FaceDirection(dir, smooth);
    }

    /// <summary>
    /// Smoothly face an object from this character's tile using a grid-aligned heading
    /// (straight N/S/E/W toward the neighboring square / nearest edge of the target).
    /// </summary>
    public void FaceTowardAdjacent(Vector3 targetWorld, bool smooth = true)
    {
        Vector3 dir = GetCardinalToward(transform.position, targetWorld);
        lockedFaceDir = dir;
        lockFacingFrames = 3; // refreshed each Update while working / waiting
        FaceDirection(dir, smooth);
    }

    /// <summary>Face a GameObject from this tile — aims at the nearest collider edge, then snaps to N/S/E/W.</summary>
    public void FaceTowardAdjacentObject(GameObject target, bool smooth = true)
    {
        if (target == null) return;
        FaceTowardAdjacent(GetNearestAimPoint(transform.position, target), smooth);
    }

    public static Vector3 GetNearestAimPoint(Vector3 fromWorld, GameObject target)
    {
        if (target == null) return fromWorld;

        var cols = target.GetComponentsInChildren<Collider>();
        bool any = false;
        Vector3 best = target.transform.position;
        float bestDist = float.MaxValue;
        for (int i = 0; i < cols.Length; i++)
        {
            Collider col = cols[i];
            if (col == null || !col.enabled) continue;
            Vector3 p = col.ClosestPoint(fromWorld);
            float d = (p - fromWorld).sqrMagnitude;
            if (!any || d < bestDist)
            {
                any = true;
                bestDist = d;
                best = p;
            }
        }
        if (any) return best;

        // Fallback: renderer bounds center / transform
        var rend = target.GetComponentInChildren<Renderer>();
        if (rend != null)
            return rend.bounds.ClosestPoint(fromWorld);
        return target.transform.position;
    }
}
