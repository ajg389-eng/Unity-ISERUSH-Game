using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Versioned kitchen checkpoint. Transient customers and cooking jobs restart on load.</summary>
[Serializable]
public class KitchenSaveSnapshot
{
    public int version = 11, day, cash, width, height, milestone, tutorialStep, appearanceTheme;
    public string activeMilestoneId;
    public int wallTexture, floorTexture, roofTexture;
    public Color wallTint = Color.white, floorTint = Color.white, roofTint = Color.white;
    public float minutes;
    public bool tutorialComplete;
    public List<Equipment> equipment = new List<Equipment>();
    public List<Stock> inventory = new List<Stock>(), ingredients = new List<Stock>(), productionTargets = new List<Stock>();
    public List<Worker> workers = new List<Worker>();
    public List<Flow> flows = new List<Flow>();
    public List<WallPhoto> wallPhotos = new List<WallPhoto>();
    public List<string> completedMilestoneIds = new List<string>();
    public List<MissionProgressManager.SavedMissionProgress> missionProgress = new List<MissionProgressManager.SavedMissionProgress>();
    [Serializable] public class WallPhoto { public string name; public Vector3 position; }
    [Serializable] public class Stock { public string item; public int count, acquired; }
    [Serializable] public class Equipment { public string item, product, pantrySecondProduct; public Vector3 position, scale; public Quaternion rotation; public int output = -1, slot = -1, processedInputs, pantryInputs, bufferedOutputs, processingUnits; public float processProgress; public Vector3 counterPosition; }
    [Serializable] public class Worker { public string name; public Vector3 position; public int level, priority; public List<int> stations = new List<int>(); }
    [Serializable] public class FlowEdge { public int from = -1, to = -1; }
    [Serializable] public class Flow { public string name; public KitchenFlowKind kind; public List<string> steps; public bool graphInitialized; public List<int> stations = new List<int>(), workers = new List<int>(); public List<FlowEdge> edges = new List<FlowEdge>(); }

    public static KitchenSaveSnapshot Capture()
    {
        var s = new KitchenSaveSnapshot();
        foreach (WallPhotoDrag photo in UnityEngine.Object.FindObjectsByType<WallPhotoDrag>(FindObjectsSortMode.None))
            s.wallPhotos.Add(new WallPhoto { name = photo.gameObject.name, position = photo.transform.position });
        var clock = GameTimeManager.Instance;
        s.day = clock != null ? clock.CurrentDay : 1; s.minutes = clock != null ? clock.CurrentMinutes : 600;
        var money = UnityEngine.Object.FindFirstObjectByType<MoneyManager>(); s.cash = money != null ? money.CurrentMoney : 1000;
        var grid = GridManager.Instance; if (grid != null) { s.width = grid.Width; s.height = grid.Height; }
        s.tutorialComplete = OnboardingTutorial.IsComplete;
        s.tutorialStep = OnboardingTutorial.Instance != null ? OnboardingTutorial.Instance.SaveStepIndex : 0;
        s.milestone = MilestoneProgressManager.Instance != null ? MilestoneProgressManager.Instance.GetHighestReachedNumberedStage() : 0;
        if (MilestoneProgressManager.Instance != null)
        {
            s.activeMilestoneId = MilestoneProgressManager.Instance.ActiveMilestoneId;
            s.completedMilestoneIds.AddRange(MilestoneProgressManager.Instance.GetCompletedMilestoneIds());
        }
        if (MissionProgressManager.Instance != null)
            s.missionProgress = MissionProgressManager.Instance.CaptureProgress();
        var appearance = StoreAppearanceController.Instance;
        if (appearance != null)
        {
            s.wallTexture = appearance.WallTextureIndex;
            s.floorTexture = appearance.FloorTextureIndex;
            s.roofTexture = appearance.RoofTextureIndex;
            s.wallTint = appearance.WallTint;
            s.floorTint = appearance.FloorTint;
            s.roofTint = appearance.RoofTint;
        }
        var inv = UnityEngine.Object.FindFirstObjectByType<InventoryManager>();
        var objects = new List<GameObject>();
        if (inv != null)
        {
            foreach (var item in inv.allItems) if (item != null) s.inventory.Add(new Stock { item=item.name, count=inv.GetCount(item), acquired=inv.GetAcquiredCount(item) });
            foreach (var item in inv.allItems)
            {
                if (item == null || item.prefab == null) continue;
                foreach (var candidate in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                {
                    var go = candidate.gameObject;
                    if (objects.Contains(go) || go.name.Contains("Ghost")) continue;
                    var placed = go.GetComponent<PlacedBuildItem>();
                    var mounted = go.GetComponent<CounterMountedItem>();
                    var groundPickup = go.GetComponent<GroundPickupStationPlacement>();
                    bool match = placed != null ? placed.itemDefinition == item : mounted != null ? mounted.itemDefinition == item
                        : go.name.Replace("(Clone)", "").Trim() == item.prefab.name
                            || (!string.IsNullOrEmpty(WorkerFlowAssigner.GetStationId(go)) && WorkerFlowAssigner.GetStationId(go) == WorkerFlowAssigner.GetStationId(item.prefab));
                    if (!match) continue;
                    objects.Add(go);
                    var grill = go.GetComponent<GrillStation>();
                    var assembly = go.GetComponent<AssemblyStation>();
                    var freezer = go.GetComponent<FreezerStation>();
                    var pantry = go.GetComponent<PantryStation>();
                    var cutting = go.GetComponent<CuttingStation>();
                    ItemDefinition product = assembly != null ? assembly.selectedProduct
                        : grill != null ? grill.selectedProduct
                        : freezer != null ? freezer.selectedItem
                        : pantry != null ? pantry.selectedItem
                        : cutting != null ? cutting.selectedProduct : null;
                    s.equipment.Add(new Equipment { item=item.name, product=product != null ? product.name : "", position=candidate.position, rotation=candidate.rotation, scale=candidate.lossyScale,
                        pantrySecondProduct=pantry != null && pantry.secondItem != null ? pantry.secondItem.name : "",
                        slot=mounted != null ? mounted.slotIndex : groundPickup != null ? groundPickup.slotIndex : -1,
                        counterPosition=mounted != null && mounted.surface != null ? mounted.surface.transform.position
                            : groundPickup != null && groundPickup.surface != null ? groundPickup.surface.transform.position : Vector3.zero,
                        processedInputs=assembly != null ? assembly.BufferedProcessedInputCount : 0,
                        pantryInputs=assembly != null ? assembly.BufferedPantryInputCount : 0,
                        bufferedOutputs=assembly != null ? assembly.BufferedOutputCount : 0,
                        processingUnits=grill != null ? grill.BufferedPattyCount : 0,
                        processProgress=grill != null ? grill.CookProgressSeconds : 0f });
                }
            }
            for (int i=0;i<objects.Count;i++) { var node=objects[i].GetComponent<StationNode>(); if(node!=null) s.equipment[i].output=objects.IndexOf(node.outputTarget); }
        }
        var kitchen=KitchenInventory.Instance;
        if(kitchen!=null) foreach(var entry in kitchen.stock) if(entry.item!=null) s.ingredients.Add(new Stock {item=entry.item.name,count=entry.quantity});
        var pm=ProductionManager.Instance;
        if(pm!=null) {
            foreach(var target in pm.productionTargets) if(target!=null && target.item!=null)
                s.productionTargets.Add(new Stock {item=target.item.name,count=Mathf.Max(0,target.quantity)});
            var staff=new List<KitchenEmployee>();
            foreach(var employee in pm.employees) if(employee!=null) {
                staff.Add(employee);
                var w=new Worker {name=employee.employeeName,position=employee.transform.position,level=employee.UpgradeLevel,priority=(int)employee.taskPriority};
                foreach(var station in employee.operatedStations) w.stations.Add(objects.IndexOf(station));
                s.workers.Add(w);
            }
            foreach(var flow in pm.productionFlows) if(flow!=null) {
                flow.EnsureLegacyConnections();
                var f=new Flow {name=flow.flowName,kind=flow.kind,steps=new List<string>(flow.stepIds),graphInitialized=flow.graphInitialized};
                foreach(var station in flow.stations) f.stations.Add(objects.IndexOf(station));
                foreach(var worker in flow.workers) f.workers.Add(staff.IndexOf(worker));
                foreach(var edge in flow.connections) if(edge!=null) f.edges.Add(new FlowEdge {from=objects.IndexOf(edge.from),to=objects.IndexOf(edge.to)});
                s.flows.Add(f);
            }
        }
        return s;
    }

    public bool Restore()
    {
        var inv=UnityEngine.Object.FindFirstObjectByType<InventoryManager>();
        var pm=ProductionManager.Instance;
        if(version<1 || version>11 || inv==null || pm==null) return false;
        // Validate assets before removing anything from the current kitchen.
        var definitions=new Dictionary<string,ItemDefinition>();
        foreach(var item in Resources.FindObjectsOfTypeAll<ItemDefinition>()) if(item!=null) {
            definitions[item.name]=item;
            // Bacon Slab replaced the legacy Raw Bacon ingredient. Saves persist
            // stock and station selections by asset name, so retain the old key.
            if(item.name=="Bacon Slab") definitions["Raw Bacon"]=item;
            // Saves created before station tiers used the original asset names.
            // Keep those keys pointed at MK1 so existing kitchens still load.
            if(item.IsTieredStation && item.stationMark==1) {
                string legacyName=item.stationFamily;
                if(item.stationFamily=="Cutting Station") legacyName="CuttingStation";
                else if(item.stationFamily=="Pickup Station") legacyName="PickupStation";
                definitions[legacyName]=item;
            }
        }
        foreach(var e in equipment) if(!definitions.ContainsKey(e.item) || definitions[e.item].prefab==null) return false;
        foreach(var e in equipment) if(!string.IsNullOrEmpty(e.pantrySecondProduct) && !definitions.ContainsKey(e.pantrySecondProduct)) return false;
        if(workers.Count>0 && pm.employeePrefab==null) return false;
        foreach(var employee in new List<KitchenEmployee>(pm.employees)) if(employee!=null) { employee.AbortCurrentWork(); employee.ClearAllOperatedStations(); employee.gameObject.SetActive(false); UnityEngine.Object.Destroy(employee.gameObject); }
        pm.ResetTransientProductionState();
        pm.employees.Clear(); pm.productionFlows.Clear();
        pm.productionTargets.Clear();
        if(productionTargets!=null) foreach(var entry in productionTargets)
            if(entry!=null && definitions.TryGetValue(entry.item,out var targetItem))
                pm.productionTargets.Add(new ProductionManager.ProductionTarget {item=targetItem,quantity=Mathf.Clamp(entry.count,0,20)});
        foreach(var candidate in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)) {
            if (candidate.GetComponent<CustomerWallDoor>() != null || candidate.GetComponentInParent<CustomerWallDoor>() != null)
                continue;
            var placed=candidate.GetComponent<PlacedBuildItem>(); var mounted=candidate.GetComponent<CounterMountedItem>();
            bool match=placed!=null || mounted!=null;
            if (!string.IsNullOrEmpty(WorkerFlowAssigner.GetStationId(candidate.gameObject))) match = true;
            if(!match) foreach(var item in inv.allItems) if(item!=null && item.prefab!=null && candidate.name.Replace("(Clone)","").Trim()==item.prefab.name) {match=true;break;}
            if(!match) continue;
            if(mounted!=null && mounted.surface!=null) mounted.surface.Release(mounted);
            candidate.GetComponent<GroundPickupStationPlacement>()?.ReleaseSlot();
            candidate.gameObject.SetActive(false); UnityEngine.Object.Destroy(candidate.gameObject);
        }
        var grid=GridManager.Instance;
        if(grid!=null) { grid.ResyncOccupancyFromScene(); grid.TryExpand(Mathf.Max(0,width-grid.Width),Mathf.Max(0,height-grid.Height)); }
        var objects=new List<GameObject>();
        foreach(var e in equipment) {
            var item=definitions[e.item]; var go=UnityEngine.Object.Instantiate(item.prefab,e.position,e.rotation);
            // Tiered stations use the current authored prefab dimensions. Older
            // saves may contain scale values from the station models they replace.
            go.transform.localScale=item.IsTieredStation && item.prefab!=null
                ? item.prefab.transform.localScale : e.scale;
            BuildPlacer.ConfigurePlacedObject(go,item);
            var placed=go.GetComponent<PlacedBuildItem>() ?? go.AddComponent<PlacedBuildItem>(); placed.itemDefinition=item;
            objects.Add(go);
        }
        for(int i=0;i<equipment.Count;i++) {
            var e=equipment[i]; var go=objects[i]; var item=definitions[e.item];
            ItemDefinition product = !string.IsNullOrEmpty(e.product) && definitions.TryGetValue(e.product,out var savedProduct) ? savedProduct : null;
            var grill=go.GetComponent<GrillStation>(); if(grill!=null && product!=null) grill.RestoreBufferedState(product,e.processingUnits,e.processProgress);
            var assembly=go.GetComponent<AssemblyStation>(); if(assembly!=null && product!=null) assembly.RestoreBufferedState(product,e.processedInputs,e.pantryInputs,e.bufferedOutputs);
            var freezer=go.GetComponent<FreezerStation>(); if(freezer!=null) freezer.SetStoredItem(product);
            var pantry=go.GetComponent<PantryStation>(); if(pantry!=null) {
                ItemDefinition second=null;
                if(!string.IsNullOrEmpty(e.pantrySecondProduct)) definitions.TryGetValue(e.pantrySecondProduct,out second);
                pantry.SetStoredItems(product,second);
            }
            var cutting=go.GetComponent<CuttingStation>(); if(cutting!=null) cutting.SetRecipe(pm.orderConfig != null ? pm.orderConfig.GetCuttingRecipe(product) : null);
            if (item.stationFamily == "Pickup Station"
                && item.placementSurface == ItemDefinition.PlacementSurface.Floor)
            {
                CounterSurface closest = null;
                float best = float.PositiveInfinity;
                Vector3 anchor = e.slot >= 0 ? e.counterPosition : e.position;
                foreach (CounterSurface surface in UnityEngine.Object.FindObjectsByType<CounterSurface>(FindObjectsSortMode.None))
                {
                    if (surface == null) continue;
                    float distance = (surface.transform.position - anchor).sqrMagnitude;
                    if (distance >= best) continue;
                    closest = surface;
                    best = distance;
                }

                int slot = e.slot;
                if (closest != null && slot < 0)
                    closest.TryGetGroundStationSlot(e.position, out slot);
                if (closest != null && slot >= 0 && closest.OpenGroundStationSlot(slot))
                {
                    var groundPlacement = go.GetComponent<GroundPickupStationPlacement>()
                        ?? go.AddComponent<GroundPickupStationPlacement>();
                    groundPlacement.BindSlot(closest, slot, item);
                    Vector3 stationPosition = closest.GetSlotWorldCenter(slot);
                    GridManager placementGrid = GridManager.Instance;
                    go.transform.position = stationPosition;
                    AlignObjectBottomToFloor(go, placementGrid != null ? placementGrid.Origin.y : stationPosition.y);
                }
            }
            else if (e.slot >= 0)
            {
                CounterSurface closest = null;
                float best = float.PositiveInfinity;
                foreach (CounterSurface surface in UnityEngine.Object.FindObjectsByType<CounterSurface>(FindObjectsSortMode.None))
                {
                    float distance = (surface.transform.position - e.counterPosition).sqrMagnitude;
                    if (distance >= best) continue;
                    closest = surface;
                    best = distance;
                }
                if (closest != null)
                {
                    var mounted = go.GetComponent<CounterMountedItem>() ?? go.AddComponent<CounterMountedItem>();
                    mounted.itemDefinition = item;
                    closest.Attach(mounted, e.slot);
                }
            }
        }
        for(int i=0;i<equipment.Count;i++) { int output=equipment[i].output; if(output>=0 && output<objects.Count) StationNode.EnsureOn(objects[i]).SetOutput(objects[output]); }
        foreach(var entry in inventory) if(definitions.TryGetValue(entry.item,out var item)) inv.RestoreCounts(item,entry.count,entry.acquired);
        if(KitchenInventory.Instance!=null) { KitchenInventory.Instance.ClearAllStock(); foreach(var entry in ingredients) if(definitions.TryGetValue(entry.item,out var item)) KitchenInventory.Instance.AddStock(item,entry.count); }
        var staff=new List<KitchenEmployee>();
        foreach(var w in workers) {
            var go=UnityEngine.Object.Instantiate(pm.employeePrefab,w.position,Quaternion.identity); var employee=go.GetComponent<KitchenEmployee>();
            employee.employeeName=w.name; employee.RestoreUpgradeLevel(w.level); employee.taskPriority=(KitchenEmployee.TaskPriority)Mathf.Clamp(w.priority,0,System.Enum.GetValues(typeof(KitchenEmployee.TaskPriority)).Length-1); pm.RegisterEmployee(employee); staff.Add(employee);
            foreach(int i in w.stations) if(i>=0 && i<objects.Count) { employee.AddOperatedStation(objects[i]); StationNode.EnsureOn(objects[i]).AddWorker(employee); }
            employee.SyncFromOperatedStations();
        }
        foreach(var f in flows) {
            var flow=new ProductionFlowPlan {flowName=f.name,kind=f.kind,stepIds=f.steps,graphInitialized=version>=6 && f.graphInitialized};
            foreach(int i in f.stations) if(i>=0 && i<objects.Count) flow.stations.Add(objects[i]);
            if(f.edges!=null) foreach(var edge in f.edges) if(edge!=null && edge.from>=0 && edge.from<objects.Count && edge.to>=0 && edge.to<objects.Count) flow.connections.Add(new ProductionFlowConnection(objects[edge.from],objects[edge.to]));
            flow.EnsureLegacyConnections();
            foreach(int i in f.workers) if(i>=0 && i<staff.Count) flow.workers.Add(staff[i]);
            pm.productionFlows.Add(flow);
        }
        foreach(var flow in pm.productionFlows) if(flow!=null) WorkerFlowAssigner.ApplyBalancedTeam(flow);
        // Version 1 checkpoints included the removed door lesson at index 2.
        int restoredTutorialStep = version == 1 && !tutorialComplete && tutorialStep > 2
            ? tutorialStep - 1
            : tutorialStep;
        if(OnboardingTutorial.Instance!=null) OnboardingTutorial.Instance.RestoreCheckpoint(tutorialComplete,restoredTutorialStep);
        MilestoneProgressManager milestoneProgress = MilestoneProgressManager.Instance;
        if (milestoneProgress != null)
        {
            if (version >= 10 && (completedMilestoneIds != null && completedMilestoneIds.Count > 0
                || !string.IsNullOrEmpty(activeMilestoneId)))
                milestoneProgress.RestoreCheckpoint(activeMilestoneId, completedMilestoneIds);
            else if (milestone > 0)
                milestoneProgress.DebugJumpToNumberedMilestone(
                    Mathf.Clamp(milestone, 1, Mathf.Max(1, milestoneProgress.GetNumberedMilestoneCount())), out _);
        }
        // Version 11 replaces the active milestone task definitions. Preserve the
        // saved milestone checkpoint from v10, but don't carry old task progress
        // into tasks whose objectives have changed.
        if (version >= 11 && MissionProgressManager.Instance != null)
            MissionProgressManager.Instance.RestoreProgress(missionProgress);
        if(grid!=null) grid.ResyncOccupancyFromScene();
        var appearance = StoreAppearanceController.Ensure();
        if (version >= 4)
            appearance.RestoreState(wallTexture, floorTexture, roofTexture, wallTint, floorTint, roofTint);
        else
            appearance.RestoreState(0, 0, 0, Color.white, Color.white, Color.white);
        var money=UnityEngine.Object.FindFirstObjectByType<MoneyManager>(); if(money!=null) money.SetMoney(cash);
        if(GameTimeManager.Instance!=null) GameTimeManager.Instance.RestoreCheckpoint(day,minutes);
        PurchaseUndoManager.Instance?.ClearHistory();
        var placer = UnityEngine.Object.FindFirstObjectByType<BuildPlacer>();
        if (placer != null)
            placer.EnsureCustomerEntrance();
        else
            UnityEngine.Object.FindFirstObjectByType<KitchenPerimeterWalls>()?.RequestRefresh();
        if (wallPhotos != null)
            foreach (WallPhotoDrag photo in UnityEngine.Object.FindObjectsByType<WallPhotoDrag>(FindObjectsSortMode.None))
                foreach (WallPhoto saved in wallPhotos)
                    if (saved != null && saved.name == photo.gameObject.name) photo.SetPosition(saved.position);
        return true;
    }

    static void AlignObjectBottomToFloor(GameObject obj, float floorY)
    {
        if (obj == null) return;
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        bool found = false;
        Bounds bounds = default;
        foreach (Renderer renderer in renderers)
        {
            if (CounterSurface.IsAuxiliaryPlacementRenderer(renderer, obj.transform)) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (found)
            obj.transform.position += Vector3.up * (floorY - bounds.min.y);
    }
}
