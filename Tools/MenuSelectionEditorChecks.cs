using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// Temporarily copy into an Editor folder and run with
// -executeMethod MenuSelectionEditorChecks.Verify. Uses only transient objects;
// it never selects a save slot, writes PlayerPrefs, or modifies an asset.
public static class MenuSelectionEditorChecks
{
    const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.NonPublic;

    public static void Verify()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Run the menu selection check in Edit Mode.");

        FieldInfo researchInstanceField = typeof(ResearchProgressManager)
            .GetField("<Instance>k__BackingField", StaticFields);
        if (researchInstanceField == null)
            throw new InvalidOperationException("Research singleton backing field was not found.");

        ResearchProgressManager previousResearch = ResearchProgressManager.Instance;
        var transientAssets = new List<UnityEngine.Object>();
        GameObject root = null;
        try
        {
            root = new GameObject("TemporaryMenuSelectionCheck") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);

            var research = root.AddComponent<ResearchProgressManager>();
            var completed = (HashSet<string>)GetField(typeof(ResearchProgressManager), "completed").GetValue(research);
            completed.Add("production_basics");
            completed.Add("recipe_upgrades_1");
            researchInstanceField.SetValue(null, research);

            var config = ScriptableObject.CreateInstance<CustomerOrderConfig>();
            config.hideFlags = HideFlags.HideAndDontSave;
            transientAssets.Add(config);
            var items = new[]
            {
                NewItem("Hamburger", transientAssets),
                NewItem("Fries", transientAssets),
                NewItem("Shake", transientAssets)
            };
            config.burgerBase = items[0];
            config.friesItem = items[1];
            config.drinkItem = items[2];

            var inventory = root.AddComponent<KitchenInventory>();
            inventory.grantStartingStock = false;
            inventory.orderConfig = config;
            var ui = root.AddComponent<IngredientsOrderUI>();
            ui.enabled = false;
            ui.inventory = inventory;
            var content = new GameObject("ExistingMenuRows", typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            ui.listContainer = content.transform;

            // Construct the rows before restoring the save, as happens while
            // the title screen is open. Refresh must reuse these same rows.
            var rows = (List<GameObject>)GetField(typeof(IngredientsOrderUI), "menuRows").GetValue(ui);
            var toggles = new Toggle[items.Length];
            int callbacks = 0;
            for (int i = 0; i < items.Length; i++)
            {
                var row = new GameObject("ExistingMenuRow_" + items[i].name, typeof(RectTransform));
                row.transform.SetParent(content.transform, false);
                Toggle toggle = row.AddComponent<Toggle>();
                toggle.SetIsOnWithoutNotify(false);
                toggle.onValueChanged.AddListener(_ => callbacks++);
                var binding = row.AddComponent<MilestoneLockedMenuRow>();
                binding.item = items[i];
                binding.toggle = toggle;
                toggles[i] = toggle;
                rows.Add(row);
            }
            GetField(typeof(IngredientsOrderUI), "built").SetValue(ui, true);
            MethodInfo catalogHash = typeof(IngredientsOrderUI).GetMethod("GetCatalogHash", InstanceFields);
            GetField(typeof(IngredientsOrderUI), "builtCatalogHash").SetValue(ui, catalogHash.Invoke(ui, null));

            config.RestoreEnabledMenuItems(new[] { "Fries", "Shake" });
            AssertSelections(config, items, new[] { false, true, true }, "first save state");
            AssertToggles(toggles, new[] { false, false, false }, "rows before save refresh");
            ui.Refresh();
            AssertToggles(toggles, new[] { false, true, true }, "first save refresh");
            AssertNoCallbacks(callbacks);

            config.RestoreEnabledMenuItems(new[] { "Hamburger" });
            ui.Refresh();
            AssertSelections(config, items, new[] { true, false, false }, "second save state");
            AssertToggles(toggles, new[] { true, false, false }, "second save refresh");
            AssertNoCallbacks(callbacks);

            config.RestoreEnabledMenuItems(Array.Empty<string>());
            ui.Refresh();
            AssertSelections(config, items, new[] { false, false, false }, "empty save state");
            AssertToggles(toggles, new[] { false, false, false }, "empty save refresh");
            AssertNoCallbacks(callbacks);
            if (rows.Count != items.Length || content.transform.childCount != items.Length)
                throw new InvalidOperationException("Refreshing restored selections rebuilt the existing rows.");

            // Confirm the callback counter is connected; the zero above must
            // be caused by silent refresh, rather than a disconnected listener.
            toggles[0].isOn = true;
            if (callbacks != 1)
                throw new InvalidOperationException("The regression fixture did not observe a normal checkbox change.");

            Debug.Log("Menu selection regression passed: existing checkboxes follow two restored saves and an empty selection without firing callbacks.");
        }
        finally
        {
            researchInstanceField.SetValue(null, previousResearch);
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            foreach (UnityEngine.Object asset in transientAssets)
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
        }
    }

    static ItemDefinition NewItem(string name, List<UnityEngine.Object> transientAssets)
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        item.name = name;
        item.itemName = name;
        item.hideFlags = HideFlags.HideAndDontSave;
        transientAssets.Add(item);
        return item;
    }

    static FieldInfo GetField(Type type, string name)
    {
        FieldInfo field = type.GetField(name, InstanceFields);
        return field ?? throw new InvalidOperationException(type.Name + "." + name + " was not found.");
    }

    static void AssertSelections(CustomerOrderConfig config, ItemDefinition[] items, bool[] expected, string phase)
    {
        for (int i = 0; i < items.Length; i++)
            if (config.IsItemEnabled(items[i]) != expected[i])
                throw new InvalidOperationException(phase + ": incorrect saved selection for " + items[i].name + ".");
    }

    static void AssertToggles(Toggle[] toggles, bool[] expected, string phase)
    {
        for (int i = 0; i < toggles.Length; i++)
            if (toggles[i].isOn != expected[i])
                throw new InvalidOperationException(phase + ": incorrect checkbox at index " + i + ".");
    }

    static void AssertNoCallbacks(int callbacks)
    {
        if (callbacks != 0)
            throw new InvalidOperationException("Refreshing restored selections fired " + callbacks + " toggle/save callbacks.");
    }
}
