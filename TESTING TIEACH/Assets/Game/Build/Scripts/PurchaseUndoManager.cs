using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Records station buys, floor expansions, worker hires, and ingredient orders so they can be undone.
/// </summary>
public class PurchaseUndoManager : MonoBehaviour
{
    public static PurchaseUndoManager Instance { get; private set; }

    struct StationRecord
    {
        public ItemDefinition item;
        public int paid;
        public GameObject placed;
    }

    struct FloorRecord
    {
        public int addWidth;
        public int addHeight;
        public int paid;
    }

    struct WorkerRecord
    {
        public KitchenEmployee employee;
        public int paid;
    }

    struct IngredientRecord
    {
        public ItemDefinition item;
        public int packSize;
        public int paid;
    }

    enum Kind
    {
        Station,
        Floor,
        Worker,
        Ingredient
    }

    struct Entry
    {
        public Kind kind;
        public StationRecord station;
        public FloorRecord floor;
        public WorkerRecord worker;
        public IngredientRecord ingredient;
    }

    readonly List<Entry> stack = new List<Entry>();

    public int Count => stack.Count;
    public bool CanUndo => stack.Count > 0;

    public string PeekLabel
    {
        get
        {
            if (stack.Count == 0) return "Undo";
            var e = stack[stack.Count - 1];
            if (e.kind == Kind.Floor)
                return "Undo Floor $" + e.floor.paid;
            if (e.kind == Kind.Worker)
                return "Undo Hire $" + e.worker.paid;
            if (e.kind == Kind.Ingredient)
            {
                string iname = e.ingredient.item != null ? e.ingredient.item.itemName : "Ingredients";
                return "Undo " + iname + " $" + e.ingredient.paid;
            }
            string name = e.station.item != null ? e.station.item.itemName : "Station";
            return "Undo " + name + " $" + e.station.paid;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        Ensure();
    }

    public static PurchaseUndoManager Ensure()
    {
        if (Instance != null)
            return Instance;

        var existing = FindFirstObjectByType<PurchaseUndoManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        Component host = FindFirstObjectByType<InventoryManager>();
        if (host == null)
            host = FindFirstObjectByType<MoneyManager>();
        if (host == null)
            return null;

        Instance = host.gameObject.AddComponent<PurchaseUndoManager>();
        return Instance;
    }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.Z))
            TryUndo();
    }

    public void RecordStationPurchase(ItemDefinition item, int paid)
    {
        if (item == null) return;
        stack.Add(new Entry
        {
            kind = Kind.Station,
            station = new StationRecord { item = item, paid = Mathf.Max(0, paid), placed = null }
        });
    }

    public void NotifyStationPlaced(ItemDefinition item, GameObject placed)
    {
        if (item == null || placed == null) return;
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            var e = stack[i];
            if (e.kind != Kind.Station) continue;
            if (e.station.item != item) continue;
            if (e.station.placed != null) continue;
            e.station.placed = placed;
            stack[i] = e;
            return;
        }
    }

    public void RecordFloorExpand(int addWidth, int addHeight, int paid)
    {
        stack.Add(new Entry
        {
            kind = Kind.Floor,
            floor = new FloorRecord
            {
                addWidth = Mathf.Max(0, addWidth),
                addHeight = Mathf.Max(0, addHeight),
                paid = Mathf.Max(0, paid)
            }
        });
    }

    public void RecordWorkerHire(KitchenEmployee employee, int paid)
    {
        if (employee == null) return;
        stack.Add(new Entry
        {
            kind = Kind.Worker,
            worker = new WorkerRecord { employee = employee, paid = Mathf.Max(0, paid) }
        });
    }

    public void RecordIngredientPack(ItemDefinition item, int packSize, int paid)
    {
        if (item == null) return;
        stack.Add(new Entry
        {
            kind = Kind.Ingredient,
            ingredient = new IngredientRecord
            {
                item = item,
                packSize = Mathf.Max(1, packSize),
                paid = Mathf.Max(0, paid)
            }
        });
    }

    public void NotifyWorkerFired(KitchenEmployee employee)
    {
        if (employee == null) return;
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (stack[i].kind != Kind.Worker) continue;
            if (stack[i].worker.employee != employee) continue;
            stack.RemoveAt(i);
            return;
        }
    }

    public bool TryUndo()
    {
        if (stack.Count == 0)
            return false;

        var e = stack[stack.Count - 1];
        bool ok;
        switch (e.kind)
        {
            case Kind.Floor:
                ok = UndoFloor(e.floor);
                break;
            case Kind.Worker:
                ok = UndoWorker(e.worker);
                break;
            case Kind.Ingredient:
                ok = UndoIngredient(e.ingredient);
                break;
            default:
                ok = UndoStation(e.station);
                break;
        }
        if (!ok)
        {
            Sfx.Play(SfxId.UiError);
            return false;
        }

        stack.RemoveAt(stack.Count - 1);
        Sfx.Play(SfxId.EarnMoney);
        RefreshRelatedUi();
        return true;
    }

    static void RefreshRelatedUi()
    {
        var invUI = FindFirstObjectByType<InventoryUI>();
        if (invUI != null)
            invUI.RefreshAll();

        var workersUI = FindFirstObjectByType<WorkersUI>();
        if (workersUI != null)
            workersUI.Refresh();

        var ingredientsUI = FindFirstObjectByType<IngredientsOrderUI>();
        if (ingredientsUI != null)
            ingredientsUI.Refresh();
    }

    bool UndoStation(StationRecord rec)
    {
        var money = FindFirstObjectByType<MoneyManager>();
        var inventory = FindFirstObjectByType<InventoryManager>();

        if (rec.placed != null)
        {
            if (!RemovePlacedStation(rec.placed))
                return false;
            if (money != null)
                money.AddMoney(rec.paid);
            return true;
        }

        if (inventory == null || rec.item == null)
            return false;
        if (!inventory.TryConsumeOne(rec.item))
            return false;
        if (money != null)
            money.AddMoney(rec.paid);
        return true;
    }

    bool UndoWorker(WorkerRecord rec)
    {
        if (rec.employee == null)
            return false;

        var production = ProductionManager.Instance != null
            ? ProductionManager.Instance
            : FindFirstObjectByType<ProductionManager>();
        if (production != null)
            production.UnregisterEmployee(rec.employee);
        Object.Destroy(rec.employee.gameObject);

        var money = FindFirstObjectByType<MoneyManager>();
        if (money != null)
            money.AddMoney(rec.paid);
        return true;
    }

    bool UndoIngredient(IngredientRecord rec)
    {
        var kitchen = KitchenInventory.Instance != null
            ? KitchenInventory.Instance
            : FindFirstObjectByType<KitchenInventory>();
        if (kitchen == null || rec.item == null)
            return false;
        if (!kitchen.TryConsume(rec.item, rec.packSize))
            return false;

        var money = FindFirstObjectByType<MoneyManager>();
        if (money != null)
            money.AddMoney(rec.paid);
        return true;
    }

    bool UndoFloor(FloorRecord rec)
    {
        var grid = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
        var money = FindFirstObjectByType<MoneyManager>();
        if (grid == null)
            return false;
        if (!grid.TryShrink(rec.addWidth, rec.addHeight))
            return false;
        if (money != null)
            money.AddMoney(rec.paid);
        return true;
    }

    static bool RemovePlacedStation(GameObject go)
    {
        var grid = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
        var placer = FindFirstObjectByType<BuildPlacer>();
        if (placer != null && placer.IsDragging)
            placer.CancelDrag();

        if (grid != null)
        {
            var fp = go.GetComponent<BuildFootprint>();
            int sizeX = Mathf.Max(1, fp != null ? fp.sizeX : 1);
            int sizeY = Mathf.Max(1, fp != null ? fp.sizeY : 1);
            float ay = go.transform.eulerAngles.y;
            int rot = (Mathf.RoundToInt(ay / 90f) % 4 + 4) % 4;
            if (rot == 1 || rot == 3)
            {
                int t = sizeX;
                sizeX = sizeY;
                sizeY = t;
            }

            float cx = (go.transform.position.x - grid.Origin.x) / grid.cellSize;
            float cy = (go.transform.position.z - grid.Origin.z) / grid.cellSize;
            int ox = Mathf.RoundToInt(cx - sizeX * 0.5f);
            int oy = Mathf.RoundToInt(cy - sizeY * 0.5f);
            if (ox >= 0 && oy >= 0 && ox + sizeX <= grid.Width && oy + sizeY <= grid.Height)
                grid.SetOccupied(ox, oy, sizeX, sizeY, false);
        }

        Object.Destroy(go);
        return true;
    }
}
