using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Recipe-driven assembly station. Recipes may create final menu items or intermediates
/// that feed a later assembly station.
/// </summary>
public class AssemblyStation : MonoBehaviour
{
    public const int IngredientCapacity = 2;
    public const int OutputCapacity = 2;

    [Header("Product")]
    [Tooltip("Recipe output this station assembles. Must be chosen in Manage mode.")]
    public ItemDefinition selectedProduct;
    [System.NonSerialized] AssemblyRecipeDefinition selectedRecipe;

    [FormerlySerializedAs("interactionTimeSeconds")]
    [Tooltip("Total time for one assembly operation.")]
    [Min(0f)] public float processTimeSeconds = 1.2f;
    public Vector3 interactionOffset = Vector3.zero;
    [SerializeField, Min(0)] int bufferedProcessedInputs;
    [SerializeField, Min(0)] int bufferedPantryInputs;
    [SerializeField, Min(0)] int bufferedOutputs;

    [Header("Table display")]
    [Tooltip("Fallback model for a cooked patty when the recipe input has no ItemDefinition prefab.")]
    public GameObject defaultProcessedInputDisplayPrefab;
    [Tooltip("Fallback model for an output whose ItemDefinition has no prefab.")]
    public GameObject defaultOutputDisplayPrefab;
    [Tooltip("Maximum world-space dimension for displayed ingredients. Finished outputs retain their prefab scale.")]
    [Min(0.02f)] public float ingredientDisplayWorldSize = 0.22f;
    [Tooltip("Two tabletop markers for the processed ingredient.")]
    public Transform[] processedInputDisplaySlots = new Transform[2];
    [Tooltip("Two tabletop markers for the pantry ingredient.")]
    public Transform[] pantryInputDisplaySlots = new Transform[2];
    [Tooltip("Two tabletop markers for completed outputs.")]
    public Transform[] outputDisplaySlots = new Transform[2];

    Transform tableDisplayRoot;

    public int BufferedProcessedInputCount => bufferedProcessedInputs;
    public int BufferedPantryInputCount => bufferedPantryInputs;
    public int BufferedOutputCount => bufferedOutputs;

    public bool HasProductSelected => GetSelectedRecipe() != null;

    public AssemblyRecipeDefinition GetSelectedRecipe()
    {
        if (selectedRecipe != null)
        {
            if (selectedProduct != selectedRecipe.output)
                selectedProduct = selectedRecipe.output;
            return selectedRecipe;
        }

        var manager = ProductionManager.Instance;
        var config = manager != null ? manager.orderConfig : null;
        selectedRecipe = config != null ? config.GetAssemblyRecipe(selectedProduct) : null;
        if (selectedRecipe != null)
            selectedProduct = selectedRecipe.output;
        return selectedRecipe;
    }

    public void SetRecipe(AssemblyRecipeDefinition recipe)
    {
        if (selectedRecipe != recipe)
        {
            bufferedProcessedInputs = 0;
            bufferedPantryInputs = 0;
            bufferedOutputs = 0;
        }
        selectedRecipe = recipe;
        selectedProduct = recipe != null ? recipe.output : null;
        RefreshTableDisplay();
    }

    public bool CanProcess(ItemDefinition product)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        return product != null && recipe != null && recipe.Produces(product);
    }

    public ItemDefinition GetPantryInput(ItemDefinition product)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        return recipe != null && recipe.Produces(product) ? recipe.pantryInput : null;
    }

    public int ReceivePantryInput(ItemDefinition item, int amount)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null || item == null || item != recipe.pantryInput || amount <= 0) return 0;
        int accepted = Mathf.Min(amount, Mathf.Max(0, IngredientCapacity - bufferedPantryInputs));
        bufferedPantryInputs += accepted;
        if (accepted > 0) RefreshTableDisplay();
        return accepted;
    }

    public bool CanReceiveProcessedInput(ItemDefinition item, int amount)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null || amount <= 0) return false;
        if (recipe.processedInput != null && item != recipe.processedInput) return false;
        return bufferedProcessedInputs + amount <= IngredientCapacity;
    }

    public int ReceiveProcessedInput(ItemDefinition item, int amount)
    {
        if (!CanReceiveProcessedInput(item, amount)) return 0;
        bufferedProcessedInputs += amount;
        RefreshTableDisplay();
        return amount;
    }

    public bool CanStoreOutput(int amount)
    {
        return amount > 0 && bufferedOutputs + amount <= OutputCapacity;
    }

    public bool HasRequiredInputs(ItemDefinition product, IReadOnlyList<ItemDefinition> pantryMaterials, int units)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null || !recipe.Produces(product) || units <= 0 || recipe.pantryInput == null)
            return false;

        int required = units * Mathf.Max(1, recipe.pantryInputAmount);
        int present = 0;
        if (pantryMaterials != null)
        {
            for (int i = 0; i < pantryMaterials.Count; i++)
                if (pantryMaterials[i] == recipe.pantryInput)
                    present++;
        }
        int processedRequired = units * Mathf.Max(1, recipe.processedInputAmount);
        return bufferedProcessedInputs >= processedRequired
            && present + bufferedPantryInputs >= required
            && CanStoreOutput(units);
    }

    public bool TryAssemble(ItemDefinition product, List<ItemDefinition> pantryMaterials, int units)
    {
        if (!HasRequiredInputs(product, pantryMaterials, units)) return false;
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        int processedConsume = units * Mathf.Max(1, recipe.processedInputAmount);
        int consume = units * Mathf.Max(1, recipe.pantryInputAmount);
        for (int i = pantryMaterials.Count - 1; i >= 0 && consume > 0; i--)
        {
            if (pantryMaterials[i] != recipe.pantryInput) continue;
            pantryMaterials.RemoveAt(i);
            consume--;
        }
        if (consume > 0)
        {
            int fromBuffer = Mathf.Min(consume, bufferedPantryInputs);
            bufferedPantryInputs -= fromBuffer;
            consume -= fromBuffer;
        }
        if (consume != 0) return false;

        bufferedProcessedInputs = Mathf.Max(0, bufferedProcessedInputs - processedConsume);
        bufferedOutputs = Mathf.Min(OutputCapacity, bufferedOutputs + units);
        RefreshTableDisplay();
        return true;
    }

    public int TakeOutput(ItemDefinition item, int amount)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null || item == null || !recipe.Produces(item) || amount <= 0) return 0;
        int taken = Mathf.Min(amount, bufferedOutputs);
        bufferedOutputs -= taken;
        if (taken > 0) RefreshTableDisplay();
        return taken;
    }

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    void OnEnable()
    {
        bufferedProcessedInputs = Mathf.Clamp(bufferedProcessedInputs, 0, IngredientCapacity);
        bufferedPantryInputs = Mathf.Clamp(bufferedPantryInputs, 0, IngredientCapacity);
        bufferedOutputs = Mathf.Clamp(bufferedOutputs, 0, OutputCapacity);
        HideSlotMarkers();
        RefreshTableDisplay();
    }

    void RefreshTableDisplay()
    {
        EnsureTableDisplayRoot();
        ClearTableDisplay();

        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null) return;

        GameObject processedPrefab = recipe.processedInput != null && recipe.processedInput.prefab != null
            ? recipe.processedInput.prefab : defaultProcessedInputDisplayPrefab;
        GameObject pantryPrefab = recipe.pantryInput != null ? recipe.pantryInput.prefab : null;
        GameObject outputPrefab = recipe.output != null && recipe.output.prefab != null
            ? recipe.output.prefab : defaultOutputDisplayPrefab;

        SpawnDisplayedItems(processedPrefab, Mathf.Min(bufferedProcessedInputs, IngredientCapacity),
            processedInputDisplaySlots, "ProcessedInput", true);
        SpawnDisplayedItems(pantryPrefab, Mathf.Min(bufferedPantryInputs, IngredientCapacity),
            pantryInputDisplaySlots, "PantryInput", true);
        SpawnDisplayedItems(outputPrefab, Mathf.Min(bufferedOutputs, OutputCapacity),
            outputDisplaySlots, "Output", false);
    }

    void EnsureTableDisplayRoot()
    {
        if (tableDisplayRoot != null) return;
        Transform existing = transform.Find("AssemblyTableDisplay");
        if (existing != null)
        {
            tableDisplayRoot = existing;
            return;
        }

        tableDisplayRoot = new GameObject("AssemblyTableDisplay").transform;
        tableDisplayRoot.SetParent(transform, false);
    }

    void ClearTableDisplay()
    {
        if (tableDisplayRoot == null) return;
        for (int i = tableDisplayRoot.childCount - 1; i >= 0; i--)
            Destroy(tableDisplayRoot.GetChild(i).gameObject);
    }

    void SpawnDisplayedItems(GameObject prefab, int count, Transform[] slots, string label, bool normalizeIngredientSize)
    {
        if (prefab == null || count <= 0 || slots == null) return;

        int visible = Mathf.Min(count, slots.Length);
        for (int i = 0; i < visible; i++)
        {
            Transform slot = slots[i];
            if (slot == null) continue;

            GameObject display = Instantiate(prefab, tableDisplayRoot);
            display.name = label + "_" + (i + 1) + "_" + prefab.name;
            Vector3 sourceScale = prefab.transform.localScale;
            display.transform.localPosition = transform.InverseTransformPoint(slot.position);
            display.transform.localRotation = prefab.transform.localRotation;
            SetNativeWorldScale(display.transform, sourceScale);
            if (normalizeIngredientSize)
                NormalizeWorldSize(display, ingredientDisplayWorldSize);
            DisableDisplayColliders(display);
        }
    }

    void HideSlotMarkers()
    {
        HideSlotMarkers(processedInputDisplaySlots);
        HideSlotMarkers(pantryInputDisplaySlots);
        HideSlotMarkers(outputDisplaySlots);
    }

    static void HideSlotMarkers(Transform[] slots)
    {
        if (slots == null) return;
        foreach (Transform slot in slots)
        {
            if (slot == null) continue;
            foreach (Renderer renderer in slot.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            foreach (Collider collider in slot.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }
    }

    static void SetNativeWorldScale(Transform target, Vector3 sourceScale)
    {
        Vector3 parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
        target.localScale = new Vector3(
            sourceScale.x / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            sourceScale.y / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            sourceScale.z / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
    }

    static void NormalizeWorldSize(GameObject display, float targetSize)
    {
        Renderer[] renderers = display.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largest <= 0.0001f) return;

        float uniformFactor = Mathf.Max(0.01f, targetSize) / largest;
        display.transform.localScale *= uniformFactor;
    }

    static void DisableDisplayColliders(GameObject display)
    {
        foreach (Collider collider in display.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
    }
}
