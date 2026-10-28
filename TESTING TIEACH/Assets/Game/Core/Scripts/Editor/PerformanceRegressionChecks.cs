using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PerformanceRegressionChecks
{
    const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        Material original = null;
        CameraOcclusionWall wall = null;
        try
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Require(shader != null, "URP Lit shader is available");
            original = new Material(shader);
            root = new GameObject("Fade Regression Wall");
            SceneManager.MoveGameObjectToScene(root, scene);
            var visible = MakeRenderer(root, original, "Visible");
            var authoredHidden = MakeRenderer(root, original, "Authored Hidden");
            authoredHidden.forceRenderingOff = true;
            var authoredDisabled = MakeRenderer(root, original, "Authored Disabled");
            authoredDisabled.enabled = false;
            wall = root.AddComponent<CameraOcclusionWall>();
            Invoke(wall, "CacheRenderers");
            Invoke(wall, "Start");

            var variants = (IList)typeof(CameraOcclusionWall)
                .GetField("opacityMaterialInstances", PrivateInstance).GetValue(wall);
            Require(variants.Count == 1, "Repeated source materials share one prepared fade variant");

            Invoke(wall, "ApplyCutaway", 0.5f);
            Require(visible.enabled && !visible.forceRenderingOff, "Partly faded wall remains renderable");
            Require(visible.sharedMaterial != original, "Partly faded wall uses fade variant");

            Invoke(wall, "ApplyCutaway", 1f);
            Require(visible.forceRenderingOff, "Fully faded wall omits rendering");
            Require(visible.enabled, "Full fade preserves enabled state");

            Invoke(wall, "ApplyCutaway", 0.5f);
            Require(!visible.forceRenderingOff, "Wall resumes rendering on fade-in");
            Require(authoredHidden.forceRenderingOff, "Fade-in preserves authored forceRenderingOff");
            Require(!authoredDisabled.enabled, "Fade-in preserves authored disabled state");

            Invoke(wall, "ApplyCutaway", 0f);
            Require(visible.sharedMaterial == original, "Full opacity restores original material");
            Require(!visible.forceRenderingOff && authoredHidden.forceRenderingOff,
                "Full opacity restores authored rendering flags");

            Invoke(wall, "ApplyCutaway", 1f);
            Invoke(wall, "OnDisable");
            Require(!visible.forceRenderingOff && visible.sharedMaterial == original,
                "Disabling a hidden wall restores original rendering");

            Invoke(wall, "ApplyCutaway", 1f);
            MakeRenderer(root, original, "Added Geometry");
            Invoke(wall, "OnTransformChildrenChanged");
            Invoke(wall, "ApplyCutaway", 0.5f);
            Require(!visible.forceRenderingOff && authoredHidden.forceRenderingOff,
                "Hierarchy recache preserves authored visibility after full fade");

            CheckHudOrdering(scene);
            CheckInventoryReuse(scene);
            CheckCounterSlots(scene);
            Debug.Log("PERFORMANCE_REGRESSION_CHECKS_PASSED: wall fade restoration, stable HUD ordering and raycasters, inventory reuse/invalidation, live counter slot queries.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
        finally
        {
            if (wall != null)
            {
                Invoke(wall, "RestoreOriginalRendering");
                Invoke(wall, "DestroyOpacityMaterials");
            }
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (original != null) UnityEngine.Object.DestroyImmediate(original);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static void CheckHudOrdering(Scene scene)
    {
        var root = new GameObject("Regression HUD", typeof(RectTransform), typeof(Canvas));
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var tabRoot = new GameObject("HUD Tabs", typeof(RectTransform), typeof(MainHudTabs));
            tabRoot.transform.SetParent(root.transform, false);
            MainHudTabs tabs = tabRoot.GetComponent<MainHudTabs>();
            tabs.Build(true);
            var actionRoot = new GameObject("Build Actions", typeof(RectTransform), typeof(Image),
                typeof(CanvasGroup), typeof(HorizontalLayoutGroup), typeof(BuildStationActionBar));
            actionRoot.transform.SetParent(root.transform, false);
            var actions = actionRoot.GetComponent<BuildStationActionBar>();
            Invoke(actions, "EnsureBuilt");
            var menu = new GameObject("Menu", typeof(RectTransform));
            menu.transform.SetParent(root.transform, false);
            int tabIndex = tabRoot.transform.GetSiblingIndex();
            int actionIndex = actionRoot.transform.GetSiblingIndex();
            for (int frame = 0; frame < 10; frame++)
            {
                Invoke(actions, "Update");
                Invoke(tabs, "LateUpdate");
            }
            Require(tabRoot.transform.GetSiblingIndex() == tabIndex
                && actionRoot.transform.GetSiblingIndex() == actionIndex,
                "HUD updates must not reorder the shared menu hierarchy");
            Require(tabRoot.GetComponent<Canvas>().overrideSorting
                && tabRoot.GetComponent<Canvas>().sortingOrder == 110
                && actionRoot.GetComponent<Canvas>().sortingOrder == 120,
                "Independent HUD canvases preserve their visual order");
            Require(tabRoot.GetComponent<GraphicRaycaster>() != null
                && actionRoot.GetComponent<GraphicRaycaster>() != null,
                "Independent HUD canvases retain pointer input");
            Require(!actionRoot.GetComponent<CanvasGroup>().blocksRaycasts,
                "Hidden build actions do not intercept menu clicks");
            var workerRoot = new GameObject("Worker Card", typeof(RectTransform), typeof(Image),
                typeof(Outline), typeof(WorkerCardUI));
            workerRoot.transform.SetParent(root.transform, false);
            var worker = workerRoot.GetComponent<WorkerCardUI>();
            Invoke(worker, "RefreshSelectionHighlight");
            int meshDirtyCount = 0;
            workerRoot.GetComponent<Image>().RegisterDirtyVerticesCallback(() => meshDirtyCount++);
            for (int frame = 0; frame < 10; frame++)
                Invoke(worker, "RefreshSelectionHighlight");
            Require(meshDirtyCount == 0,
                "Unchanged worker selection must not dirty menu mesh geometry");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void CheckInventoryReuse(Scene scene)
    {
        var root = new GameObject("Regression Inventory", typeof(RectTransform), typeof(Canvas),
            typeof(InventoryManager), typeof(InventoryUI));
        SceneManager.MoveGameObjectToScene(root, scene);
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        var texture = new Texture2D(2, 2);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        try
        {
            item.itemName = "Regression Item";
            item.previewIcon = sprite;
            var inventory = root.GetComponent<InventoryManager>();
            inventory.allItems.Clear();
            inventory.allItems.Add(item);
            inventory.RestoreCounts(item, 1, 1);
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            var ui = root.GetComponent<InventoryUI>();
            ui.inventory = inventory;
            ui.contentParent = content.transform;
            ui.RefreshAll();
            Require(content.transform.childCount == 1, "Initial inventory creates one card per item");
            GameObject card = content.transform.GetChild(0).gameObject;
            ui.RefreshAll();
            Require(content.transform.childCount == 1 && content.transform.GetChild(0).gameObject == card,
                "Unchanged inventory reuses existing cards");
            inventory.RestoreCounts(item, 2, 2);
            ui.RefreshAll();
            Require(card == null && content.transform.childCount == 1,
                "Changed inventory invalidates the card snapshot");
            var displayed = content.transform.GetChild(0).GetComponent<InventoryItemCardUI>();
            Require(displayed.qtyText.text == "2", "Updated inventory shows the acquired quantity");
            item.itemName = "Renamed Item";
            ui.RefreshAll();
            Require(content.transform.GetChild(0).GetComponent<InventoryItemCardUI>().nameText.text == item.itemName,
                "Catalog changes invalidate the card snapshot");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(item);
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    static void CheckCounterSlots(Scene scene)
    {
        var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(root, scene);
        var heldItem = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            var surface = root.AddComponent<CounterSurface>();
            surface.slotCount = 5;
            root.transform.localScale = new Vector3(2f, 1f, 10f);
            var occupant = new GameObject("Occupied Slots", typeof(CounterMountedItem));
            occupant.transform.SetParent(root.transform, false);
            var mounted = occupant.GetComponent<CounterMountedItem>();
            heldItem.counterSlotSpan = 2;
            mounted.itemDefinition = heldItem;
            Require(surface.Attach(mounted, 2), "Test counter reserves a two-slot item");
            for (int shape = 0; shape < 3; shape++)
            {
                root.transform.SetPositionAndRotation(new Vector3(shape * 4f, shape, -shape * 3f),
                    Quaternion.Euler(0f, shape * 45f, 0f));
                root.transform.localScale = new Vector3(2f + shape, 1f, 10f - shape);
                if (shape == 2) MakeRenderer(root, null, "New Counter Body");
                for (int span = 1; span <= surface.slotCount; span++)
                for (int probe = 0; probe < 8; probe++)
                for (int occupied = 0; occupied < 2; occupied++)
                {
                    Vector3 point = root.transform.position + new Vector3(probe - 3f, 2f, 3f - probe * 1.7f);
                    int expected = -1;
                    float nearest = float.MaxValue;
                    for (int slot = 0; slot <= surface.slotCount - span; slot++)
                    {
                        if (occupied == 1 && !surface.IsSlotRangeAvailable(slot, span)) continue;
                        float distance = (surface.GetSlotWorldCenter(slot, span) - point).sqrMagnitude;
                        if (distance >= nearest) continue;
                        expected = slot;
                        nearest = distance;
                    }
                    Require(surface.GetNearestSlot(point, span, occupied == 1) == expected,
                        "Optimized counter query matches nearest valid slot after transform or geometry changes");
                }
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(heldItem);
        }
    }

    static Renderer MakeRenderer(GameObject root, Material original, string name)
    {
        var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
        child.name = name;
        child.transform.SetParent(root.transform, false);
        var renderer = child.GetComponent<Renderer>();
        renderer.sharedMaterial = original;
        return renderer;
    }

    static void Invoke(object target, string name, params object[] arguments)
    {
        target.GetType().GetMethod(name, PrivateInstance).Invoke(target, arguments);
    }

    static void Require(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }
}
