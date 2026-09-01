using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class CreateMissionSystem
{
    const string DatabasePath = "Assets/Resources/MissionDatabase.asset";

    [MenuItem("Tools/ISE Rush/Create Mission Database")]
    public static MissionDatabase EnsureDatabase()
    {
        var existing = AssetDatabase.LoadAssetAtPath<MissionDatabase>(DatabasePath);
        if (existing != null && existing.missions != null && existing.missions.Count > 0)
        {
            Selection.activeObject = existing;
            return existing;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        var database = existing ?? ScriptableObject.CreateInstance<MissionDatabase>();
        database.missions = new List<MissionDefinition>
        {
            CreateMission("start_shift", "Start your shift",
                "Click Play and get ready to run the restaurant.", TutorialVoiceEventId.ShiftStarted),
            CreateMission("watch_queue", "Watch the queue",
                "A customer is lining up at the register.", TutorialVoiceEventId.FirstCustomerArrived),
            CreateMission("hire_worker", "Hire a worker",
                "Open Management and hire someone from the Workers tab.", TutorialVoiceEventId.FirstWorkerHired),
            CreateMission("assign_output", "Connect your kitchen",
                "Assign a worker to a station, then set its Output.", TutorialVoiceEventId.OutputAssigned),
            CreateMission("serve_order", "Serve an order",
                "Get food to the customer at the register.", TutorialVoiceEventId.FirstOrderServed)
        };

        if (existing == null)
            AssetDatabase.CreateAsset(database, DatabasePath);
        else
            EditorUtility.SetDirty(database);

        foreach (var mission in database.missions)
        {
            if (mission == null) continue;
            if (!AssetDatabase.Contains(mission))
                AssetDatabase.AddObjectToAsset(mission, database);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = database;
        Debug.Log("Mission database ready at " + DatabasePath);
        return database;
    }

    [MenuItem("Tools/ISE Rush/Add Mission System To Scene")]
    public static void AddToScene()
    {
        EnsureDatabase();

        MissionProgressManager progress = Object.FindFirstObjectByType<MissionProgressManager>();
        MissionListUI listUi = Object.FindFirstObjectByType<MissionListUI>();

        if (progress != null && listUi != null)
        {
            Selection.activeGameObject = listUi.gameObject;
            EditorGUIUtility.PingObject(listUi.gameObject);
            Debug.Log("Mission system already in scene.");
            return;
        }

        var go = new GameObject("MissionSystem");
        Undo.RegisterCreatedObjectUndo(go, "Add Mission System");

        if (progress == null)
            progress = go.AddComponent<MissionProgressManager>();
        if (listUi == null)
            listUi = go.AddComponent<MissionListUI>();

        var db = AssetDatabase.LoadAssetAtPath<MissionDatabase>(DatabasePath);
        if (db != null)
            progress.database = db;

        Selection.activeGameObject = go;
    }

    static MissionDefinition CreateMission(string id, string title, string description, string completionEvent)
    {
        var mission = ScriptableObject.CreateInstance<MissionDefinition>();
        mission.name = id;
        mission.missionId = id;
        mission.title = title;
        mission.description = description;
        mission.completionEventId = completionEvent;
        mission.showBeforeComplete = true;
        mission.showAfterComplete = true;
        return mission;
    }
}
