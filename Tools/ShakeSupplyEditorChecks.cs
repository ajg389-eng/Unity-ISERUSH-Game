using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Sim = KitchenProduction.ProductionSimulation;

// Copy into Assets/Editor temporarily and run ShakeSupplyEditorChecks.Run in
// batch mode. Uses real station prefabs and the reported shelf/worker positions;
// never selects a save slot or writes player preferences.
public static class ShakeSupplyEditorChecks
{
    static void SetProperty(Type type, object target, string name, object value)
    {
        type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance).SetValue(target, value);
    }

    static T Station<T>(string prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        where T : Component
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Game/Stations/Prefabs/" + prefab + ".prefab");
        if (asset == null) throw new Exception("Missing fixture prefab: " + prefab);
        GameObject station = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        station.transform.SetPositionAndRotation(position, rotation);
        station.transform.localScale = scale;
        return station.GetComponent<T>();
    }

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        DeterministicProductionController controller = null;
        try
        {
            var grid = new GameObject("FixtureGrid").AddComponent<GridManager>();
            SetProperty(typeof(GridManager), null, "Instance", grid);
            SetProperty(typeof(GridManager), grid, "Origin", new Vector3(-8, 0, -1));
            SetProperty(typeof(GridManager), grid, "Width", 10);
            SetProperty(typeof(GridManager), grid, "Height", 10);
            var nodes = new Node[10, 10];
            for (int x = 0; x < 10; x++)
                for (int y = 0; y < 10; y++)
                    nodes[x, y] = new Node(x, y, grid.CellToWorld(x, y));
            SetProperty(typeof(GridManager), grid, "Nodes", nodes);

            CustomerOrderConfig config = AssetDatabase.LoadAssetAtPath<CustomerOrderConfig>(
                "Assets/Game/Customers/Data/CustomerOrderConfig.asset");
            AssemblyRecipeDefinition recipe = config.assemblyRecipes.Find(r => r.output == config.drinkItem);
            if (recipe == null || recipe.pantryInput == null || recipe.processedInput == null)
                throw new Exception("Shake recipe missing its milk or cup.");
            var manager = new GameObject("FixtureProduction").AddComponent<ProductionManager>();
            manager.orderConfig = config;
            manager.useScheduledProduction = true;
            // Selection is already unlocked in the reported save. This fixture
            // isolates routing from campaign progression and uses the same recipe.
            SetProperty(typeof(ProductionManager), null, "Instance", null);

            Vector3 shelfScale = new Vector3(1, 2, 1);
            PantryStation pantry = Station<PantryStation>("Pantry", new Vector3(-7.5f, 1, -0.5f),
                Quaternion.Euler(0, 180, 0), shelfScale);
            pantry.selectedItem = recipe.pantryInput;
            // The adjacent shelf is present in the reported layout too.
            Station<PantryStation>("Pantry", new Vector3(-7.5f, 1, 1.5f),
                Quaternion.Euler(0, 180, 0), shelfScale);
            FreezerStation freezer = Station<FreezerStation>("Freezer", new Vector3(-2.5f, 1, -0.5f),
                Quaternion.identity, shelfScale);
            freezer.selectedItem = recipe.processedInput;
            ShakeStation shake = Station<ShakeStation>("ShakeStationMk1", new Vector3(-4.5f, 0.5166533f, 0.5f),
                Quaternion.identity, Vector3.one);
            typeof(AssemblyStation).GetField("selectedRecipe", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(shake, recipe);
            shake.selectedProduct = recipe.output;
            var pickup = new GameObject("FixturePickup").AddComponent<HeatLampStation>();
            pickup.transform.position = new Vector3(1.5f, 0, 4.5f);
            var worker = new GameObject("FixtureWorker").AddComponent<KitchenEmployee>();
            worker.transform.position = new Vector3(-4.5f, 1, 1.5f);
            worker.moveSpeed = 2f;
            var flow = new ProductionFlowPlan { flowName = "Shake", graphInitialized = true };
            flow.stations.AddRange(new[] { pantry.gameObject, shake.gameObject, freezer.gameObject, pickup.gameObject });
            flow.AddConnection(pantry.gameObject, shake.gameObject);
            flow.AddConnection(freezer.gameObject, shake.gameObject);
            flow.AddConnection(shake.gameObject, pickup.gameObject);
            flow.workers.Add(worker);
            worker.operatedStations = new List<GameObject>(flow.stations);
            manager.productionFlows = new List<ProductionFlowPlan> { flow };
            manager.employees = new List<KitchenEmployee> { worker };
            grid.ResyncOccupancyFromScene();
            Vector3 authored = KitchenEmployee.GetInteractionPosition(pantry.gameObject);
            Debug.Log("Shake regression Pantry interaction=" + authored + ", grid origin=" + grid.Origin);

            controller = new DeterministicProductionController(manager);
            typeof(DeterministicProductionController).GetMethod("Rebuild", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(controller, null);
            Sim simulation = controller.Simulation;
            var definitions = (List<ItemDefinition>)typeof(DeterministicProductionController)
                .GetField("items", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller);
            int cup = definitions.IndexOf(recipe.pantryInput);
            int milk = definitions.IndexOf(recipe.processedInput);
            if (simulation.Plan.Workers.Length != 1)
                throw new Exception("The fixture must schedule exactly one worker.");
            foreach (Sim.Edge edge in simulation.Plan.Edges)
                Debug.Log("Shake regression route " + edge.From + "->" + edge.To + " " + definitions[edge.Item].name
                    + " approach=" + simulation.Plan.Workers[0].TravelTicks[simulation.Trips[0].Location, edge.From]
                    + " travel=" + simulation.Plan.Workers[0].TravelTicks[edge.From, edge.To]);
            if (cup < 0 || milk < 0) throw new Exception("Cup or milk missing from scheduled items.");
            foreach (Sim.Edge edge in simulation.Plan.Edges)
                if (edge.Item == cup && simulation.Plan.Workers[0].TravelTicks[simulation.Trips[0].Location, edge.From] < 0)
                    throw new Exception("Worker cannot approach the connected cup shelf.");
            simulation.Stock[cup] = 340;
            simulation.Stock[milk] = 310;
            // The reported save has thirty milk and zero cups at the Shake Station.
            foreach (Sim.Edge edge in simulation.Plan.Edges)
                if (edge.Item == cup) simulation.Buffers[edge.To].Inputs[milk] = 30;
            simulation.DrainPickup = true;
            simulation.AdvanceTo(120 * Sim.TicksPerSecond);
            if (simulation.Stock[cup] >= 340 || simulation.Delivered[0] <= 0)
                throw new Exception("Connected cup Pantry did not supply Shake: cups="
                    + simulation.Stock[cup] + ", shakes delivered=" + simulation.Delivered[0]);
            Debug.Log("PASS: saved-layout cup shelf supplied " + (340 - simulation.Stock[cup])
                + " cups and delivered " + simulation.Delivered[0] + " shakes with one worker.");

            flow.connections.RemoveAll(edge => edge.from == pantry.gameObject && edge.to == shake.gameObject);
            typeof(DeterministicProductionController).GetMethod("Rebuild", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(controller, null);
            definitions = (List<ItemDefinition>)typeof(DeterministicProductionController)
                .GetField("items", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller);
            cup = definitions.IndexOf(recipe.pantryInput);
            if (Array.Exists(controller.Simulation.Plan.Edges, edge => edge.Item == cup))
                throw new Exception("Removing the cup connection left an invisible supply route.");
            Debug.Log("PASS: removing the cup connection removes the scheduled supply route.");
        }
        finally
        {
            controller?.Dispose();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetProperty(typeof(GridManager), null, "Instance", null);
        }
    }
}
