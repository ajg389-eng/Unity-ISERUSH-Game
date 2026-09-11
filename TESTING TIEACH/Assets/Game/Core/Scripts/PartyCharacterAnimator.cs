using UnityEngine;

/// <summary>
/// Drives FREE Party Characters (char_AC) idle/run triggers from movement.
/// Put on the KitchenWorker / Customer root (or the character model).
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

    [Tooltip("How fast the model turns to face movement.")]
    public float turnSpeed = 12f;

    static readonly int IdleTrigger = Animator.StringToHash("idle");
    static readonly int RunTrigger = Animator.StringToHash("run");

    Vector3 lastPos;
    bool isMoving;
    bool hasIdle;
    bool hasRun;

    /// <summary>Add this component if the object (or a child) has an Animator.</summary>
    public static PartyCharacterAnimator EnsureOn(GameObject go)
    {
        if (go == null) return null;
        var existing = go.GetComponent<PartyCharacterAnimator>();
        if (existing != null) return existing;
        if (go.GetComponentInChildren<Animator>(true) == null) return null;
        return go.AddComponent<PartyCharacterAnimator>();
    }

    void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (visualRoot == null && animator != null)
            visualRoot = animator.transform;

        if (animator != null)
        {
            EnsureController();
            animator.applyRootMotion = false;
            CacheParams();
        }

        lastPos = transform.position;
        if (animator != null && hasIdle)
            animator.SetTrigger(IdleTrigger);
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
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
            if (animator == null) return;
            EnsureController();
            CacheParams();
        }

        if (animator.runtimeAnimatorController == null)
        {
            EnsureController();
            CacheParams();
            if (animator.runtimeAnimatorController == null)
                return;
        }

        Vector3 pos = transform.position;
        Vector3 delta = pos - lastPos;
        delta.y = 0f;
        float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPos = pos;

        bool movingNow = speed >= moveThreshold;
        if (movingNow != isMoving)
        {
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

        if (movingNow && visualRoot != null && delta.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(delta.normalized, Vector3.up);
            visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, targetRot, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
        }
    }
}
