using UnityEngine;
using UnityEditor;

public static class CreateHeatLampStation
{
    [MenuItem("Production/Add Heat Lamp Station")]
    [MenuItem("GameObject/FactoryGame/Heat Lamp Station", false, 12)]
    public static void Create()
    {
        var existing = UnityEngine.Object.FindFirstObjectByType<HeatLampStation>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("HeatLampStation already exists. Select it to tune maxCapacity, targetStock, and expireAfterSeconds.");
            WireProductionManager(existing);
            return;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "HeatLamp";
        Undo.RegisterCreatedObjectUndo(go, "Create Heat Lamp");
        go.transform.position = Vector3.zero;
        go.transform.localScale = new Vector3(1.2f, 0.35f, 1.2f);

        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = new Color(1f, 0.45f, 0.15f, 1f);
            renderer.sharedMaterial = mat;
        }

        var lamp = go.AddComponent<HeatLampStation>();
        lamp.maxCapacity = 8;
        lamp.targetStock = 3;
        lamp.expireAfterSeconds = 28f;
        lamp.interactionOffset = new Vector3(0f, 0f, -0.8f);

        WireProductionManager(lamp);
        Selection.activeGameObject = go;
        Debug.Log("Heat Lamp added. Place it near the kitchen/pass. Workers deliver finished meals here; registers serve from it. Tune Target Stock vs Expire time to teach over/under-production.");
    }

    static void WireProductionManager(HeatLampStation lamp)
    {
        var pm = UnityEngine.Object.FindFirstObjectByType<ProductionManager>();
        if (pm == null) return;
        Undo.RecordObject(pm, "Assign Heat Lamp");
        pm.heatLamp = lamp;
        EditorUtility.SetDirty(pm);
    }
}
