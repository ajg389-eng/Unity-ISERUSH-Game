using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// In-game clock and Sims-style speed control.
/// Day runs 10 AM → 10 PM; 1 game hour = 1 real minute at 1x speed.
/// Pause sets Time.timeScale to 0 (simulation freezes) but UI input and camera still work.
/// </summary>
public class GameTimeManager : MonoBehaviour
{
    public static GameTimeManager Instance { get; private set; }

    public enum SpeedMode
    {
        Paused = 0,
        Play = 1,
        FastForward = 2,
        SuperFast = 3
    }

    public const string PauseTitleScreen = "TitleScreen";
    public const string PauseManagement = "Management";
    public const string PauseMenu = "PauseMenu";
    public const string PauseOnboarding = "OnboardingTutorial";

    [Header("Day schedule")]
    [Tooltip("Hour the shift starts (24h). Default 10 = 10 AM.")]
    public int dayStartHour = 10;
    [Tooltip("Hour the shift ends (24h). Default 22 = 10 PM.")]
    public int dayEndHour = 22;

    [Header("Timing")]
    [Tooltip("Real seconds per in-game hour at 1x speed.")]
    public float realSecondsPerGameHour = 60f;
    [Tooltip("Unity time scale while fast-forwarding.")]
    public float fastForwardTimeScale = 3f;
    [Tooltip("Unity time scale for debug super-speed.")]
    public float superFastTimeScale = 20f;
    [Tooltip("Pause automatically when the shift ends.")]
    public bool pauseAtDayEnd = true;

    public SpeedMode CurrentSpeed { get; private set; } = SpeedMode.Play;
    public int CurrentDay { get; private set; } = 1;
    public bool IsShiftOver { get; private set; }

    /// <summary>Minutes from midnight (fractional).</summary>
    public float CurrentMinutes { get; private set; }

    public event Action OnSpeedChanged;
    public event Action OnTimeChanged;
    public event Action OnDayEnded;
    public event Action OnDayStarted;

    readonly HashSet<string> externalPauseSources = new HashSet<string>();

    float DayStartMinutes => dayStartHour * 60f;
    float DayEndMinutes => dayEndHour * 60f;
    float GameMinutesPerRealSecond => 60f / Mathf.Max(1f, realSecondsPerGameHour);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<GameTimeManager>() != null) return;
        var go = new GameObject("GameTimeManager");
        go.AddComponent<GameTimeManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        ResetDayClock();
        TimeOfDaySkyboxController.EnsureOn(gameObject);
        CelestialBodyController.EnsureOn(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        ApplyTimeScale();

        if (Time.timeScale <= 0f || IsShiftOver)
            return;

        CurrentMinutes += GameMinutesPerRealSecond * Time.deltaTime;

        if (CurrentMinutes >= DayEndMinutes)
        {
            CurrentMinutes = DayEndMinutes;
            EndShift();
        }

        OnTimeChanged?.Invoke();
    }

    public void ResetDayClock()
    {
        CurrentMinutes = DayStartMinutes;
        IsShiftOver = false;
        OnTimeChanged?.Invoke();
    }

    public void StartNextDay()
    {
        CurrentDay++;
        ResetDayClock();
        SetSpeed(SpeedMode.Play);
        OnDayStarted?.Invoke();
    }

    /// <summary>Debug helper that ends the shift through the normal day-end path.</summary>
    public bool DebugSkipToEndOfDay()
    {
        if (IsShiftOver) return false;
        CurrentMinutes = DayEndMinutes;
        OnTimeChanged?.Invoke();
        EndShift();
        return true;
    }

    public void SetSpeed(SpeedMode mode)
    {
        if (IsShiftOver && mode != SpeedMode.Paused)
            return;

        if (CurrentSpeed == mode) return;
        CurrentSpeed = mode;
        ApplyTimeScale();
        OnSpeedChanged?.Invoke();
    }

    public void TogglePausePlay()
    {
        if (CurrentSpeed == SpeedMode.Paused)
            SetSpeed(SpeedMode.Play);
        else
            SetSpeed(SpeedMode.Paused);
    }

    public void RequestExternalPause(string source)
    {
        if (string.IsNullOrEmpty(source)) return;
        externalPauseSources.Add(source);
        ApplyTimeScale();
    }

    public void ReleaseExternalPause(string source)
    {
        if (string.IsNullOrEmpty(source)) return;
        externalPauseSources.Remove(source);
        ApplyTimeScale();
    }

    public bool IsExternallyPaused => externalPauseSources.Count > 0;
    public bool IsSimulationPaused => Time.timeScale <= 0f;

    public int CurrentHour24 => Mathf.FloorToInt(CurrentMinutes / 60f) % 24;

    /// <summary>Lunch 12–2 and dinner 5–7, once Milestone 2 is reached.</summary>
    public bool IsRushHour
    {
        get
        {
            if (!MilestoneFeatures.RushHourUnlocked || IsShiftOver) return false;
            int hour = CurrentHour24;
            return (hour >= 12 && hour < 14) || (hour >= 17 && hour < 19);
        }
    }

    public string RushHourLabel
    {
        get
        {
            if (!IsRushHour) return "";
            int hour = CurrentHour24;
            return hour >= 17 ? "Dinner rush" : "Lunch rush";
        }
    }

    public string GetClockText()
    {
        int total = Mathf.FloorToInt(CurrentMinutes);
        int hour24 = (total / 60) % 24;
        int minute = total % 60;
        bool pm = hour24 >= 12;
        int hour12 = hour24 % 12;
        if (hour12 == 0) hour12 = 12;
        return $"{hour12}:{minute:00} {(pm ? "PM" : "AM")}";
    }

    public string GetDayText() => "Day " + CurrentDay;

    void ApplyTimeScale()
    {
        if (externalPauseSources.Count > 0 || CurrentSpeed == SpeedMode.Paused || IsShiftOver)
        {
            Time.timeScale = 0f;
            return;
        }

        if (CurrentSpeed == SpeedMode.SuperFast)
            Time.timeScale = Mathf.Max(1f, superFastTimeScale);
        else if (CurrentSpeed == SpeedMode.FastForward)
            Time.timeScale = Mathf.Max(1f, fastForwardTimeScale);
        else
            Time.timeScale = 1f;
    }

    public string GetSpeedLabel()
    {
        switch (CurrentSpeed)
        {
            case SpeedMode.Paused: return "Paused";
            case SpeedMode.Play: return "1x";
            case SpeedMode.FastForward: return $"{fastForwardTimeScale:0.##}x";
            case SpeedMode.SuperFast: return $"{superFastTimeScale:0.##}x";
            default: return CurrentSpeed.ToString();
        }
    }

    void EndShift()
    {
        if (IsShiftOver) return;
        IsShiftOver = true;
        if (pauseAtDayEnd)
            SetSpeed(SpeedMode.Paused);
        OnDayEnded?.Invoke();
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.DayEnded);
    }
}

/// <summary>
/// Drives the imported Customizable Skybox "Stylized/Sky" shader from the game clock.
/// Values are based on the package's Day1, Sunset5, and Night4 material presets.
/// </summary>
public sealed class TimeOfDaySkyboxController : MonoBehaviour
{
    const string SkyShaderName = "Stylized/Sky";

    static readonly int SkyTop = Shader.PropertyToID("_SkyGradientTop");
    static readonly int SkyBottom = Shader.PropertyToID("_SkyGradientBottom");
    static readonly int SkyExponent = Shader.PropertyToID("_SkyGradientExponent");
    static readonly int SunDiscColor = Shader.PropertyToID("_SunDiscColor");
    static readonly int SunDiscMultiplier = Shader.PropertyToID("_SunDiscMultiplier");
    static readonly int SunDiscExponent = Shader.PropertyToID("_SunDiscExponent");
    static readonly int SunHaloColor = Shader.PropertyToID("_SunHaloColor");
    static readonly int SunHaloExponent = Shader.PropertyToID("_SunHaloExponent");
    static readonly int SunHaloContribution = Shader.PropertyToID("_SunHaloContribution");
    static readonly int HorizonColor = Shader.PropertyToID("_HorizonLineColor");
    static readonly int HorizonExponent = Shader.PropertyToID("_HorizonLineExponent");
    static readonly int HorizonContribution = Shader.PropertyToID("_HorizonLineContribution");

    struct SkyPreset
    {
        public Color top;
        public Color bottom;
        public float gradientExponent;
        public Color sun;
        public float sunMultiplier;
        public float sunExponent;
        public Color halo;
        public float haloExponent;
        public float haloContribution;
        public Color horizon;
        public float horizonExponent;
        public float horizonContribution;
    }

    static readonly SkyPreset Day = new SkyPreset
    {
        top = new Color(0.1725f, 0.5686f, 0.6941f, 1f),
        bottom = new Color(0.7647f, 0.8157f, 0.8510f, 1f),
        gradientExponent = 2.5f,
        sun = Color.white,
        sunMultiplier = 25f,
        sunExponent = 125000f,
        halo = new Color(0.8980f, 0.8333f, 0.6667f, 1f),
        haloExponent = 125f,
        haloContribution = 0.5f,
        horizon = new Color(0.7922f, 0.8708f, 0.9059f, 1f),
        horizonExponent = 4f,
        horizonContribution = 0.177f
    };

    static readonly SkyPreset Sunset = new SkyPreset
    {
        top = new Color(0.2336f, 0.2368f, 0.7059f, 1f),
        bottom = new Color(0.4853f, 0.3354f, 0.4853f, 1f),
        gradientExponent = 0.25f,
        sun = new Color(1f, 0.9412f, 0.8706f, 1f),
        sunMultiplier = 100000f,
        sunExponent = 100000f,
        halo = new Color(1f, 0.3738f, 0.0980f, 1f),
        haloExponent = 250f,
        haloContribution = 0.5f,
        horizon = new Color(0.8549f, 0.7765f, 0.8471f, 1f),
        horizonExponent = 5f,
        horizonContribution = 0.32f
    };

    static readonly SkyPreset Night = new SkyPreset
    {
        top = new Color(0.035f, 0.065f, 0.13f, 1f),
        bottom = new Color(0.09f, 0.14f, 0.20f, 1f),
        gradientExponent = 0.75f,
        sun = new Color(0.64f, 0.76f, 0.86f, 1f),
        sunMultiplier = 500f,
        sunExponent = 250000f,
        halo = new Color(0.0476f, 0.8088f, 0.7143f, 1f),
        haloExponent = 500f,
        haloContribution = 0.04f,
        horizon = new Color(0.10f, 0.15f, 0.21f, 1f),
        horizonExponent = 12.3f,
        horizonContribution = 0.121f
    };

    Material originalSkybox;
    Material runtimeSkybox;
    GameTimeManager clock;
    float lastAppliedMinutes = float.MinValue;
    float nextEnvironmentRefresh;
    Light daylightSource;
    float originalSunIntensity;
    float originalAmbientIntensity;
    float originalReflectionIntensity;
    bool lightingCaptured;

    public static void EnsureOn(GameObject host)
    {
        if (host != null && host.GetComponent<TimeOfDaySkyboxController>() == null)
            host.AddComponent<TimeOfDaySkyboxController>();
    }

    void Awake()
    {
        clock = GetComponent<GameTimeManager>();
        originalSkybox = RenderSettings.skybox;
        Shader shader = Shader.Find(SkyShaderName);
        if (shader == null)
        {
            Debug.LogWarning("Time-of-day skybox could not find the Customizable Skybox shader: " + SkyShaderName);
            enabled = false;
            return;
        }

        runtimeSkybox = originalSkybox != null && originalSkybox.shader == shader
            ? new Material(originalSkybox)
            : new Material(shader);
        runtimeSkybox.name = "Time Of Day Skybox (Runtime)";
        RenderSettings.skybox = runtimeSkybox;
        daylightSource = RenderSettings.sun;
        if (daylightSource == null)
        {
            foreach (Light candidate in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (candidate.type != LightType.Directional || !candidate.isActiveAndEnabled) continue;
                if (daylightSource == null || candidate.intensity > daylightSource.intensity)
                    daylightSource = candidate;
            }
        }
        if (daylightSource != null) originalSunIntensity = daylightSource.intensity;
        originalAmbientIntensity = RenderSettings.ambientIntensity;
        originalReflectionIntensity = RenderSettings.reflectionIntensity;
        lightingCaptured = true;
        ApplySky(forceEnvironmentRefresh: true);
    }

    void Update()
    {
        if (clock == null)
            clock = GameTimeManager.Instance;
        ApplySky(forceEnvironmentRefresh: false);
    }

    void ApplySky(bool forceEnvironmentRefresh)
    {
        if (runtimeSkybox == null || clock == null) return;
        float minutes = clock.CurrentMinutes;
        if (!forceEnvironmentRefresh && Mathf.Abs(minutes - lastAppliedMinutes) < 0.1f)
            return;

        float hour = minutes / 60f;
        SkyPreset value;
        if (hour < 16f)
            value = Day;
        else if (hour < 18f)
            value = Lerp(Day, Sunset, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(16f, 18f, hour)));
        else if (hour < 20f)
            value = Lerp(Sunset, Night, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 20f, hour)));
        else
            value = Night;

        SetPreset(value);
        float daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 9f, hour))
            * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(17f, 20f, hour)));
        runtimeSkybox.SetFloat("_WeatherDaylight", daylight);
        float cloudVisibility = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 9f, hour))
            * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(16f, 18f, hour)));
        runtimeSkybox.SetFloat("_CloudVisibility", cloudVisibility);
        // Keep night visibly darker than day without obscuring stations, workers,
        // floor markings, or customer queues.
        if (daylightSource != null)
            daylightSource.intensity = originalSunIntensity * Mathf.Lerp(0.30f, 1f, daylight);
        RenderSettings.ambientIntensity = originalAmbientIntensity * Mathf.Lerp(0.55f, 1f, daylight);
        RenderSettings.reflectionIntensity = originalReflectionIntensity * Mathf.Lerp(0.45f, 1f, daylight);
        runtimeSkybox.SetFloat("_WeatherTime", minutes * 0.008f);
        runtimeSkybox.SetFloat("_ConstellationIndex", (clock.CurrentDay - 1) % 3);
        lastAppliedMinutes = minutes;

        if (forceEnvironmentRefresh || Time.unscaledTime >= nextEnvironmentRefresh)
        {
            DynamicGI.UpdateEnvironment();
            nextEnvironmentRefresh = Time.unscaledTime + 0.5f;
        }
    }

    void SetPreset(SkyPreset value)
    {
        runtimeSkybox.SetColor(SkyTop, value.top);
        runtimeSkybox.SetColor(SkyBottom, value.bottom);
        runtimeSkybox.SetFloat(SkyExponent, value.gradientExponent);
        runtimeSkybox.SetColor(SunDiscColor, value.sun);
        // The separate emissive sun mesh is the visible disc. Keep the shader's own
        // directional-light disc disabled so two suns never appear.
        runtimeSkybox.SetFloat(SunDiscMultiplier, 0f);
        runtimeSkybox.SetFloat(SunDiscExponent, value.sunExponent);
        runtimeSkybox.SetColor(SunHaloColor, value.halo);
        runtimeSkybox.SetFloat(SunHaloExponent, value.haloExponent);
        runtimeSkybox.SetFloat(SunHaloContribution, value.haloContribution);
        runtimeSkybox.SetColor(HorizonColor, value.horizon);
        runtimeSkybox.SetFloat(HorizonExponent, value.horizonExponent);
        runtimeSkybox.SetFloat(HorizonContribution, value.horizonContribution);
    }

    static SkyPreset Lerp(SkyPreset a, SkyPreset b, float t)
    {
        return new SkyPreset
        {
            top = Color.Lerp(a.top, b.top, t),
            bottom = Color.Lerp(a.bottom, b.bottom, t),
            gradientExponent = Mathf.Lerp(a.gradientExponent, b.gradientExponent, t),
            sun = Color.Lerp(a.sun, b.sun, t),
            sunMultiplier = Mathf.Lerp(a.sunMultiplier, b.sunMultiplier, t),
            sunExponent = Mathf.Lerp(a.sunExponent, b.sunExponent, t),
            halo = Color.Lerp(a.halo, b.halo, t),
            haloExponent = Mathf.Lerp(a.haloExponent, b.haloExponent, t),
            haloContribution = Mathf.Lerp(a.haloContribution, b.haloContribution, t),
            horizon = Color.Lerp(a.horizon, b.horizon, t),
            horizonExponent = Mathf.Lerp(a.horizonExponent, b.horizonExponent, t),
            horizonContribution = Mathf.Lerp(a.horizonContribution, b.horizonContribution, t)
        };
    }

    void OnDestroy()
    {
        if (RenderSettings.skybox == runtimeSkybox)
            RenderSettings.skybox = originalSkybox;
        if (lightingCaptured)
        {
            if (daylightSource != null) daylightSource.intensity = originalSunIntensity;
            RenderSettings.ambientIntensity = originalAmbientIntensity;
            RenderSettings.reflectionIntensity = originalReflectionIntensity;
        }
        if (runtimeSkybox != null)
            Destroy(runtimeSkybox);
    }
}

/// <summary>Creates billboarded emissive sun and moon circles that orbit with the game clock.</summary>
public sealed class CelestialBodyController : MonoBehaviour
{
    const float SunriseHour = 6f;
    const float SunsetHour = 20f;

    GameTimeManager clock;
    Camera viewCamera;
    Transform sun;
    Transform moon;
    Mesh circleMesh;
    Material sunMaterial;
    Material moonMaterial;
    Mesh moonPhaseMesh;
    int moonPhaseDay = -1;

    public static void EnsureOn(GameObject host)
    {
        if (host != null && host.GetComponent<CelestialBodyController>() == null)
            host.AddComponent<CelestialBodyController>();
    }

    void Awake()
    {
        clock = GetComponent<GameTimeManager>();
        circleMesh = CreateCircleMesh(48);
        sunMaterial = CreateEmissiveMaterial("Sun Emissive", new Color(4.5f, 2.8f, 0.65f, 1f));
        moonMaterial = CreateEmissiveMaterial("Moon Emissive", new Color(1.15f, 1.45f, 2.2f, 1f));
        sun = CreateBody("Time Of Day Sun", sunMaterial).transform;
        moon = CreateBody("Time Of Day Moon", moonMaterial).transform;
    }

    GameObject CreateBody(string bodyName, Material material)
    {
        var body = new GameObject(bodyName);
        body.transform.SetParent(transform, false);
        var filter = body.AddComponent<MeshFilter>();
        filter.sharedMesh = circleMesh;
        var renderer = body.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return body;
    }

    static Mesh CreateCircleMesh(int segments)
    {
        segments = Mathf.Max(12, segments);
        var vertices = new Vector3[segments + 1];
        var triangles = new int[segments * 3];
        vertices[0] = Vector3.zero;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 0.5f, Mathf.Sin(angle) * 0.5f, 0f);
            int next = (i + 1) % segments;
            int triangle = i * 3;
            // Reverse winding so the face points toward a camera located behind local Z.
            triangles[triangle] = 0;
            triangles[triangle + 1] = next + 1;
            triangles[triangle + 2] = i + 1;
        }

        var mesh = new Mesh { name = "Celestial Circle (Runtime)" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Material CreateEmissiveMaterial(string materialName, Color hdrColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            return null;

        var material = new Material(shader) { name = materialName + " (Runtime)" };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", hdrColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", hdrColor);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", hdrColor);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
        material.EnableKeyword("_EMISSION");
        return material;
    }

    void LateUpdate()
    {
        if (clock == null)
            clock = GameTimeManager.Instance;
        if (viewCamera == null || !viewCamera.isActiveAndEnabled)
            viewCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (clock == null || viewCamera == null || sun == null || moon == null)
            return;

        UpdateMoonPhase();

        float hour = clock.CurrentMinutes / 60f;
        float phase = ((hour - SunriseHour) / (SunsetHour - SunriseHour)) * Mathf.PI;
        Vector3 sunDirection = new Vector3(-Mathf.Cos(phase), Mathf.Sin(phase), 0.28f).normalized;
        Vector3 moonDirection = -sunDirection;

        float distance = Mathf.Clamp(viewCamera.farClipPlane * 0.65f, 80f, 500f);
        float baseDiameter = distance * 0.055f;
        PositionBody(sun, sunDirection, distance, baseDiameter, sunDirection.y > -0.04f);
        PositionBody(moon, moonDirection, distance, baseDiameter * 0.78f, moonDirection.y > -0.04f);
    }

    void PositionBody(Transform body, Vector3 direction, float distance, float diameter, bool visible)
    {
        body.gameObject.SetActive(visible);
        if (!visible) return;
        body.position = viewCamera.transform.position + direction * distance;
        body.rotation = Quaternion.LookRotation(direction, Vector3.up);
        body.localScale = Vector3.one * diameter;
    }

    void UpdateMoonPhase()
    {
        if (moonPhaseDay == clock.CurrentDay) return;
        moonPhaseDay = clock.CurrentDay;
        float fullness = Mathf.Lerp(0.15f, 1f, Mathf.Clamp01((moonPhaseDay - 1f) / 6f));
        const int segments = 48;
        var vertices = new Vector3[(segments + 1) * 2];
        var triangles = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, i / (float)segments);
            float y = Mathf.Sin(angle) * 0.5f;
            float rim = Mathf.Cos(angle) * 0.5f;
            vertices[i * 2] = new Vector3((1f - 2f * fullness) * rim, y, 0);
            vertices[i * 2 + 1] = new Vector3(rim, y, 0);
            if (i == segments) continue;
            int t = i * 6, v = i * 2;
            triangles[t] = v; triangles[t+1] = v+2; triangles[t+2] = v+1;
            triangles[t+3] = v+1; triangles[t+4] = v+2; triangles[t+5] = v+3;
        }
        if (moonPhaseMesh == null) moonPhaseMesh = new Mesh { name = "Daily Moon Phase" };
        moonPhaseMesh.Clear();
        moonPhaseMesh.vertices = vertices;
        moonPhaseMesh.triangles = triangles;
        moonPhaseMesh.RecalculateNormals();
        moonPhaseMesh.RecalculateBounds();
        moon.GetComponent<MeshFilter>().sharedMesh = moonPhaseMesh;
    }

    void OnDestroy()
    {
        if (sun != null) Destroy(sun.gameObject);
        if (moon != null) Destroy(moon.gameObject);
        if (sunMaterial != null) Destroy(sunMaterial);
        if (moonMaterial != null) Destroy(moonMaterial);
        if (circleMesh != null) Destroy(circleMesh);
        if (moonPhaseMesh != null) Destroy(moonPhaseMesh);
    }
}
