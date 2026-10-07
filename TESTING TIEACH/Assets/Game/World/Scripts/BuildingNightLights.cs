using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Warm evening wall fixtures for the kitchen, lobby, and entrance.</summary>
public sealed class BuildingNightLights : MonoBehaviour
{
    sealed class WallLamp
    {
        public Light light;
        public GameObject root;
        public CameraOcclusionWall wall;
        public float intensity;
        public bool roomFill;
    }
    readonly List<WallLamp> lights = new List<WallLamp>();
    readonly List<(GameObject decor, CameraOcclusionWall wall)> decorations = new List<(GameObject, CameraOcclusionWall)>();
    int rebuildFrame = -1;
    GridManager grid;
    GameObject fixtures;
    Material housing, lens, indoorTrim, indoorShade;
    float nextRefresh;

    void Update()
    {
        if (grid == null)
        {
            grid = GridManager.Instance;
            if (grid == null) return;
            grid.GridChanged += QueueRebuild;
            QueueRebuild();
        }
        // Doors and the customer floor may be created after the grid bootstrap.
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 3f;
            if (fixtures == null || lights.Count == 0) QueueRebuild();
        }
        float hour = GameTimeManager.Instance != null ? GameTimeManager.Instance.CurrentMinutes / 60f : 12f;
        float daylight = Mathf.SmoothStep(0,1,Mathf.InverseLerp(6,9,hour))
            * (1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(16,19,hour)));
        float strength = 1-daylight;
        foreach (var item in decorations)
            if (item.decor != null)
                item.decor.SetActive(item.wall != null && item.wall.isActiveAndEnabled && item.wall.DuckAmount < 0.1f);
        foreach (WallLamp fixture in lights)
        {
            if (fixture.root == null) continue;
            bool visible = fixture.roomFill || (fixture.wall != null && fixture.wall.isActiveAndEnabled
                && fixture.wall.DuckAmount < 0.1f);
            fixture.root.SetActive(visible);
            fixture.light.enabled = visible && strength > 0.01f;
            fixture.light.intensity = fixture.intensity * strength;
        }
        if (lens != null) lens.SetColor("_EmissionColor", new Color(1f,0.85f,0.60f)*4f*strength);
        if (indoorShade != null) indoorShade.SetColor("_EmissionColor", new Color(1f,0.94f,0.82f)*2f*strength);
    }

    void Rebuild()
    {
        if (fixtures != null) { fixtures.SetActive(false); Destroy(fixtures); }
        lights.Clear();
        decorations.Clear();
        if (grid == null || grid.floor == null) return;
        if (housing == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;
            housing = new Material(shader);
            housing.SetColor("_BaseColor", new Color(0.13f,0.12f,0.10f));
            lens = new Material(shader);
            lens.SetColor("_BaseColor", new Color(1f,0.9f,0.7f));
            lens.EnableKeyword("_EMISSION");
            indoorTrim = new Material(shader);
            indoorTrim.SetColor("_BaseColor", new Color(0.72f,0.66f,0.54f));
            indoorShade = new Material(shader);
            indoorShade.SetColor("_BaseColor", new Color(1f,0.97f,0.88f));
            indoorShade.EnableKeyword("_EMISSION");
        }
        fixtures = new GameObject("Building Wall Lamps");
        fixtures.transform.SetParent(transform,false);
        AddRoomFill(grid.floor);
        if (grid.customerFloor != null && grid.customerFloor != grid.floor)
            AddRoomFill(grid.customerFloor);
        foreach (var wall in FindObjectsByType<CameraOcclusionWall>(FindObjectsSortMode.None))
        {
            if (!wall.name.StartsWith("KitchenWallGroup_")) continue;
            Vector3 facing = wall.outwardNormal;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.1f) continue;
            facing.Normalize();
            bool found = false;
            Bounds bounds = default;
            var bricks = new List<Bounds>();
            foreach (Renderer renderer in wall.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!renderer.name.StartsWith("KitchenWallBrick_")) continue;
                bricks.Add(renderer.bounds);
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!found) continue;
            bool alongX = Mathf.Abs(facing.z) > Mathf.Abs(facing.x);
            float length = alongX ? bounds.size.x : bounds.size.z;
            if (length < 2f) continue;
            // Small restaurant signs on solid upper brick, clear of floor routes,
            // windows, corner lights and counter interactions.
            if (alongX && length >= 6f)
            {
                float signY = bounds.max.y - 0.5f;
                for (int sign = 0; sign < 2; sign++)
                {
                    float x = Mathf.Lerp(bounds.min.x, bounds.max.x, sign == 0 ? 0.36f : 0.64f);
                    if (!HasBrickBacking(bricks, true, x, signY, 0.75f, 0.25f)) continue;
                    var mount = new GameObject("Interior Restaurant Sign");
                    mount.transform.SetParent(fixtures.transform, false);
                    Vector3 position = new Vector3(x, signY - 0.22f, bounds.center.z);
                    position -= facing * (bounds.extents.z + 0.08f);
                    mount.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-facing));
                    RestaurantDetails.Add("Sign", mount.transform, Vector3.zero, new Vector3(1.4f, 0.44f, 0.12f));
                    decorations.Add((mount, wall));
                }
            }
            float inset = Mathf.Min(1.5f, length * 0.3f);
            int count = length >= 4f ? 2 : 1;
            // A doorway gets its own centered pair instead of uneven corner lights.
            CustomerWallDoor entry = CustomerWallDoor.FindEntryDoor();
            bool doorWall = entry != null && Vector3.Dot(entry.OutwardDirection(), facing) > 0.95f
                && Mathf.Abs(Vector3.Dot(entry.GetPassageCenter() - bounds.center, facing)) < 1.5f;
            if (doorWall)
            {
                Vector3 center = entry.GetPassageCenter();
                float doorAlong = alongX ? center.x : center.z;
                float min = alongX ? bounds.min.x : bounds.min.z;
                float max = alongX ? bounds.max.x : bounds.max.z;
                float spacing = 0f;
                float lampY = Mathf.Min(bounds.max.y-0.4f, bounds.min.y+2.7f);
                float available = Mathf.Min(doorAlong-min, max-doorAlong)-0.5f;
                // Both housings must fit on solid brick at the SAME distance
                // from the doorway, never on a window or in the opening.
                for (float candidate = 0.8f; candidate <= available; candidate += 0.05f)
                {
                    if (HasBrickBacking(bricks, alongX, doorAlong-candidate, lampY, 0.25f, 0.30f)
                        && HasBrickBacking(bricks, alongX, doorAlong+candidate, lampY, 0.25f, 0.30f))
                    { spacing = candidate; break; }
                }
                if (spacing > 0f)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 position = bounds.center;
                        if (alongX) position.x = doorAlong + side*spacing;
                        else position.z = doorAlong + side*spacing;
                        position += facing*((alongX ? bounds.extents.z : bounds.extents.x)+0.10f);
                        position.y = lampY;
                        AddLamp(position, facing, "Entrance Wall Lamp", 5f, wall);
                    }
                }
                else doorWall = false;
            }
            for (int i = 0; i < count; i++)
            {
                Vector3 position = bounds.center;
                float along = count == 1 ? 0f : (i == 0 ? -1f : 1f) * (length * 0.5f - inset);
                if (alongX) position.x += along; else position.z += along;
                position += facing * ((alongX ? bounds.extents.z : bounds.extents.x) + 0.10f);
                position.y = Mathf.Min(bounds.max.y - 0.4f, bounds.min.y + 2.7f);
                if (!doorWall) AddLamp(position, facing, "Exterior Corner Lamp", 5f, wall);
                // North/south wall ends cover the four indoor corners without
                // doubling up fixtures on the adjoining east/west walls.
                if (alongX)
                {
                    position -= facing*((alongX ? bounds.size.z : bounds.size.x)+0.24f);
                    position.y = bounds.max.y - 0.32f;
                    AddLamp(position, -facing, "Interior Frosted Sconce", 6f, wall, true);
                }
            }
        }
    }

    static bool HasBrickBacking(List<Bounds> bricks, bool alongX, float along, float y, float halfWidth, float halfHeight)
    {
        foreach (Bounds brick in bricks)
        {
            float min = alongX ? brick.min.x : brick.min.z;
            float max = alongX ? brick.max.x : brick.max.z;
            if (along-halfWidth >= min && along+halfWidth <= max
                && y-halfHeight >= brick.min.y && y+halfHeight <= brick.max.y)
                return true;
        }
        return false;
    }

    void AddRoomFill(Transform floor)
    {
        Renderer surface = floor.GetComponentInChildren<Renderer>();
        if (surface == null) return;
        Bounds bounds = surface.bounds;
        int columns = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / 5f));
        int rows = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / 5f));
        for (int x = 0; x < columns; x++)
        for (int z = 0; z < rows; z++)
        {
            var root = new GameObject("Soft Interior Lighting");
            root.transform.SetParent(fixtures.transform, false);
            root.transform.position = new Vector3(
                Mathf.Lerp(bounds.min.x, bounds.max.x, (x + 0.5f) / columns),
                bounds.max.y + 3.6f,
                Mathf.Lerp(bounds.min.z, bounds.max.z, (z + 0.5f) / rows));
            root.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var light = root.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(1f, 0.95f, 0.86f);
            light.spotAngle = 105f;
            light.innerSpotAngle = 75f;
            light.range = 6.5f;
            light.shadows = LightShadows.Soft;
            light.shadowResolution = LightShadowResolution.Low;
            light.enabled = false;
            // Ambient-looking room coverage persists when walls are cut away.
            lights.Add(new WallLamp { root = root, light = light, roomFill = true, intensity = 2.2f });
        }
    }

    void QueueRebuild() { rebuildFrame = Time.frameCount + 1; }

    void LateUpdate()
    {
        // Measure only after the procedural walls have finished rebuilding.
        if (rebuildFrame >= 0 && Time.frameCount >= rebuildFrame)
        {
            rebuildFrame = -1;
            Rebuild();
        }
    }

    void AddLamp(Vector3 position, Vector3 facing, string name, float range, CameraOcclusionWall wall, bool indoor = false)
    {
        // South-wall sconces face north into the restaurant.
        bool southInterior = indoor && wall.outwardNormal.z < -0.5f;
        var root = new GameObject(name);
        root.transform.SetParent(fixtures.transform,false);
        root.transform.SetPositionAndRotation(position,Quaternion.LookRotation(facing,Vector3.up));
        AddPart(root.transform,"Housing",Vector3.zero,indoor ? new Vector3(0.8f,0.28f,0.16f) : new Vector3(0.4f,0.55f,0.18f),indoor ? indoorTrim : housing);
        AddPart(root.transform,indoor ? "Frosted Shade" : "Warm Lens",new Vector3(0,0,0.11f),indoor ? new Vector3(0.70f,0.21f,0.16f) : new Vector3(0.30f,0.40f,0.08f),indoor ? indoorShade : lens);
        var bulb = new GameObject("Warm Building Light");
        bulb.transform.SetParent(root.transform,false);
        bulb.transform.localPosition = new Vector3(0,0,0.35f);
        var light = bulb.AddComponent<Light>();
        light.type = LightType.Spot;
        bulb.transform.localRotation = Quaternion.Euler(southInterior ? 30f : 45f, 0f, 0f);
        light.spotAngle = indoor ? 110f : 90f;
        light.innerSpotAngle = southInterior ? 65f : (indoor ? 35f : 55f);
        light.color = indoor ? new Color(1f,0.94f,0.82f) : new Color(1f,0.87f,0.68f);
        light.range = southInterior ? 9f : range;
        light.shadows = LightShadows.Soft;
        light.shadowResolution = LightShadowResolution.Low;
        light.enabled = false;
        root.SetActive(wall.DuckAmount < 0.1f);
        lights.Add(new WallLamp { light = light, root = root, wall = wall, intensity = southInterior ? 4.2f : (indoor ? 2.8f : 4.5f) });
    }

    static void AddPart(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent,false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        var collider = part.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
        var renderer = part.GetComponent<Renderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    void OnDestroy()
    {
        if (grid != null) grid.GridChanged -= QueueRebuild;
        if (housing != null) Destroy(housing);
        if (lens != null) Destroy(lens);
        if (indoorTrim != null) Destroy(indoorTrim);
        if (indoorShade != null) Destroy(indoorShade);
    }
}
