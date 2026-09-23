using System.Collections.Generic;
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
    const string HumanoidAvatarResourceName = "party_character_humanoid";
    const string SpatulaResourceName = "Prop_Spatula_05";

    /// <summary>Work clip played while a worker is actively using a station.</summary>
    public enum StationWorkKind
    {
        None = 0,
        Grill,
        Fryer,
        Assembly,
        Freezer,
        Drink,
        Register,
        Plating,
        Attention
    }

    [Tooltip("Animator on the party character. Auto-found in children if empty.")]
    public Animator animator;

    [Tooltip("Optional: assign Assets/Imported/FREE/Pack_FREE_PartyCharacters/Animations/char_AC")]
    public RuntimeAnimatorController controller;

    [Tooltip("Optional override for grill cooking. Defaults to Resources/grilling_a_meat_party.")]
    public RuntimeAnimatorController grillCookingController;

    [Tooltip("Humanoid avatar used only while working a station (keeps Generic idle/run intact).")]
    public Avatar cookHumanoidAvatar;

    [Tooltip("Spatula shown in the worker's hand while grilling.")]
    public GameObject spatulaPrefab;

    [Tooltip("Local position of the spatula on the right hand.")]
    public Vector3 spatulaLocalPosition = new Vector3(0.02f, 0.04f, 0.08f);

    [Tooltip("Local euler angles of the spatula on the right hand.")]
    public Vector3 spatulaLocalEuler = new Vector3(0f, 90f, 90f);

    [Tooltip("Target world-space length of the spatula (meters).")]
    public float spatulaWorldLength = 0.55f;

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
    RuntimeAnimatorController locomotionController;
    Avatar locomotionAvatar;
    StationWorkKind activeWork;
    GameObject spatulaInstance;
    AnimationClip attentionWaveClip;
    RuntimeAnimatorController attentionWaveController;
    AnimatorUpdateMode attentionPreviousUpdateMode;
    bool attentionUpdateModeSaved;
    static readonly Dictionary<StationWorkKind, RuntimeAnimatorController> cachedWorkControllers =
        new Dictionary<StationWorkKind, RuntimeAnimatorController>();
    static Avatar cachedHumanoidAvatar;
    static GameObject cachedSpatulaPrefab;

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

        // Workers asking for help keep facing the player, including while
        // Management mode pauses scaled game time.
        if (activeWork == StationWorkKind.Attention)
        {
            Camera playerCamera = Camera.main;
            if (playerCamera != null)
            {
                Vector3 towardCamera = playerCamera.transform.position - visualRoot.position;
                towardCamera.y = 0f;
                FaceDirection(towardCamera, smooth: true);
            }
            lastPos = pos;
            return;
        }

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
            FaceDirection(delta, smooth: true);
    }

    void UpdateAnimTriggers(bool movingNow)
    {
        if (activeWork != StationWorkKind.None) return;
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

    /// <summary>
    /// Play a remapped EEJANAI cooking clip while a worker is at a station.
    /// Temporarily swaps to a Humanoid avatar so muscle curves work, then restores
    /// the Generic avatar + locomotion controller for idle/run.
    /// </summary>
    public void SetStationWork(StationWorkKind work)
    {
        if (animator == null)
            ResolveRefs();
        if (animator == null) return;
        if (work == activeWork) return;

        bool wasWorking = activeWork != StationWorkKind.None;
        StationWorkKind previous = activeWork;
        activeWork = work;

        if (previous == StationWorkKind.Grill)
            ShowSpatula(false);

        if (work == StationWorkKind.None)
        {
            if (!wasWorking) return;
            RestoreLocomotion();
            return;
        }

        if (work == StationWorkKind.Attention && !attentionUpdateModeSaved)
        {
            attentionPreviousUpdateMode = animator.updateMode;
            attentionUpdateModeSaved = true;
        }

        if (locomotionController == null)
            locomotionController = animator.runtimeAnimatorController;
        if (locomotionAvatar == null)
            locomotionAvatar = animator.avatar;

        var cook = ResolveWorkController(work);
        var humanoid = ResolveCookHumanoidAvatar();
        if (cook != null)
        {
            if (humanoid != null)
                animator.avatar = humanoid;
            animator.runtimeAnimatorController = cook;
            if (work == StationWorkKind.Attention)
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.applyRootMotion = false;
            animator.Rebind();
            animator.Update(0f);
            animator.Play(0, 0, 0f);
        }

        if (work == StationWorkKind.Grill)
            ShowSpatula(true);
    }

    /// <summary>Legacy alias — grill cooking with spatula.</summary>
    public void SetCookUsingPan(bool on)
    {
        SetGrillCooking(on);
    }

    public void SetGrillCooking(bool on)
    {
        SetStationWork(on ? StationWorkKind.Grill : StationWorkKind.None);
    }

    /// <summary>Loop a humanoid wave until the worker's blocking condition is resolved.</summary>
    public void SetAttentionWave(bool on, AnimationClip waveClip)
    {
        if (waveClip != null && attentionWaveClip != waveClip)
        {
            attentionWaveClip = waveClip;
            attentionWaveController = null;
        }
        SetStationWork(on ? StationWorkKind.Attention : StationWorkKind.None);
    }

    void RestoreLocomotion()
    {
        ShowSpatula(false);
        if (attentionUpdateModeSaved)
        {
            animator.updateMode = attentionPreviousUpdateMode;
            attentionUpdateModeSaved = false;
        }
        if (locomotionAvatar != null)
            animator.avatar = locomotionAvatar;
        if (locomotionController != null)
            animator.runtimeAnimatorController = locomotionController;
        else
            EnsureController();

        animator.applyRootMotion = false;
        animator.Rebind();
        animator.Update(0f);
        CacheParams();
        isMoving = false;
        if (hasRun) animator.ResetTrigger(RunTrigger);
        if (hasIdle)
        {
            animator.ResetTrigger(IdleTrigger);
            animator.SetTrigger(IdleTrigger);
        }
    }

    static string WorkControllerResourceName(StationWorkKind work)
    {
        switch (work)
        {
            case StationWorkKind.Grill: return "grilling_a_meat_party";
            case StationWorkKind.Fryer: return "cook_using_pan_party";
            case StationWorkKind.Assembly: return "cutting_vegetables_party";
            case StationWorkKind.Freezer: return "washing_vegetables_party";
            case StationWorkKind.Drink: return "pouring_water_party";
            case StationWorkKind.Register: return "plating_food_party";
            case StationWorkKind.Plating: return "plating_food_party";
            default: return null;
        }
    }

    RuntimeAnimatorController ResolveWorkController(StationWorkKind work)
    {
        if (work == StationWorkKind.Attention)
            return ResolveAttentionWaveController();

        if (work == StationWorkKind.Grill && grillCookingController != null)
            return grillCookingController;

        if (cachedWorkControllers.TryGetValue(work, out var cached) && cached != null)
            return cached;

        string resource = WorkControllerResourceName(work);
        if (string.IsNullOrEmpty(resource)) return null;

        var loaded = Resources.Load<RuntimeAnimatorController>(resource);
        if (loaded != null)
            cachedWorkControllers[work] = loaded;
        return loaded;
    }

    RuntimeAnimatorController ResolveAttentionWaveController()
    {
        if (attentionWaveController != null) return attentionWaveController;
        if (attentionWaveClip == null) return null;

        var baseController = Resources.Load<RuntimeAnimatorController>("plating_food_party");
        if (baseController == null) return null;

        var replacement = new AnimatorOverrideController(baseController);
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        replacement.GetOverrides(overrides);
        for (int i = 0; i < overrides.Count; i++)
            overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, attentionWaveClip);
        replacement.ApplyOverrides(overrides);
        attentionWaveController = replacement;
        return attentionWaveController;
    }

    void ShowSpatula(bool on)
    {
        if (!on)
        {
            if (spatulaInstance != null)
            {
                Destroy(spatulaInstance);
                spatulaInstance = null;
            }
            return;
        }

        if (spatulaInstance != null) return;

        var prefab = ResolveSpatulaPrefab();
        Transform hand = FindRightHand();
        if (prefab == null || hand == null) return;

        spatulaInstance = Instantiate(prefab, hand, false);
        spatulaInstance.name = "GrillSpatula";
        spatulaInstance.transform.localPosition = spatulaLocalPosition;
        spatulaInstance.transform.localRotation = Quaternion.Euler(spatulaLocalEuler);
        spatulaInstance.transform.localScale = Vector3.one;
        FitSpatulaWorldSize(spatulaInstance.transform, Mathf.Max(0.05f, spatulaWorldLength));

        // Props shouldn't collide with kitchen geometry.
        var cols = spatulaInstance.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
                cols[i].enabled = false;
        }
    }

    static void FitSpatulaWorldSize(Transform spatula, float targetWorldLength)
    {
        if (spatula == null) return;

        var renderers = spatula.GetComponentsInChildren<Renderer>();
        float current = 0f;
        if (renderers != null && renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].enabled)
                    b.Encapsulate(renderers[i].bounds);
            }
            current = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        }

        if (current < 1e-5f)
        {
            // Fallback when mesh bounds aren't ready: match hat-socket cm correction.
            float parentScale = spatula.parent != null
                ? Mathf.Max(1e-4f, Mathf.Abs(spatula.parent.lossyScale.x))
                : 1f;
            spatula.localScale = Vector3.one * (targetWorldLength / parentScale);
            return;
        }

        spatula.localScale *= targetWorldLength / current;
    }

    GameObject ResolveSpatulaPrefab()
    {
        if (spatulaPrefab != null) return spatulaPrefab;
        if (cachedSpatulaPrefab == null)
            cachedSpatulaPrefab = Resources.Load<GameObject>(SpatulaResourceName);
        return cachedSpatulaPrefab;
    }

    Transform FindRightHand()
    {
        if (animator != null)
        {
            Transform bone = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (bone != null) return bone;
        }

        Transform root = visualRoot != null ? visualRoot : transform;
        return FindDeepChild(root, "RightHand");
    }

    static Transform FindDeepChild(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeepChild(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    Avatar ResolveCookHumanoidAvatar()
    {
        if (cookHumanoidAvatar != null)
            return cookHumanoidAvatar;
        if (cachedHumanoidAvatar != null)
            return cachedHumanoidAvatar;

        var avatars = Resources.LoadAll<Avatar>(HumanoidAvatarResourceName);
        if (avatars != null)
        {
            for (int i = 0; i < avatars.Length; i++)
            {
                if (avatars[i] != null && avatars[i].isHuman)
                {
                    cachedHumanoidAvatar = avatars[i];
                    return cachedHumanoidAvatar;
                }
            }
            if (avatars.Length > 0)
                cachedHumanoidAvatar = avatars[0];
        }
        return cachedHumanoidAvatar;
    }

    void OnDestroy()
    {
        if (spatulaInstance != null)
            Destroy(spatulaInstance);
        if (attentionWaveController != null)
            Destroy(attentionWaveController);
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

    /// <summary>Face the actual travel vector, including diagonal headings.</summary>
    public void FaceMovementToward(Vector3 worldPoint, bool smooth = true)
    {
        lockFacingFrames = 0;
        FaceToward(worldPoint, smooth);
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
