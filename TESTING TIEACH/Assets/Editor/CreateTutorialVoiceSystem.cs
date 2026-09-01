using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class CreateTutorialVoiceSystem
{
    const string ResourcesDatabasePath = "Assets/Resources/TutorialVoiceDatabase.asset";

    [MenuItem("Tools/ISE Rush/Add Tutorial Voice System")]
    public static void AddToScene()
    {
        var database = EnsureDefaultDatabase();

        var existing = Object.FindFirstObjectByType<TutorialVoiceManager>();
        if (existing != null)
        {
            if (database != null && existing.database == null)
            {
                existing.database = database;
                EditorUtility.SetDirty(existing);
            }

            var ui = existing.GetComponentInChildren<TutorialVoiceUI>(true);
            if (ui != null)
                Selection.activeGameObject = ui.gameObject;
            else
                Selection.activeGameObject = existing.gameObject;

            EditorGUIUtility.PingObject(Selection.activeGameObject);
            Debug.Log("Tutorial voice system already in scene. Select TutorialVoiceUI child to edit subtitles.");
            return;
        }

        var go = new GameObject("TutorialVoiceSystem");
        Undo.RegisterCreatedObjectUndo(go, "Create Tutorial Voice System");

        go.AddComponent<AudioSource>();
        var manager = go.AddComponent<TutorialVoiceManager>();
        if (database != null)
            manager.database = database;

        var voiceUi = go.GetComponentInChildren<TutorialVoiceUI>(true);
        Selection.activeGameObject = voiceUi != null ? voiceUi.gameObject : go;
        Debug.Log("Added TutorialVoiceSystem. Select the TutorialVoiceUI child to edit subtitle look/position.");
    }

    [MenuItem("Tools/ISE Rush/Create Tutorial Voice Database")]
    public static TutorialVoiceDatabase EnsureDefaultDatabase()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TutorialVoiceDatabase>(ResourcesDatabasePath);
        if (existing != null && existing.lines != null && existing.lines.Count > 0)
        {
            existing.RebuildLookup();
            return existing;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        var database = existing ?? ScriptableObject.CreateInstance<TutorialVoiceDatabase>();
        database.lines = BuildDefaultLines();

        if (existing == null)
            AssetDatabase.CreateAsset(database, ResourcesDatabasePath);
        else
            EditorUtility.SetDirty(database);

        foreach (var line in database.lines)
        {
            if (line == null) continue;
            if (!AssetDatabase.Contains(line))
                AssetDatabase.AddObjectToAsset(line, database);
        }

        database.RebuildLookup();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return database;
    }

    static List<TutorialVoiceLine> BuildDefaultLines()
    {
        return new List<TutorialVoiceLine>
        {
            CreateLine(TutorialVoiceEventId.ShiftStarted,
                "Welcome to your first shift. Customers will line up at the register — your job is to get orders out without making them wait too long."),
            CreateLine(TutorialVoiceEventId.FirstCustomerArrived,
                "Here comes your first customer. Watch the queue. When people wait, that backlog is called work in progress."),
            CreateLine(TutorialVoiceEventId.QueueGrowing,
                "The line is getting long. That usually means something in your system is slowing down — maybe the counter or the kitchen."),
            CreateLine(TutorialVoiceEventId.FirstWorkerHired,
                "Good hire. Open Management, click a station, and assign your worker so food can start moving."),
            CreateLine(TutorialVoiceEventId.OutputAssigned,
                "Nice. After a station finishes, the worker delivers only to the output you set."),
            CreateLine(TutorialVoiceEventId.FirstOrderServed,
                "First order served. Keep the flow steady — faster service means happier customers and more sales.")
        };
    }

    static TutorialVoiceLine CreateLine(string eventId, string subtitle)
    {
        var line = ScriptableObject.CreateInstance<TutorialVoiceLine>();
        line.name = eventId;
        line.eventId = eventId;
        line.subtitleText = subtitle;
        line.playOnce = true;
        line.displayDuration = 5f;
        return line;
    }
}
