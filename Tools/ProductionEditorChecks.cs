using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sim = KitchenProduction.ProductionSimulation;

// Run through `unity command run_script`. The fixture uses a temporary Play Mode
// start scene, preserving the user's open scene and never selecting a save slot.
public static class ProductionEditorChecks
{
    const string FixturePath = "Assets/Editor/TemporaryProductionScheduleCheck.unity";
    const string PreviousStartKey = "ProductionChecks.PreviousStart";

    public static string Prepare()
    {
        if (Application.isPlaying) throw new Exception("Stop Play Mode before preparing the isolated fixture.");
        Scene active = SceneManager.GetActiveScene();
        SceneAsset previous = EditorSceneManager.playModeStartScene;
        SessionState.SetString(PreviousStartKey, previous != null ? AssetDatabase.GetAssetPath(previous) : "");
        SessionState.SetBool("ProductionChecks.Background", Application.runInBackground);
        CustomerOrderConfig config = AssetDatabase.LoadAssetAtPath<CustomerOrderConfig>("Assets/Game/Customers/Data/CustomerOrderConfig.asset");
        if (config == null) throw new Exception("Production configuration missing.");
        Scene fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(fixture);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "ScheduleCheckFloor";
            floor.transform.localScale = new Vector3(2, 1, 2);
            var grid = new GameObject("ScheduleCheckGrid").AddComponent<GridManager>();
            grid.floor = floor.transform;
            var tutorial = new GameObject("DisabledTutorialForScheduleCheck").AddComponent<OnboardingTutorial>();
            tutorial.enabled = false;
            var inventory = new GameObject("ScheduleCheckInventory").AddComponent<KitchenInventory>();
            inventory.orderConfig = config;
            inventory.grantStartingStock = false;
            inventory.stock.Add(new IngredientStockEntry { item = config.rawPattyIngredient, quantity = 100 });
            AssemblyRecipeDefinition recipe = config.assemblyRecipes.Find(r => r.output == config.burgerBase);
            if (recipe?.pantryInput == null) throw new Exception("Hamburger input recipe missing.");
            inventory.stock.Add(new IngredientStockEntry { item = recipe.pantryInput, quantity = 100 });
            var manager = new GameObject("ScheduleCheckProduction").AddComponent<ProductionManager>();
            manager.orderConfig = config;
            manager.useScheduledProduction = true;
            var source = new GameObject("CheckFreezer").AddComponent<FreezerStation>();
            source.selectedItem = config.rawPattyIngredient;
            source.transform.position = new Vector3(-3.5f, 0, 0.5f);
            var grill = new GameObject("CheckGrill").AddComponent<GrillStation>();
            grill.selectedProduct = config.cookedPattyIngredient;
            grill.transform.position = new Vector3(-1.5f, 0, 0.5f);
            var assembly = new GameObject("CheckAssembly").AddComponent<AssemblyStation>();
            assembly.selectedProduct = config.burgerBase;
            assembly.transform.position = new Vector3(0.5f, 0, 0.5f);
            var pantry = new GameObject("CheckPantry").AddComponent<PantryStation>();
            pantry.selectedItem = recipe.pantryInput;
            pantry.transform.position = new Vector3(0.5f, 0, 2.5f);
            var pickup = new GameObject("CheckPickup").AddComponent<HeatLampStation>();
            pickup.transform.position = new Vector3(2.5f, 0, 0.5f);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Employees/Prefabs/KitchenWorker.prefab");
            KitchenEmployee worker = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, fixture)).GetComponent<KitchenEmployee>();
            worker.name = "ScheduleCheckWorker";
            worker.transform.position = source.transform.position;
            worker.moveSpeed = 2f;
            var flow = new ProductionFlowPlan { flowName = "Schedule Check", graphInitialized = true };
            flow.stations.AddRange(new[] { source.gameObject, grill.gameObject, assembly.gameObject, pantry.gameObject, pickup.gameObject });
            flow.AddConnection(source.gameObject, grill.gameObject);
            flow.AddConnection(grill.gameObject, assembly.gameObject);
            flow.AddConnection(pantry.gameObject, assembly.gameObject);
            flow.AddConnection(assembly.gameObject, pickup.gameObject);
            flow.workers.Add(worker);
            worker.operatedStations = new List<GameObject>(flow.stations);
            manager.productionFlows = new List<ProductionFlowPlan> { flow };
            manager.employees = new List<KitchenEmployee> { worker };
            var camera = new GameObject("ScheduleCheckCamera").AddComponent<Camera>();
            camera.gameObject.AddComponent<AudioListener>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 12, -10);
            camera.transform.LookAt(Vector3.zero);
            if (!AssetDatabase.IsValidFolder("Assets/Editor")) AssetDatabase.CreateFolder("Assets", "Editor");
            EditorSceneManager.SaveScene(fixture, FixturePath);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(FixturePath);
        }
        finally
        {
            EditorSceneManager.CloseScene(fixture, true);
            SceneManager.SetActiveScene(active);
        }
        return "Prepared isolated production fixture; user's open scene preserved.";
    }

    public static async Task<string> VerifyLive()
    {
        if (!Application.isPlaying || SceneManager.GetActiveScene().path != FixturePath)
            throw new Exception("Run only in the isolated production fixture.");
        Application.runInBackground = true;
        GameTimeManager clock = GameTimeManager.Instance;
        clock.ReleaseExternalPause(GameTimeManager.PauseTitleScreen);
        clock.ReleaseExternalPause(GameTimeManager.PauseManagement);
        clock.ReleaseExternalPause(GameTimeManager.PauseMenu);
        clock.ReleaseExternalPause(GameTimeManager.PauseOnboarding);
        clock.superFastTimeScale = 20f;
        clock.SetSpeed(GameTimeManager.SpeedMode.SuperFast);
        ProductionManager manager = ProductionManager.Instance;
        ResearchProgressManager.Instance.DebugCompleteAll();
        manager.ScheduledProduction.RequestConfigurationCheck();
        manager.ScheduledProduction.Tick();
        Sim simulation = manager.ScheduledProduction.Simulation;
        if (simulation == null || simulation.Plan.Workers.Length != 1) throw new Exception("Fixture worker is not scheduled.");
        Sim future = simulation.Copy();
        long end = simulation.Now + 60000;
        long baseline = simulation.Delivered[0];
        future.AdvanceTo(end);
        long predicted = future.Delivered[0] - baseline;
        int firstFrame = Time.frameCount;
        Vector3 start = manager.employees[0].transform.position;
        bool moved = false;
        DateTime timeout = DateTime.UtcNow.AddSeconds(25);
        while (simulation.Now < end)
        {
            if (DateTime.UtcNow > timeout) throw new Exception("Editor did not advance production through the test window.");
            await Task.Delay(50);
            moved |= Vector3.Distance(start, manager.employees[0].transform.position) > 0.1f;
            foreach (int count in simulation.Stock) if (count < 0) throw new Exception("Negative live stock.");
        }
        clock.SetSpeed(GameTimeManager.SpeedMode.Paused);
        manager.ScheduledProduction.Tick();
        long actual = simulation.Delivered[0] - baseline;
        if (predicted <= 0 || actual != predicted) throw new Exception("Live deliveries differ: predicted=" + predicted + ", actual=" + actual);
        if (!moved || Time.frameCount <= firstFrame) throw new Exception("Worker/frames did not move.");
        HeatLampStation pickup = UnityEngine.Object.FindFirstObjectByType<HeatLampStation>();
        if (pickup.Count != simulation.Delivered[0]) throw new Exception("Visible pickup buffer differs from simulation deliveries.");
        GrillStation grill = UnityEngine.Object.FindFirstObjectByType<GrillStation>();
        int cooked = grill.GetOutputCount(manager.orderConfig.cookedPattyIngredient);
        int simCooked = -1;
        string key = StationNode.EnsureOn(grill.gameObject).StableStationId;
        for (int i = 0; i < simulation.Plan.Nodes.Length; i++) if (simulation.Plan.Nodes[i].Key == key) simCooked = simulation.Buffers[i].Output;
        if (cooked != simCooked) throw new Exception("Grill cooking state differs from displayed buffer.");
        // Force a genuinely loaded trip so the checkpoint check cannot pass
        // trivially with an empty worker.
        pickup.TakeOutput(manager.orderConfig.burgerBase, 1);
        clock.SetSpeed(GameTimeManager.SpeedMode.SuperFast);
        timeout = DateTime.UtcNow.AddSeconds(10);
        while (!Array.Exists(simulation.Trips, t => t.CargoUnits > 0))
        {
            if (DateTime.UtcNow > timeout) throw new Exception("No physical cargo trip became available.");
            await Task.Delay(10);
        }
        clock.SetSpeed(GameTimeManager.SpeedMode.Paused);
        manager.ScheduledProduction.Tick();
        var stationSaves = new Dictionary<GameObject, ProductionStationCheckpoint>();
        foreach (GameObject station in manager.SelectedFlow.stations)
        {
            ProductionStationCheckpoint checkpoint = manager.ScheduledProduction.CaptureStation(station);
            if (checkpoint != null) stationSaves[station] = JsonUtility.FromJson<ProductionStationCheckpoint>(JsonUtility.ToJson(checkpoint));
        }
        KitchenEmployee worker = manager.employees[0];
        ProductionWorkerCheckpoint cargo = manager.ScheduledProduction.CaptureWorker(worker);
        int cargoBefore = cargo != null ? cargo.units : 0;
        if (cargoBefore == 0) throw new Exception("Checkpoint fixture did not capture carried items.");
        manager.ResetTransientProductionState();
        foreach (var entry in stationSaves)
        {
            HeatLampStation lamp = entry.Key.GetComponent<HeatLampStation>();
            if (lamp != null)
                foreach (ItemDefinition item in new[] { manager.orderConfig.burgerBase, manager.orderConfig.friesItem, manager.orderConfig.drinkItem })
                    if (item != null) lamp.TakeOutput(item, 100);
            manager.ScheduledProduction.RestoreStation(entry.Key, entry.Value);
        }
        if (cargo != null) manager.ScheduledProduction.RestoreWorker(worker,
            JsonUtility.FromJson<ProductionWorkerCheckpoint>(JsonUtility.ToJson(cargo)));
        manager.ScheduledProduction.Tick();
        ProductionWorkerCheckpoint restoredCargo = manager.ScheduledProduction.CaptureWorker(worker);
        if ((restoredCargo != null ? restoredCargo.units : 0) != cargoBefore) throw new Exception("Cargo lost during checkpoint restore.");
        foreach (var entry in stationSaves)
        {
            ProductionStationCheckpoint restored = manager.ScheduledProduction.CaptureStation(entry.Key);
            if (restored.output != entry.Value.output || restored.processing != entry.Value.processing)
                throw new Exception("Station processing/output lost during checkpoint restore.");
        }
        string cargoItem = restoredCargo.item;
        GameObject cargoSource = manager.SelectedFlow.stations.Find(s => StationNode.EnsureOn(s).StableStationId == restoredCargo.sourceGuid);
        if (cargoSource.GetComponent<FreezerStation>() != null)
            cargoSource.GetComponent<FreezerStation>().SetStoredItem(manager.orderConfig.eggIngredient);
        else if (cargoSource.GetComponent<PantryStation>() != null)
            cargoSource.GetComponent<PantryStation>().SetStoredItem(manager.orderConfig.onionIngredient);
        else if (cargoSource.GetComponent<GrillStation>() != null)
            cargoSource.GetComponent<GrillStation>().SetRecipeOutput(manager.orderConfig.cookedBaconIngredient);
        else if (cargoSource.GetComponent<AssemblyStation>() != null)
            cargoSource.GetComponent<AssemblyStation>().SetRecipe(manager.orderConfig.GetAssemblyRecipe(manager.orderConfig.cheeseburgerItem));
        manager.ScheduledProduction.RequestConfigurationCheck();
        manager.ScheduledProduction.Tick();
        ProductionWorkerCheckpoint afterEdit = manager.ScheduledProduction.CaptureWorker(worker);
        Sim.Trip editedTrip = manager.ScheduledProduction.Simulation.Trips[0];
        if (afterEdit.units != cargoBefore || afterEdit.item != cargoItem || editedTrip.Phase != Sim.TripPhase.Travel)
            throw new Exception("Source recipe edit stranded or changed connected physical cargo.");
        return "PASS: " + predicted + " forecast deliveries = " + actual + " visible live deliveries; frames advanced; worker traveled; grill projection matches; loaded-cargo checkpoint JSON round trip passed; source recipe edit preserves the connected cargo trip.";
    }

    public static string Cleanup()
    {
        if (Application.isPlaying) throw new Exception("Stop Play Mode before cleaning the fixture.");
        string previous = SessionState.GetString(PreviousStartKey, "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        Application.runInBackground = SessionState.GetBool("ProductionChecks.Background", false);
        AssetDatabase.DeleteAsset(FixturePath);
        SessionState.EraseString(PreviousStartKey);
        return "Restored previous Play Mode start scene and removed temporary fixture.";
    }
}
