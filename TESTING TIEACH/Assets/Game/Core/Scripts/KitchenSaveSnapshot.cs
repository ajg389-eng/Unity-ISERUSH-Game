using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Versioned checkpoint for kitchen, active customers, and campaign state.</summary>
[Serializable]
public class KitchenSaveSnapshot
{
    public const int CurrentVersion = 45;
    public int version = CurrentVersion, day, cash, width, height, milestone, tutorialStep, appearanceTheme;
    public int totalWorkersHired;
    public string activeMilestoneId;
    public int wallTexture, floorTexture, roofTexture;
    public Color wallTint = Color.white, floorTint = Color.white, roofTint = Color.white;
    public float minutes;
    public int dailyCompletedOrders, dailyRevenue, careerDailyTasksCompleted;
    public int dailyMealsWasted, dailyCustomersLost, dailyCustomersVisited, dailyMoneyAtStart;
    public bool dailyRevenueTaskAwarded, dailyWaitTaskAwarded;
    public bool dailyTrackingStarted;
    public float dailyWaitTotalSeconds;
    public int[] dailyVisitBuckets = Array.Empty<int>();
    public int careerOrdersServed, careerCustomersVisited, careerCustomersLost;
    public int careerShiftsCompleted, careerRevenue, careerMealsWasted;
    public int careerBestShiftRevenue, careerBestShiftOrders, careerLastCompletedDay;
    public float careerWaitTotalSeconds;
    public bool tutorialComplete;
    public bool tutorialOrderServed;
    public bool tutorialPauseControlUsed;
    public bool tutorialPlayControlUsed;
    public bool tutorialFastForwardControlUsed;
    public bool tutorialShiftStarted;
    public int tutorialFastForwardRateMask;
    public int tutorialAnalyticsStage = -1;
    public float tutorialBaselineRate, tutorialTargetRate;
    public bool tutorialStationMoved;
    public bool milestoneOneBottleneckLessonDone;
    public bool milestoneOneAnalyticsLessonDone;
    public bool activeMilestoneShiftObjectives;
    public ShiftPlanningUI.SavedState shiftPlanningState;
    public List<Equipment> equipment = new List<Equipment>();
    public List<Stock> inventory = new List<Stock>(), ingredients = new List<Stock>();
    public List<IngredientShipment> ingredientShipments = new List<IngredientShipment>();
    public List<Worker> workers = new List<Worker>();
    public List<CustomerAI.SavedState> customers = new List<CustomerAI.SavedState>();
    public List<Flow> flows = new List<Flow>();
    public List<WallPhoto> wallPhotos = new List<WallPhoto>();
    public List<string> completedMilestoneIds = new List<string>();
    public List<string> completedResearchIds = new List<string>();
    public List<string> enabledMenuItemNames = new List<string>();
    public List<CustomerOrderConfig.SavedItemReputation> itemReputation = new List<CustomerOrderConfig.SavedItemReputation>();
    public string activeResearchId;
    public float activeResearchMinutesRemaining;
    public List<MissionProgressManager.SavedMissionProgress> missionProgress = new List<MissionProgressManager.SavedMissionProgress>();
    [Serializable] public class WallPhoto { public string name; public Vector3 position; }
    [Serializable] public class Stock
    {
        public string item;
        public int count, acquired, reorderTarget, supplyRatePerMinute;
        public float supplyUnitProgress, supplyCostRemainder;
    }
    [Serializable] public class IngredientShipment { public float remaining, duration; public List<IngredientShipmentPack> packs = new List<IngredientShipmentPack>(); }
    [Serializable] public class IngredientShipmentPack { public string item; public int amount; }
    [Serializable] public class Equipment { public string item, product, pantrySecondProduct, stationGuid; public Vector3 position, scale; public Quaternion rotation; public int output = -1, slot = -1, processedInputs, pantryInputs, thirdInputs, bufferedOutputs, processingUnits, secondaryInputs, assemblyProcessingUnits, stationCurrentState; public float processProgress, assemblyProcessProgress, productionRatePercent = 100f, stationWorkingSeconds, stationStarvedSeconds, stationBlockedSeconds, stationIdleSeconds, stationCurrentStateSeconds; public Vector3 counterPosition; public bool registerEnabled = true, hasStationMetrics; public ProductionStationCheckpoint productionState; }
    [Serializable] public class Worker { public string name; public Vector3 position; public int level, priority, bodyIndex, faceIndex, hatIndex = -1; public List<int> stations = new List<int>(); public List<string> stationGuids = new List<string>(); public ProductionWorkerCheckpoint productionState; }
    [Serializable] public class FlowEdge { public int from = -1, to = -1; public string fromGuid, toGuid; }
    [Serializable] public class Flow { public string name; public KitchenFlowKind kind; public List<string> steps; public bool graphInitialized, autoName; public List<int> stations = new List<int>(), workers = new List<int>(); public List<string> stationGuids = new List<string>(); public List<FlowEdge> edges = new List<FlowEdge>(); }

    public static KitchenSaveSnapshot Capture()
    {
        ProductionManager scheduleManager = ProductionManager.Instance;
        if (scheduleManager != null && scheduleManager.useScheduledProduction) scheduleManager.ScheduledProduction.Tick();
        var s = new KitchenSaveSnapshot();
        foreach (WallPhotoDrag photo in UnityEngine.Object.FindObjectsByType<WallPhotoDrag>(FindObjectsSortMode.None))
            s.wallPhotos.Add(new WallPhoto { name = photo.gameObject.name, position = photo.PlacementPosition });
        var clock = GameTimeManager.Instance;
        s.day = clock != null ? clock.CurrentDay : 1; s.minutes = clock != null ? clock.CurrentMinutes : 600;
        s.shiftPlanningState = ShiftPlanningUI.CaptureSaveState();
        var money = UnityEngine.Object.FindFirstObjectByType<MoneyManager>(); s.cash = money != null ? money.CurrentMoney : 1000;
        var grid = GridManager.Instance; if (grid != null) { s.width = grid.Width; s.height = grid.Height; }
        s.tutorialComplete = OnboardingTutorial.IsComplete;
        s.tutorialStep = OnboardingTutorial.Instance != null ? OnboardingTutorial.Instance.SaveStepIndex : 0;
        s.tutorialOrderServed = OnboardingTutorial.Instance != null && OnboardingTutorial.Instance.OrderServedForSave;
        s.tutorialPauseControlUsed = OnboardingTutorial.Instance != null && OnboardingTutorial.Instance.TutorialPauseUsedForSave;
        s.tutorialPlayControlUsed = OnboardingTutorial.Instance != null && OnboardingTutorial.Instance.TutorialPlayUsedForSave;
        s.tutorialFastForwardControlUsed = OnboardingTutorial.Instance != null && OnboardingTutorial.Instance.TutorialFastForwardUsedForSave;
        s.tutorialShiftStarted = OnboardingTutorial.Instance != null && OnboardingTutorial.Instance.TutorialShiftStartedForSave;
        s.tutorialFastForwardRateMask = OnboardingTutorial.Instance != null ? OnboardingTutorial.Instance.TutorialFastForwardRateMaskForSave : 0;
        s.tutorialAnalyticsStage = OnboardingTutorial.Instance != null ? OnboardingTutorial.Instance.TutorialAnalyticsStageForSave : -1;
        s.tutorialBaselineRate = OnboardingTutorial.Instance != null ? OnboardingTutorial.Instance.TutorialBaselineRateForSave : 0f;
        s.tutorialTargetRate = OnboardingTutorial.Instance != null ? OnboardingTutorial.Instance.TutorialTargetRateForSave : 0f;
        s.tutorialStationMoved = OnboardingTutorial.Instance != null && OnboardingTutorial.Instance.TutorialStationMovedForSave;
        s.milestoneOneBottleneckLessonDone = OnboardingTutorial.Instance != null
            && OnboardingTutorial.Instance.MilestoneOneBottleneckLessonDoneForSave;
        s.milestoneOneAnalyticsLessonDone = OnboardingTutorial.Instance != null
            && OnboardingTutorial.Instance.MilestoneOneAnalyticsLessonDoneForSave;
        s.milestone = MilestoneProgressManager.Instance != null ? MilestoneProgressManager.Instance.GetHighestReachedNumberedStage() : 0;
        if (MilestoneProgressManager.Instance != null)
        {
            s.activeMilestoneId = MilestoneProgressManager.Instance.ActiveMilestoneId;
            s.activeMilestoneShiftObjectives = MilestoneProgressManager.Instance.ActiveShiftObjectives;
            s.completedMilestoneIds.AddRange(MilestoneProgressManager.Instance.GetCompletedMilestoneIds());
        }
        if (MissionProgressManager.Instance != null)
            s.missionProgress = MissionProgressManager.Instance.CaptureProgress();
        var statistics = StoreStatisticsManager.Instance;
        if (statistics != null)
        {
            s.dailyCompletedOrders = statistics.OrdersCompletedToday;
            s.dailyRevenue = statistics.RevenueToday;
            s.dailyWaitTotalSeconds = statistics.WaitTotalTodaySeconds;
            s.dailyMealsWasted = statistics.MealsWastedToday;
            s.dailyCustomersLost = statistics.CustomersLostToday;
            s.dailyCustomersVisited = statistics.CustomersVisitedToday;
            s.dailyMoneyAtStart = statistics.MoneyAtDayStart;
            s.dailyTrackingStarted = statistics.DayTrackingStarted;
            s.dailyVisitBuckets = statistics.GetVisitTrendBuckets();
            s.careerDailyTasksCompleted = statistics.CareerDailyTasksCompleted;
            s.dailyRevenueTaskAwarded = statistics.DailyRevenueTaskAwarded;
            s.dailyWaitTaskAwarded = statistics.DailyWaitTaskAwarded;
            s.careerOrdersServed = statistics.CareerOrdersServed;
            s.careerCustomersVisited = statistics.CareerCustomersVisited;
            s.careerCustomersLost = statistics.CareerCustomersLost;
            s.careerShiftsCompleted = statistics.CareerShiftsCompleted;
            s.careerRevenue = statistics.RevenueEarned;
            s.careerMealsWasted = statistics.MealsWasted;
            s.careerBestShiftRevenue = statistics.CareerBestShiftRevenue;
            s.careerBestShiftOrders = statistics.CareerBestShiftOrders;
            s.careerLastCompletedDay = statistics.CareerLastCompletedDay;
            s.careerWaitTotalSeconds = statistics.CareerWaitTotalSeconds;
        }
        if (ResearchProgressManager.Instance != null)
        {
            s.completedResearchIds = ResearchProgressManager.Instance.CaptureCompleted();
            s.activeResearchId = ResearchProgressManager.Instance.ActiveId;
            s.activeResearchMinutesRemaining = ResearchProgressManager.Instance.RemainingMinutes;
        }
        ProductionManager productionForMenu = ProductionManager.Instance;
        if (productionForMenu != null && productionForMenu.orderConfig != null)
        {
            s.enabledMenuItemNames.AddRange(productionForMenu.orderConfig.CaptureEnabledMenuItemNames());
            s.itemReputation.AddRange(productionForMenu.orderConfig.CaptureItemReputation());
        }
        s.customers = CustomerAI.CaptureAllForSave();
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
                            || HasMatchingStationComponent(go, item.prefab);
                    if (!match) continue;
                    objects.Add(go);
                    var grill = go.GetComponent<GrillStation>();
                    var assembly = go.GetComponent<AssemblyStation>();
                    var freezer = go.GetComponent<FreezerStation>();
                    var pantry = go.GetComponent<PantryStation>();
                    var cutting = go.GetComponent<CuttingStation>();
                    var fryer = go.GetComponent<FryerStation>();
                    var stationNode = go.GetComponent<StationNode>();
                    if (stationNode == null
                        && (go.GetComponent<IStationBuffer>() != null || go.GetComponent<Register>() != null))
                        stationNode = StationNode.EnsureOn(go);
                    StationRuntimeMetrics stationMetrics = stationNode != null
                        ? StationRuntimeMetrics.EnsureOn(go) : null;
                    // Persist the station's resolved assignment, not just its backing
                    // field. Several station types normalize or derive their active
                    // recipe, so the raw field can be null or stale while the UI and
                    // production system are using a valid selection.
                    AssemblyRecipeDefinition assemblyRecipe = assembly != null ? assembly.GetSelectedRecipe() : null;
                    CuttingRecipeDefinition cuttingRecipe = cutting != null ? cutting.GetSelectedRecipe() : null;
                    ItemDefinition product = assembly != null
                        ? (assemblyRecipe != null ? assemblyRecipe.output : assembly.selectedProduct)
                        : grill != null ? grill.GetSelectedOutput()
                        : fryer != null ? fryer.GetSelectedOutput()
                        : freezer != null ? freezer.selectedItem
                        : pantry != null ? pantry.selectedItem
                        : cutting != null ? (cuttingRecipe != null ? cuttingRecipe.output : cutting.selectedProduct)
                        : null;
                    s.equipment.Add(new Equipment { item=item.name, product=product != null ? product.name : "", position=candidate.position, rotation=candidate.rotation, scale=candidate.lossyScale,
                        registerEnabled=true,
                        slot=mounted != null ? mounted.slotIndex : groundPickup != null ? groundPickup.slotIndex : -1,
                        counterPosition=mounted != null && mounted.surface != null ? mounted.surface.transform.position
                            : groundPickup != null && groundPickup.surface != null ? groundPickup.surface.transform.position : Vector3.zero,
                        processedInputs=assembly != null ? assembly.BufferedProcessedInputCount : 0,
                        pantryInputs=assembly != null ? assembly.BufferedPantryInputCount : 0,
                        thirdInputs=assembly != null ? assembly.BufferedThirdInputCount : 0,
                        bufferedOutputs=assembly != null ? assembly.BufferedOutputCount : 0,
                        processingUnits=grill != null ? grill.BufferedPattyCount - grill.SecondaryInputUnitCount : 0,
                        secondaryInputs=grill != null ? grill.SecondaryInputUnitCount : 0,
                        processProgress=grill != null ? grill.CookProgressSeconds : 0f,
                        assemblyProcessingUnits=assembly != null ? assembly.ProcessingUnitCount : 0,
                        assemblyProcessProgress=assembly != null ? assembly.ProcessingProgressSeconds : 0f,
                        productionRatePercent=stationNode != null ? stationNode.ProductionRatePercent : 100f,
                        stationGuid=stationNode != null ? stationNode.StableStationId : "",
                        hasStationMetrics=stationMetrics != null,
                        stationWorkingSeconds=stationMetrics != null ? stationMetrics.WorkingSeconds : 0f,
                        stationStarvedSeconds=stationMetrics != null ? stationMetrics.StarvedSeconds : 0f,
                        stationBlockedSeconds=stationMetrics != null ? stationMetrics.BlockedSeconds : 0f,
                        stationIdleSeconds=stationMetrics != null ? stationMetrics.IdleSeconds : 0f,
                        stationCurrentState=stationMetrics != null ? (int)stationMetrics.CurrentState : 0,
                        stationCurrentStateSeconds=stationMetrics != null ? stationMetrics.CurrentStateSeconds : 0f,
                        productionState=scheduleManager != null && scheduleManager.useScheduledProduction
                            ? scheduleManager.ScheduledProduction.CaptureStation(go) : null });
                }
            }
            for (int i=0;i<objects.Count;i++) { var node=objects[i].GetComponent<StationNode>(); if(node!=null) s.equipment[i].output=objects.IndexOf(node.outputTarget); }
        }
        var kitchen=KitchenInventory.Instance;
        if(kitchen!=null) foreach(var entry in kitchen.stock) if(entry.item!=null) s.ingredients.Add(new Stock
        {
            item = entry.item.name,
            count = entry.quantity,
            supplyRatePerMinute = entry.supplyRatePerMinute,
            supplyUnitProgress = entry.supplyUnitProgress,
            supplyCostRemainder = entry.supplyCostRemainder
        });
        IngredientDeliveryService.Instance?.CaptureSaveState(s.ingredientShipments);
        var pm=ProductionManager.Instance;
        if(pm!=null) {
            s.totalWorkersHired = Mathf.Max(pm.totalWorkersHired, pm.HiredWorkerCount);
            var staff=new List<KitchenEmployee>();
            foreach(var employee in pm.employees) if(employee!=null) {
                staff.Add(employee);
                PartyCharacterRandomizer look = employee.GetComponent<PartyCharacterRandomizer>();
                var w=new Worker {name=employee.employeeName,position=employee.transform.position,level=0,priority=0,
                    productionState=pm.useScheduledProduction ? pm.ScheduledProduction.CaptureWorker(employee) : null,
                    bodyIndex=look != null ? look.bodyIndex : 0,
                    faceIndex=look != null ? look.faceIndex : 0,
                    hatIndex=look != null ? look.hatIndex : -1};
                foreach(var station in employee.operatedStations)
                {
                    w.stations.Add(objects.IndexOf(station));
                    StationNode node = station != null ? StationNode.EnsureOn(station) : null;
                    w.stationGuids.Add(node != null ? node.StableStationId : "");
                }
                s.workers.Add(w);
            }
            foreach(var flow in pm.productionFlows) if(flow!=null) {
                flow.EnsureLegacyConnections();
                var f=new Flow {name=flow.flowName,kind=flow.kind,steps=new List<string>(flow.stepIds),graphInitialized=flow.graphInitialized,autoName=flow.autoName};
                foreach(var station in flow.stations)
                {
                    f.stations.Add(objects.IndexOf(station));
                    StationNode node = station != null ? StationNode.EnsureOn(station) : null;
                    f.stationGuids.Add(node != null ? node.StableStationId : "");
                }
                foreach(var worker in flow.workers) f.workers.Add(staff.IndexOf(worker));
                foreach(var edge in flow.connections) if(edge!=null)
                {
                    StationNode fromNode = edge.from != null ? StationNode.EnsureOn(edge.from) : null;
                    StationNode toNode = edge.to != null ? StationNode.EnsureOn(edge.to) : null;
                    f.edges.Add(new FlowEdge
                    {
                        from=objects.IndexOf(edge.from),
                        to=objects.IndexOf(edge.to),
                        fromGuid=fromNode != null ? fromNode.StableStationId : "",
                        toGuid=toNode != null ? toNode.StableStationId : ""
                    });
                }
                s.flows.Add(f);
            }
        }
        return s;
    }

    public bool Restore()
    {
        var inv=UnityEngine.Object.FindFirstObjectByType<InventoryManager>();
        var pm=ProductionManager.Instance;
        if(version<1 || version>CurrentVersion || inv==null || pm==null) return false;
        CustomerSpawner customerSpawner = CustomerSpawner.Instance != null
            ? CustomerSpawner.Instance : UnityEngine.Object.FindFirstObjectByType<CustomerSpawner>();
        if (customers != null && customers.Count > 0
            && (customerSpawner == null || customerSpawner.customerPrefab == null
                || customerSpawner.customerPrefab.GetComponent<CustomerAI>() == null)) return false;
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
        if (version < 32)
        {
            Transform[] sceneTransforms = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            equipment.RemoveAll(entry => IsLegacyAncestorEquipment(entry, definitions, sceneTransforms));
        }
        foreach(var e in equipment) if(!definitions.ContainsKey(e.item) || definitions[e.item].prefab==null) return false;
        foreach(var e in equipment) if(!string.IsNullOrEmpty(e.pantrySecondProduct) && !definitions.ContainsKey(e.pantrySecondProduct)) return false;
        if(workers != null && workers.Count > 0
            && (pm.employeePrefab == null || pm.employeePrefab.GetComponent<KitchenEmployee>() == null)) return false;
        if (customers != null)
            foreach (CustomerAI.SavedState customer in customers)
                if (customer != null && (!AreSavedOrderItemsAvailable(customer.order, definitions)
                    || !AreSavedOrderItemsAvailable(customer.originalOrder, definitions)
                    || !AreSavedDeliveredItemsAvailable(customer.deliveredItems, definitions))) return false;
        customerSpawner?.ResetArrivalScheduleForSaveRestore();
        CustomerAI.DespawnAllCustomers();
        foreach(var employee in new List<KitchenEmployee>(pm.employees)) if(employee!=null) { employee.AbortCurrentWork(); employee.ClearAllOperatedStations(); employee.gameObject.SetActive(false); UnityEngine.Object.Destroy(employee.gameObject); }
        pm.ResetTransientProductionState();
        pm.employees.Clear(); pm.productionFlows.Clear();
        pm.totalWorkersHired = 0;
        foreach(var candidate in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)) {
            if (candidate.GetComponent<CustomerWallDoor>() != null || candidate.GetComponentInParent<CustomerWallDoor>() != null)
                continue;
            var placed=candidate.GetComponent<PlacedBuildItem>(); var mounted=candidate.GetComponent<CounterMountedItem>();
            bool match=placed!=null || mounted!=null;
            // Only classify this GameObject as a station when the station
            // component is attached directly to it. GetStationId searches
            // inactive descendants, which made scene roots such as Environment
            // look like stations when they contained inactive register models.
            if (!match) match = HasStationComponent(candidate.gameObject);
            if(!match) foreach(var item in inv.allItems) if(item!=null && item.prefab!=null && candidate.name.Replace("(Clone)","").Trim()==item.prefab.name) {match=true;break;}
            if(!match) continue;
            if(mounted!=null && mounted.surface!=null) mounted.surface.Release(mounted);
            candidate.GetComponent<GroundPickupStationPlacement>()?.ReleaseSlot();
            candidate.gameObject.SetActive(false); UnityEngine.Object.Destroy(candidate.gameObject);
        }
        var grid=GridManager.Instance;
        if (grid != null)
        {
            grid.ResyncOccupancyFromScene();
            if (width > 0 && height > 0)
            {
                int shrinkWidth = Mathf.Max(0, grid.Width - width);
                int shrinkHeight = Mathf.Max(0, grid.Height - height);
                if ((shrinkWidth > 0 || shrinkHeight > 0)
                    && !grid.TryShrink(shrinkWidth, shrinkHeight))
                    Debug.LogWarning("Save restore could not shrink the kitchen grid to its saved size.");

                grid.TryExpand(Mathf.Max(0, width - grid.Width), Mathf.Max(0, height - grid.Height));
            }
        }
        var objects=new List<GameObject>();
        foreach(var e in equipment) {
            var item=definitions[e.item]; var go=UnityEngine.Object.Instantiate(item.prefab,e.position,e.rotation);
            // Use current authored dimensions for tiered stations and Pantry.
            // Older saves captured Pantry at its previous two-tile placement scale.
            go.transform.localScale=item.IsTieredStation && item.prefab!=null
                ? item.prefab.transform.localScale
                : item.itemName=="Pantry" ? item.placementScale : e.scale;
            BuildPlacer.ConfigurePlacedObject(go,item);
            var placed=go.GetComponent<PlacedBuildItem>() ?? go.AddComponent<PlacedBuildItem>(); placed.itemDefinition=item;
            objects.Add(go);
        }
        // Recipe lookup is milestone-filtered. Restore progression before applying
        // station selections, otherwise valid MK1/MK2 assembly recipes temporarily
        // resolve as locked and flow balancing replaces them with Hamburger.
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
        for(int i=0;i<equipment.Count;i++) {
            var e=equipment[i]; var go=objects[i]; var item=definitions[e.item];
            ItemDefinition product = !string.IsNullOrEmpty(e.product) && definitions.TryGetValue(e.product,out var savedProduct) ? savedProduct : null;
            // Always restore configurable stations, including an intentionally empty
            // assignment. Skipping null previously left whatever default happened to
            // be serialized on the prefab, which made load results station-dependent.
            var grill=go.GetComponent<GrillStation>(); if(grill!=null) grill.RestoreBufferedState(product,e.processingUnits,e.processProgress,e.secondaryInputs);
            if (pm.useScheduledProduction && e.productionState != null) pm.ScheduledProduction.RestoreStation(go,e.productionState);
            var assembly=go.GetComponent<AssemblyStation>(); if(assembly!=null) assembly.RestoreBufferedState(product,e.processedInputs,e.pantryInputs,e.thirdInputs,e.bufferedOutputs,e.assemblyProcessingUnits,e.assemblyProcessProgress);
            var fryer=go.GetComponent<FryerStation>(); if(fryer!=null) fryer.SetRecipeOutput(product);
            var freezer=go.GetComponent<FreezerStation>(); if(freezer!=null) freezer.SetStoredItem(product);
            var pantry=go.GetComponent<PantryStation>(); if(pantry!=null) {
                ItemDefinition second=null;
                if(!string.IsNullOrEmpty(e.pantrySecondProduct)) definitions.TryGetValue(e.pantrySecondProduct,out second);
                pantry.SetStoredItems(product,second);
            }
            var cutting=go.GetComponent<CuttingStation>(); if(cutting!=null) cutting.RestoreSelectedProduct(product);
            StationNode restoredStationNode = StationNode.EnsureOn(go);
            if (restoredStationNode != null)
            {
                restoredStationNode.RestoreStableStationId(e.stationGuid);
                restoredStationNode.SetProductionRatePercent(e.productionRatePercent > 0f
                    ? e.productionRatePercent : 100f);
            }
            if (version >= 33 && e.hasStationMetrics && restoredStationNode != null)
                StationRuntimeMetrics.EnsureOn(go).RestoreCumulativeState(
                    e.stationWorkingSeconds, e.stationStarvedSeconds, e.stationBlockedSeconds,
                    e.stationIdleSeconds, e.stationCurrentState, e.stationCurrentStateSeconds);
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
        var stationObjectsByGuid = new Dictionary<string, GameObject>(System.StringComparer.Ordinal);
        foreach (GameObject stationObject in objects)
        {
            StationNode node = stationObject != null ? stationObject.GetComponent<StationNode>() : null;
            if (node == null) continue;
            string guid = node.StableStationId;
            if (!string.IsNullOrEmpty(guid) && !stationObjectsByGuid.ContainsKey(guid))
                stationObjectsByGuid.Add(guid, stationObject);
        }
        foreach(var entry in inventory) if(definitions.TryGetValue(entry.item,out var item)) inv.RestoreCounts(item,entry.count,entry.acquired);
        if(KitchenInventory.Instance!=null) {
            KitchenInventory.Instance.ClearAllStock();
            KitchenInventory.Instance.ClearAllSupplySettings();
            foreach(var entry in ingredients) if(definitions.TryGetValue(entry.item,out var item)) {
                KitchenInventory.Instance.AddStock(item,entry.count);
                KitchenInventory.Instance.RestoreSupplySettings(item, entry.supplyRatePerMinute,
                    entry.supplyUnitProgress, entry.supplyCostRemainder);
            }
        }
        IngredientDeliveryService.Instance?.RestoreSaveState(ingredientShipments, definitions);
        var staff=new List<KitchenEmployee>();
        if (workers == null) workers = new List<Worker>();
        foreach(var w in workers) {
            if (w == null) { staff.Add(null); continue; }
            var go=UnityEngine.Object.Instantiate(pm.employeePrefab,w.position,Quaternion.identity); var employee=go.GetComponent<KitchenEmployee>();
            if (employee == null)
            {
                UnityEngine.Object.Destroy(go);
                staff.Add(null);
                continue;
            }
            PartyCharacterRandomizer look = PartyCharacterRandomizer.EnsureOn(go);
            if (look != null)
            {
                // Prevent the worker prefab's startup randomizer from replacing
                // the appearance restored from the save on the next frame.
                look.randomizeOnStart = false;
                look.bodyIndex = w.bodyIndex;
                look.faceIndex = w.faceIndex;
                look.hatIndex = w.hatIndex;
                look.ApplyCurrent();
            }
            employee.employeeName=w.name; pm.RegisterEmployee(employee); staff.Add(employee);
            if (pm.useScheduledProduction && w.productionState != null) pm.ScheduledProduction.RestoreWorker(employee,w.productionState);
            var restoredAssignments = new HashSet<GameObject>();
            int assignmentCount = Mathf.Max(w.stations != null ? w.stations.Count : 0,
                version >= 22 && w.stationGuids != null ? w.stationGuids.Count : 0);
            for (int i = 0; i < assignmentCount; i++)
            {
                GameObject stationObject = null;
                if (version >= 22 && w.stationGuids != null && i < w.stationGuids.Count
                    && !string.IsNullOrEmpty(w.stationGuids[i]))
                    stationObjectsByGuid.TryGetValue(w.stationGuids[i], out stationObject);
                if (stationObject == null && w.stations != null && i < w.stations.Count
                    && w.stations[i] >= 0 && w.stations[i] < objects.Count)
                    stationObject = objects[w.stations[i]];
                if (stationObject == null || !restoredAssignments.Add(stationObject)) continue;
                employee.AddOperatedStation(stationObject);
                StationNode.EnsureOn(stationObject).AddWorker(employee);
            }
            employee.SyncFromOperatedStations();
        }
        pm.totalWorkersHired = Mathf.Max(totalWorkersHired, staff.Count);
        if (flows == null) flows = new List<Flow>();
        foreach(var f in flows) {
            if (f == null) continue;
            var flow=new ProductionFlowPlan {flowName=f.name,autoName=version>=34 ? f.autoName : ProductionFlowPlan.IsDefaultGeneratedName(f.name),kind=f.kind,stepIds=f.steps,graphInitialized=version>=6 && f.graphInitialized};
            int stationCount = Mathf.Max(f.stations != null ? f.stations.Count : 0,
                f.stationGuids != null ? f.stationGuids.Count : 0);
            for (int i = 0; i < stationCount; i++)
            {
                GameObject stationObject = null;
                if (f.stationGuids != null && i < f.stationGuids.Count
                    && !string.IsNullOrEmpty(f.stationGuids[i]))
                    stationObjectsByGuid.TryGetValue(f.stationGuids[i], out stationObject);
                if (stationObject == null && f.stations != null && i < f.stations.Count
                    && f.stations[i] >= 0 && f.stations[i] < objects.Count)
                    stationObject = objects[f.stations[i]];
                if (stationObject != null && !flow.stations.Contains(stationObject))
                    flow.stations.Add(stationObject);
            }
            if(f.edges!=null) foreach(var edge in f.edges)
            {
                if (edge == null) continue;
                GameObject from = null;
                GameObject to = null;
                if (!string.IsNullOrEmpty(edge.fromGuid))
                    stationObjectsByGuid.TryGetValue(edge.fromGuid, out from);
                if (!string.IsNullOrEmpty(edge.toGuid))
                    stationObjectsByGuid.TryGetValue(edge.toGuid, out to);
                if (from == null && edge.from >= 0 && edge.from < objects.Count)
                    from = objects[edge.from];
                if (to == null && edge.to >= 0 && edge.to < objects.Count)
                    to = objects[edge.to];
                if (from != null && to != null)
                    flow.connections.Add(new ProductionFlowConnection(from, to));
            }
            flow.EnsureLegacyConnections();
            if (f.workers != null)
                foreach(int i in f.workers)
                    if(i>=0 && i<staff.Count && staff[i] != null && !flow.workers.Contains(staff[i]))
                        flow.workers.Add(staff[i]);
            pm.productionFlows.Add(flow);
        }
        pm.RefreshAutomaticProductionFlowNames();
        if (customers != null && customerSpawner != null)
            customerSpawner.RestoreSavedCustomers(customers, definitions, stationObjectsByGuid);
        // Version 1 checkpoints included the removed door lesson at index 2.
        int restoredTutorialStep = version == 1 && !tutorialComplete && tutorialStep > 2
            ? tutorialStep - 1
            : tutorialStep;
        if (version < 28 && !tutorialComplete)
        {
            if (restoredTutorialStep >= 3 && restoredTutorialStep <= 8)
                restoredTutorialStep = 2;
            else if (restoredTutorialStep >= 9)
                restoredTutorialStep -= 5;
        }
        // Version 29 folds the separate fryer and pickup placement lessons into
        // the station checklist at step 2, removing the two old tutorial steps.
        if (version < 29 && !tutorialComplete)
        {
            if (restoredTutorialStep >= 3 && restoredTutorialStep <= 4)
                restoredTutorialStep = 2;
            else if (restoredTutorialStep >= 5)
                restoredTutorialStep -= 2;
        }
        // Version 30 introduces a persistent Plan tutorial lesson and moves
        // menu selection before Supply. Revisit the plan/menu/supply section
        // for saves already in that section; keep later lessons aligned.
        if (version < 30 && !tutorialComplete)
        {
            if (restoredTutorialStep >= 6 && restoredTutorialStep <= 9)
                restoredTutorialStep = 6;
            else if (restoredTutorialStep >= 10)
                restoredTutorialStep++;
        }
        // Version 37 adds an Objectives walkthrough after the Plan lesson.
        // Keep incomplete tutorials past Plan aligned with their previous step.
        if (version < 37 && !tutorialComplete && restoredTutorialStep >= 7)
            restoredTutorialStep++;
        // Version 38 separates the shift-start lesson from the time-controls
        // checklist, moving the old Analytics lesson from step 16 to 17.
        if (version < 38 && !tutorialComplete && restoredTutorialStep >= 16)
            restoredTutorialStep++;
        // Version 39 moves the Objectives walkthrough to just before shift start.
        // Convert checkpoints from the previous ordering after earlier migrations.
        if (version < 39 && !tutorialComplete)
        {
            if (restoredTutorialStep == 7)
                restoredTutorialStep = 14;
            else if (restoredTutorialStep >= 8 && restoredTutorialStep <= 14)
                restoredTutorialStep--;
        }
        // The short-play tutorial adds a required movement card and changes its
        // checkpoint indices. Restart incomplete tutorials at the welcome card.
        if (version < 43 && !tutorialComplete)
            restoredTutorialStep = 0;
        // Version 44 separates observing production from following a customer.
        // Move old layout-improvement checkpoints past the added customer step.
        if (version < 44 && !tutorialComplete && restoredTutorialStep >= 4)
            restoredTutorialStep++;
        // Version 35 makes milestone objectives shift-scoped and changes recipe
        // counts from orders served to items delivered at Pickup. Old campaign
        // progress cannot be converted reliably, so keep tutorial tasks but reset
        // old numbered-milestone progress.
        if (MissionProgressManager.Instance != null)
        {
            if (version >= 35 || !tutorialComplete)
                MissionProgressManager.Instance.RestoreProgress(missionProgress);
            else
                MissionProgressManager.Instance.ResetProgress();
        }
        // Older saves may already have finished onboarding before the tutorial
        // milestone had its dedicated completion task.
        if (tutorialComplete && milestoneProgress != null
            && milestoneProgress.ActiveMilestoneId == "tutorial")
            TutorialVoiceEvents.Raise(TutorialVoiceEventId.TutorialCompleted);
        ResearchProgressManager research = ResearchProgressManager.Ensure();
        if (research != null)
            research.Restore(
                version >= 13 ? completedResearchIds : null,
                version >= 13 ? activeResearchId : null,
                version >= 13 ? activeResearchMinutesRemaining : 0f);
        // Recipe availability depends on both milestone and research state.
        // Balance only after restoring both so connected recipes can be resolved.
        foreach (var flow in pm.productionFlows)
            if (flow != null)
                WorkerFlowAssigner.ApplyBalancedTeam(flow);
        pm.NotifyProductionStateChanged(0f);
        if (pm.useScheduledProduction)
            pm.ScheduledProduction.RequestConfigurationCheck();
        if (pm.orderConfig != null)
        {
            pm.orderConfig.RestoreEnabledMenuItems(enabledMenuItemNames);
            pm.orderConfig.RestoreItemReputation(version >= 17 ? itemReputation : null);
        }
        if(grid!=null) grid.ResyncOccupancyFromScene();
        var appearance = StoreAppearanceController.Ensure();
        if (version >= 4)
            appearance.RestoreState(wallTexture, floorTexture, roofTexture, wallTint, floorTint, roofTint);
        else
            appearance.RestoreState(0, 0, 0, Color.white, Color.white, Color.white);
        var money=UnityEngine.Object.FindFirstObjectByType<MoneyManager>(); if(money!=null) money.SetMoney(cash);
        ShiftPlanningUI.RestoreSaveState(version >= 23 ? shiftPlanningState : null,
            hasIdleStationBaselines: version >= 35);
        StoreStatisticsManager statistics = StoreStatisticsManager.Instance;
        if (statistics != null)
        {
            if (version >= 18)
                statistics.RestoreDailyTaskProgress(dailyCompletedOrders, dailyRevenue, dailyWaitTotalSeconds,
                    version >= 26 ? careerDailyTasksCompleted : 0,
                    version >= 26 && dailyRevenueTaskAwarded,
                    version >= 26 && dailyWaitTaskAwarded,
                    version >= 33 ? dailyMealsWasted : 0,
                    version >= 33 ? dailyCustomersLost : 0,
                    version >= 33 ? dailyCustomersVisited : 0,
                    version >= 33 ? dailyMoneyAtStart : cash,
                    version >= 33 ? dailyVisitBuckets : null,
                    version < 33 || dailyTrackingStarted);
            else
                statistics.BeginDay();

            if (version >= 24)
                statistics.RestoreCareerStatistics(careerOrdersServed, careerCustomersVisited,
                    careerCustomersLost, careerShiftsCompleted, careerRevenue, careerMealsWasted,
                    careerBestShiftRevenue, careerBestShiftOrders, careerLastCompletedDay,
                    version >= 25 ? careerWaitTotalSeconds : 0f);
            else
                statistics.ResetCareerStatistics();
        }
        milestoneProgress?.RestoreActiveShiftObjectives(version >= 35 && activeMilestoneShiftObjectives);
        // Restoring a checkpoint at the shift end raises OnDayEnded immediately.
        // Apply all saved report values first so the EOD panel reads the checkpoint,
        // not whatever statistics happened to be in memory before this load.
        if(GameTimeManager.Instance!=null) GameTimeManager.Instance.RestoreCheckpoint(day,minutes);
        PurchaseUndoManager.Instance?.ClearHistory();
        var placer = UnityEngine.Object.FindFirstObjectByType<BuildPlacer>();
        if (placer != null)
            placer.EnsureCustomerEntrance();
        else
            UnityEngine.Object.FindFirstObjectByType<KitchenPerimeterWalls>()?.RequestRefresh();
        KitchenPerimeterWalls perimeter = UnityEngine.Object.FindFirstObjectByType<KitchenPerimeterWalls>();
        if (perimeter != null)
        {
            perimeter.generateAtRuntime = true;
            perimeter.FitToGrid();
        }
        StoreAppearanceController.Instance?.RefreshNow();
        if (wallPhotos != null)
            foreach (WallPhotoDrag photo in UnityEngine.Object.FindObjectsByType<WallPhotoDrag>(FindObjectsSortMode.None))
                foreach (WallPhoto saved in wallPhotos)
                    if (saved != null && saved.name == photo.gameObject.name) photo.SetPlacementPosition(saved.position);
        // Apply tutorial state only after the full checkpoint is restored. An
        // incomplete tutorial can start as soon as gameplay is activated, and its
        // setup must not run while stations, research, missions, or customers are
        // still being rebuilt from this save.
        if (OnboardingTutorial.Instance != null)
            OnboardingTutorial.Instance.RestoreCheckpoint(tutorialComplete, restoredTutorialStep,
                version >= 43 && tutorialOrderServed,
                version >= 36 && tutorialPauseControlUsed,
                version >= 36 && tutorialPlayControlUsed,
                version >= 36 && tutorialFastForwardControlUsed,
                version >= 43 && tutorialShiftStarted,
                version >= 40 ? tutorialFastForwardRateMask : 0,
                version >= 40 ? tutorialAnalyticsStage : -1,
                version >= 41 && restoredTutorialStep > 0,
                version >= 41 ? tutorialBaselineRate : 0f,
                version >= 41 ? tutorialTargetRate : 0f,
                version >= 41 && tutorialStationMoved,
                version >= 45 && milestoneOneBottleneckLessonDone,
                version >= 45 && milestoneOneAnalyticsLessonDone);
        return true;
    }

    static bool HasStationComponent(GameObject candidate)
    {
        if (candidate == null) return false;
        foreach (FlowStationDef station in WorkerFlowAssigner.Catalog)
        {
            if (station != null && station.componentType != null
                && candidate.GetComponent(station.componentType) != null)
                return true;
        }
        return false;
    }

    static bool HasMatchingStationComponent(GameObject candidate, GameObject prefab)
    {
        if (candidate == null || prefab == null) return false;
        foreach (FlowStationDef station in WorkerFlowAssigner.Catalog)
        {
            if (station == null || station.componentType == null
                || candidate.GetComponent(station.componentType) == null)
                continue;
            if (prefab.GetComponent(station.componentType) != null
                || prefab.GetComponentInChildren(station.componentType, true) != null)
                return true;
        }
        return false;
    }

    static bool IsLegacyAncestorEquipment(Equipment entry,
        Dictionary<string, ItemDefinition> definitions, Transform[] sceneTransforms)
    {
        if (entry == null || definitions == null || sceneTransforms == null
            || !definitions.TryGetValue(entry.item, out ItemDefinition item)
            || item == null || item.prefab == null)
            return false;

        string itemStationId = WorkerFlowAssigner.GetStationId(item.prefab);
        if (string.IsNullOrEmpty(itemStationId)) return false;

        for (int i = 0; i < sceneTransforms.Length; i++)
        {
            Transform candidate = sceneTransforms[i];
            if (candidate == null) continue;
            GameObject candidateObject = candidate.gameObject;
            if (candidateObject.GetComponent<PlacedBuildItem>() != null
                || candidateObject.GetComponent<CounterMountedItem>() != null
                || candidateObject.GetComponentInChildren<PlacedBuildItem>(true) != null
                || candidateObject.GetComponentInChildren<CounterMountedItem>(true) != null
                || HasStationComponent(candidateObject))
                continue;

            string normalizedName = candidateObject.name.Replace("(Clone)", "").Trim();
            if (normalizedName == item.prefab.name) continue;
            if (WorkerFlowAssigner.GetStationId(candidateObject) != itemStationId) continue;
            if ((candidate.position - entry.position).sqrMagnitude > 0.0001f
                || Quaternion.Angle(candidate.rotation, entry.rotation) > 0.1f
                || (candidate.lossyScale - entry.scale).sqrMagnitude > 0.0001f)
                continue;

            // Older capture logic mistook a station-containing scene ancestor
            // for a placed station and saved the ancestor's transform as one.
            return true;
        }
        return false;
    }

    static bool AreSavedOrderItemsAvailable(List<CustomerAI.SavedOrderLine> lines,
        Dictionary<string, ItemDefinition> definitions)
    {
        if (lines == null) return true;
        foreach (CustomerAI.SavedOrderLine line in lines)
            if (line != null && line.quantity > 0
                && (string.IsNullOrEmpty(line.item) || !definitions.ContainsKey(line.item))) return false;
        return true;
    }

    static bool AreSavedDeliveredItemsAvailable(List<CustomerAI.SavedDeliveredItem> items,
        Dictionary<string, ItemDefinition> definitions)
    {
        if (items == null) return true;
        foreach (CustomerAI.SavedDeliveredItem item in items)
            if (item != null && (string.IsNullOrEmpty(item.item) || !definitions.ContainsKey(item.item))) return false;
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
