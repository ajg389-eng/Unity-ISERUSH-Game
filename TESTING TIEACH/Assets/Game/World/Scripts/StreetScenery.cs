using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Decorative SimplePoly props placed outside traffic and customer routes.</summary>
public sealed class StreetScenery : MonoBehaviour
{
}

/// <summary>
/// Decorative pedestrians who walk along the public sidewalk without joining
/// restaurant queues or contributing to customer demand.
/// </summary>
[DisallowMultipleComponent]
public sealed class AmbientPedestrianSystem : MonoBehaviour
{
    const int Population = 6;
    const float EndInset = 4f;

    readonly List<AmbientPedestrian> pedestrians = new List<AmbientPedestrian>();

    IEnumerator Start()
    {
        // Let road extensions and scene-authored sidewalk renderers settle first.
        yield return null;

        GameObject prefab = Resources.Load<GameObject>("Prefabs/character_default");
        if (prefab == null)
        {
            Debug.LogWarning("Ambient pedestrians: NPC prefab was not found.", this);
            yield break;
        }

        if (!RoadTrafficController.TryGetHighway(
            out float roadWest, out float roadEast, out float roadZ, out float roadY))
            yield break;

        ResolveWalkway(roadWest, roadEast, roadZ, roadY,
            out float west, out float east, out float sidewalkZ, out float groundY, out float laneHalfWidth);
        if (east - west < EndInset * 2f + 2f) yield break;

        west += EndInset;
        east -= EndInset;
        for (int i = 0; i < Population; i++)
        {
            float t = (i + 0.5f) / Population;
            float x = Mathf.Lerp(west, east, t);
            float z = sidewalkZ + Random.Range(-laneHalfWidth, laneHalfWidth);
            GameObject npc = Instantiate(prefab, new Vector3(x, groundY, z), Quaternion.identity, transform);
            npc.name = "Ambient Pedestrian " + (i + 1);

            AlignFeetToGround(npc, groundY);
            DisableColliders(npc);

            PartyCharacterRandomizer look = PartyCharacterRandomizer.EnsureOn(npc);
            if (look != null) look.hatChance = 0.35f;

            AmbientPedestrian walker = npc.AddComponent<AmbientPedestrian>();
            walker.Configure(west, east, i % 2 == 0 ? 1f : -1f, Random.Range(1.05f, 1.45f));
            pedestrians.Add(walker);
        }
    }

    static void ResolveWalkway(float roadWest, float roadEast, float roadZ, float roadY,
        out float west, out float east, out float z, out float y, out float halfWidth)
    {
        west = roadWest;
        east = roadEast;
        z = roadZ - 5.5f;
        y = roadY + 0.12f;
        halfWidth = 0.55f;

        float buildingZ = roadZ - 1f;
        GridManager grid = GridManager.Instance;
        Renderer workFloor = grid != null && grid.floor != null
            ? grid.floor.GetComponentInChildren<Renderer>()
            : null;
        if (workFloor != null) buildingZ = workFloor.bounds.center.z;
        float buildingSide = Mathf.Sign(buildingZ - roadZ);
        if (Mathf.Abs(buildingSide) < 0.5f) buildingSide = -1f;
        z = roadZ + buildingSide * 5.5f;

        Renderer best = null;
        float bestScore = float.PositiveInfinity;
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer candidate = renderers[i];
            if (candidate == null || candidate.name.IndexOf("Sidewalk", System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            Bounds bounds = candidate.bounds;
            float side = Mathf.Sign(bounds.center.z - roadZ);
            if (side != buildingSide || bounds.size.x < bounds.size.z) continue;
            float distance = Mathf.Abs(bounds.center.z - roadZ);
            if (distance < 2f || distance > 12f) continue;
            float score = distance - Mathf.Min(bounds.size.x, 50f) * 0.02f;
            if (score >= bestScore) continue;
            bestScore = score;
            best = candidate;
        }

        if (best == null) return;
        Bounds walkway = best.bounds;
        west = Mathf.Max(roadWest, walkway.min.x);
        east = Mathf.Min(roadEast, walkway.max.x);
        z = walkway.center.z;
        y = walkway.max.y + 0.02f;
        halfWidth = Mathf.Clamp(walkway.extents.z * 0.45f, 0.25f, 0.8f);
    }

    static void AlignFeetToGround(GameObject npc, float groundY)
    {
        Renderer[] renderers = npc.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            if (renderers[i] != null) bounds.Encapsulate(renderers[i].bounds);
        npc.transform.position += Vector3.up * (groundY - bounds.min.y);
    }

    static void DisableColliders(GameObject npc)
    {
        Collider[] colliders = npc.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = false;
    }
}

/// <summary>Simple endpoint-to-endpoint sidewalk motion for an ambient NPC.</summary>
public sealed class AmbientPedestrian : MonoBehaviour
{
    float west;
    float east;
    float direction;
    float speed;
    Animator animator;

    public void Configure(float westEnd, float eastEnd, float initialDirection, float walkSpeed)
    {
        west = westEnd;
        east = eastEnd;
        direction = Mathf.Sign(initialDirection);
        if (Mathf.Abs(direction) < 0.5f) direction = 1f;
        speed = walkSpeed;
        animator = GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.speed = 0.78f;
            animator.SetBool("idle", false);
            animator.SetBool("run", true);
        }
        FaceDirection();
    }

    void Update()
    {
        Vector3 position = transform.position;
        position.x += direction * speed * Time.deltaTime;
        if (position.x >= east)
        {
            position.x = east;
            direction = -1f;
            FaceDirection();
        }
        else if (position.x <= west)
        {
            position.x = west;
            direction = 1f;
            FaceDirection();
        }
        transform.position = position;
    }

    void FaceDirection()
    {
        transform.rotation = Quaternion.LookRotation(new Vector3(direction, 0f, 0f), Vector3.up);
    }
}
