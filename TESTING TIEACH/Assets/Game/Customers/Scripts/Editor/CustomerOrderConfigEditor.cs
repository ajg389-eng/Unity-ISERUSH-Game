#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CustomerOrderConfig))]
public sealed class CustomerOrderConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (GUILayout.Button("Validate Production Recipe Graph"))
            Validate((CustomerOrderConfig)target);
    }

    [MenuItem("ISE/Recipes/Validate Production Recipe Graph")]
    static void ValidateSelectedOrAll()
    {
        CustomerOrderConfig selected = Selection.activeObject as CustomerOrderConfig;
        if (selected != null)
        {
            Validate(selected);
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:CustomerOrderConfig");
        if (guids.Length == 0)
        {
            Debug.LogWarning("No CustomerOrderConfig assets were found.");
            return;
        }
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Validate(AssetDatabase.LoadAssetAtPath<CustomerOrderConfig>(path));
        }
    }

    static void Validate(CustomerOrderConfig config)
    {
        if (config == null) return;
        config.EnsureProductionRecipeGraph();
        var issues = config.ValidateProductionRecipeGraph();
        if (issues.Count == 0)
        {
            Debug.Log("Production recipe graph is valid.", config);
            return;
        }

        foreach (string issue in issues)
            Debug.LogWarning("Production recipe graph: " + issue, config);
    }
}
#endif
