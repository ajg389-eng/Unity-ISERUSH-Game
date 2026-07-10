using UnityEngine;
using UnityEditor;

public static class CreateFryerAndDrinkStations
{
    [MenuItem("Production/Add Fryer Station")]
    [MenuItem("GameObject/FactoryGame/Fryer Station", false, 13)]
    public static void CreateFryer()
    {
        var existing = UnityEngine.Object.FindFirstObjectByType<FryerStation>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("FryerStation already exists.");
            return;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Fryer";
        Undo.RegisterCreatedObjectUndo(go, "Create Fryer");
        go.transform.position = Vector3.zero;
        go.transform.localScale = new Vector3(1.1f, 0.9f, 0.9f);

        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = new Color(0.75f, 0.55f, 0.2f, 1f);
            renderer.sharedMaterial = mat;
        }

        var fryer = go.AddComponent<FryerStation>();
        fryer.cookTimeSeconds = 5f;
        fryer.interactionOffset = new Vector3(0f, 0f, -0.8f);
        StationNode.EnsureOn(go);

        Selection.activeGameObject = go;
        Debug.Log("Fryer added. Assign a worker in Manage mode. Fries jobs: Fryer → Heat Lamp.");
    }

    [MenuItem("Production/Add Drink Station")]
    [MenuItem("GameObject/FactoryGame/Drink Station", false, 14)]
    public static void CreateDrink()
    {
        var existing = UnityEngine.Object.FindFirstObjectByType<DrinkStation>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("DrinkStation already exists.");
            return;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "DrinkStation";
        Undo.RegisterCreatedObjectUndo(go, "Create Drink Station");
        go.transform.position = Vector3.zero;
        go.transform.localScale = new Vector3(0.7f, 0.55f, 0.7f);

        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = new Color(0.25f, 0.55f, 0.85f, 1f);
            renderer.sharedMaterial = mat;
        }

        var drink = go.AddComponent<DrinkStation>();
        drink.interactionTimeSeconds = 1.5f;
        drink.interactionOffset = new Vector3(0f, 0f, -0.7f);
        StationNode.EnsureOn(go);

        Selection.activeGameObject = go;
        Debug.Log("Drink Station added. Assign a worker in Manage mode. Drink jobs: Drink Station → Heat Lamp.");
    }
}
