/// <summary>
/// Concise reference copy for the in-game guidebook. Two pages are shown at a time.
/// </summary>
public static class GuidebookPages
{
    public struct Page
    {
        public string chapter;
        public string title;
        public string body;

        public Page(string chapter, string title, string body)
        {
            this.chapter = chapter;
            this.title = title;
            this.body = body;
        }
    }

    public static readonly Page[] All =
    {
        new Page(
            "Start Here",
            "Find the right help",
            "Click <b>Gus</b> once to enter button help, then click any control to hear what it does. The help click will not activate that control. Click Gus again or choose <b>Done</b> to exit.\n\n" +
            "The <b>Guidebook</b> is the reference for rules, rates, and examples. Open it with the HUD information icon. Use the chapter tabs, page arrows, or A and D to browse.\n\n" +
            "The <b>tutorial</b> teaches the first actions in order. Return to the Guidebook when you need a rule or want to understand the ISE idea behind it."),
        new Page(
            "Start Here",
            "Plan your first shift",
            "1. Place the stations needed for one menu item.\n" +
            "2. Enable that item and buy its ingredients in Supply.\n" +
            "3. Create a flow from its input stations through production to Pickup.\n" +
            "4. Hire a worker and assign them to the flow.\n" +
            "5. Set station target rates to match expected demand.\n" +
            "6. Run the shift and compare item demand with completed output.\n\n" +
            "Start with one product, then expand when you can see what is limiting it. Tasks and milestones introduce systems in stages and unlock research."),

        new Page(
            "Build",
            "Place stations",
            "Open <b>Inventory</b> and choose a station. After purchase, move its preview over the kitchen grid and place it on a valid highlighted footprint. Occupied or blocked tiles are invalid.\n\n" +
            "The first copy of each station family is free. MK1 and MK2 share that family. Each extra copy adds 20% of its base price to the next cost, rounded to the nearest $5. There is no station quantity cap, so plan around purchase cost and available floor space. Double-click a placed station to move it.\n\n" +
            "Leave clear walking space between stations. Workers need a usable route to each flow node."),
        new Page(
            "Build",
            "Plan the kitchen layout",
            "Place related stations close enough to keep worker routes short, while leaving clear aisles and room to serve customers. A larger floor gives more placement space, but can also increase travel.\n\n" +
            "A useful layout makes the work easy to follow: ingredients enter the process, stations transform them, and finished items reach Pickup. Avoid making workers cross busy paths when you can arrange a direct route.\n\n" +
            "<b>ISE connection:</b> Facility layout affects travel, handling effort, congestion, and the capacity of the whole system."),

        new Page(
            "Production",
            "Recipe rates",
            "Each production cycle creates one output item. The recipe defines the station's base rate, and the target control sets its planned output up to <b>100%</b> of that rate.\n\n" +
            "At <b>15 items/min</b>, a station makes one item every <b>4 seconds</b> (60 / 15). At <b>30 items/min</b>, it makes one every <b>2 seconds</b>.\n\n" +
            "A target is a pace, not a guarantee. Missing inputs, a full output buffer, or a blocked route can keep actual production below target."),
        new Page(
            "Production",
            "Balance recipe inputs",
            "A recipe can run only as fast as its required inputs arrive. Compare the total supply rate for each input with the rate the next station consumes.\n\n" +
            "For example, two grills supplying cooked patties at 10/min and 5/min can feed an assembly station targeting 15 burgers/min, if the bun branch also supplies 15/min. The 5/min grill is not the whole line's limit because the two grill outputs combine.\n\n" +
            "Use parallel stations to add capacity, then check that every required input branch keeps pace."),

        new Page(
            "Workers",
            "Create a flow",
            "A flow connects the stations that move or transform items. Create one from the Workers tab, select stations in work order, and finish at the Pickup Station for finished products.\n\n" +
            "Add branches when a recipe needs parallel inputs, such as a pantry feeding buns to assembly while a grill supplies cooked patties. Split or merge routes where the work actually separates or joins.\n\n" +
            "Check that every station node is reachable and connected to the right part of the recipe."),
        new Page(
            "Workers",
            "Assign and plan transport",
            "Hire a worker, then assign their card to the flow that needs labor. Workers carry up to <b>4 items per trip</b>. Training raises their transport pace through <b>60, 80, 100, and 120 items/min</b>.\n\n" +
            "Travel and station interaction still take time. Keep routes short and clear, and use the selected flow's capacity information to see whether worker coverage can support its station workload.\n\n" +
            "If the line falls short, check reachability, travel distance, and the specific input or output station before adding more workers."),

        new Page(
            "Supply",
            "Stock ingredients",
            "The menu controls which items customers can order. The Supply tab shows current stock, incoming deliveries, pack sizes, and prices. Buy ingredients used by enabled menu items. A normal shipment takes <b>1 minute</b> to arrive. Express Ordering begins delivery immediately and adds a <b>$200 fee</b>.\n\n" +
            "Plan stock to cover use during the delivery delay. Check incoming quantities before ordering again so you do not spend cash on stock already on the way.\n\n" +
            "<b>ISE connection:</b> Inventory protects production from shortages, while excess stock ties up cash."),
        new Page(
            "Supply",
            "Automatic ordering",
            "Automatic Ordering research adds a target for each ingredient. The game places a normal order when available stock plus incoming stock falls below that target.\n\n" +
            "Choose targets that cover expected use during replenishment. Larger targets reduce the chance of a shortage, but spend cash earlier and can leave extra stock. Turn auto ordering off for ingredients you no longer need.\n\n" +
            "Check the target and incoming stock after changing your menu or production rates."),

        new Page(
            "Service",
            "Demand and customer queues",
            "Scheduled item demand uses five-item/min steps, starts from the product recipe's output at 100%, and stays steady through the shift. Customers arrive at the rate needed to express that demand.\n\n" +
            "They first queue at the <b>register</b>. Their patience timer starts after they order, then they wait in the <b>Pickup Station</b> queue for every item in that order.\n\n" +
            "Analytics separates customer orders from product units. One order can contain several items. Use customer order rates to understand service and item rates to plan production.\n\n" +
            "If the register line grows, check ordering capacity. If ordered customers wait at Pickup, compare ready food with the items their orders need."),
        new Page(
            "Service",
            "Pickup and freshness",
            "The Pickup Station holds finished items for customer collection. It has limited slots, so clear space is needed for new output. Customers wait there after ordering until their full order is ready.\n\n" +
            "Freshness changes the sale value of an item over time: up to <b>8 seconds</b> earns <b>+50%</b>, at <b>28 seconds</b> it returns to base value, and at <b>56 seconds</b> it reaches <b>-50%</b>. Items remain available until collected or discarded.\n\n" +
            "Match production to demand so food is ready when needed without sitting at Pickup too long."),

        new Page(
            "Reports",
            "Read item analytics",
            "In the by-item section, compare these rates: <b>Target / Min</b> is scheduled demand, <b>Items Ordered / Min</b> is accepted customer demand, and <b>Produced / Min</b> counts item units delivered into Pickup Stations during the last 60 seconds.\n\n" +
            "Open Demand shows requests still waiting to be accepted. Ready and Cooking are current item counts. Use the same product row when comparing demand with production.\n\n" +
            "A sustained gap between demand and completed output means customers may wait or leave. A large output surplus can build stock and increase waste."),
        new Page(
            "Reports",
            "Understand the shift grade",
            "The end-of-day grade combines five outcomes: orders served, revenue, service rate, average wait, and food waste. The summary shows points for each category and an operations diagnosis.\n\n" +
            "Use the lowest scoring category to choose what to improve next. For example, reduce waits by finding where queues form, or reduce waste by bringing production closer to demand.\n\n" +
            "Change one part of the system, then compare the next shift's measures."),

        new Page(
            "ISE Practice",
            "Match production to demand",
            "<b>Takt time</b> is the pace needed to meet demand: available seconds divided by required items. At 15 items/min, the line needs one item every 4 seconds.\n\n" +
            "Set the finished item's target, then check that recipe inputs and worker transport support that pace. If demand is 15/min, two parallel sources producing 10/min and 5/min can provide the combined 15/min input.\n\n" +
            "Use the Analytics screen to compare the plan with actual completed items. Avoid making much more than customers request."),
        new Page(
            "ISE Practice",
            "Find bottlenecks and queues",
            "A bottleneck is a constraint that limits completed output. It may be a station, missing ingredient, worker coverage, travel route, or full Pickup Station. Parallel stations can combine capacity, so do not identify the bottleneck by looking only for the lowest individual rate.\n\n" +
            "Work in process (WIP) is work waiting or being processed. A growing register queue means customers are waiting to order. A growing Pickup queue means ordered work is waiting for finished items.\n\n" +
            "Follow the queue upstream to find the constrained step, make one change, and measure again."),
        new Page(
            "ISE Practice",
            "Human factors and work design",
            "Work design considers how people interact with tasks, tools, and space. In the kitchen, clear routes, readable station placement, enough aisle space, and a sensible task sequence help workers move items consistently.\n\n" +
            "Reduce unnecessary walking and backtracking. Keep related tasks close, make flow direction easy to understand, and avoid placing equipment where it blocks another station's access.\n\n" +
            "A good design supports both capacity and reliable work. Watch worker movement and queues to see whether the layout is helping."),
        new Page(
            "ISE Practice",
            "Improve the system",
            "The restaurant is a connected system of people, equipment, materials, information, and customers. A local change can affect other parts of the line. For example, adding a station can increase output but also add travel, inventory, and handling.\n\n" +
            "Use a simple improvement cycle: define the shortfall, inspect related measures, change one cause, and compare the result. Check both service and waste so an improvement in one area does not create a new problem elsewhere."),

        new Page(
            "Troubleshooting",
            "A station is not producing",
            "Check these in order:\n" +
            "1. Is the menu item enabled and its recipe available?\n" +
            "2. Does the station have the required recipe and a target above zero?\n" +
            "3. Is there enough ingredient stock and space for input or output?\n" +
            "4. Is the station connected to the correct flow and assigned worker?\n" +
            "5. Can the worker reach the station and the next node?\n\n" +
            "Inspect the first missing input or blocked transfer. Rebuilding the whole flow can hide the original cause."),
        new Page(
            "Troubleshooting",
            "An order is waiting",
            "If customers wait at the register, inspect register service and the line ahead. If they have ordered and wait at Pickup, check every product in their order, not just one.\n\n" +
            "Confirm the required recipe is enabled, ingredients are stocked, its production route reaches Pickup, and Pickup has free slots. For a worker who is returning to the flow start, check that the first node is reachable from their work area and the route has connected nodes.\n\n" +
            "Use Analytics for demand and item output, then the Workers tab for flow coverage and route details."),
    };
}
