/// <summary>
/// Static copy for the in-game guidebook. Two pages are shown at a time.
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
            "Guidebook",
            "Welcome",
            "This book is a quick field guide for running your kitchen.\n\n" +
            "Use the arrows (or A and D) to turn the page. You always see two pages at once, like a real book.\n\n" +
            "A first-run tutorial walks you through buying stations, stocking food, hiring, and building a flow. After that, this book is the reference if you forget a screen.\n\n" +
            "Open it from the pause menu (Esc). The game stays paused while you read."),
        new Page(
            "Guidebook",
            "What's inside",
            "<b>Inventory</b> (top-left)\n" +
            "• Stations — buy and place kitchen equipment\n" +
            "• Floor — expand the kitchen grid\n\n" +
            "<b>Management</b> (top-left, or press M)\n" +
            "• Workers — hire staff, create and edit flows, assign people to those flows\n" +
            "• Food — choose what to sell and buy ingredient packs\n" +
            "• Store Stats — live numbers that show bottlenecks\n" +
            "• Customers — demand and visit trends\n\n" +
            "<b>Side panel</b>\n" +
            "• Tasks — current milestone missions\n" +
            "• Progression — tutorial chapter, then numbered milestones and quizzes\n\n" +
            "Inventory changes the layout. Management runs the layout. You need both."),
        new Page(
            "Inventory",
            "Inventory",
            "Open <b>Inventory</b> from the top-left tabs. While it is open you are in Build mode: click the grid to place equipment instead of serving.\n\n" +
            "Two tabs:\n" +
            "• <b>Stations</b> — catalog of equipment\n" +
            "• <b>Floor</b> — spend money to grow the walkable kitchen\n\n" +
            "The <b>first copy of each station type is free</b>. Extra copies cost cash.\n\n" +
            "Undo under the list reverses a buy or a floor expand. Close Inventory when you are done so you return to Play."),
        new Page(
            "Inventory",
            "Stations",
            "The Stations tab is the catalog: freezer, grill, fryer, drinks, assembly, heat lamp, pantry, and more. The register is already in the lobby — you do not buy it.\n\n" +
            "<b>How to place</b>\n" +
            "1. Buy the card (first copy is FREE).\n" +
            "2. Click the card so a ghost follows the mouse.\n" +
            "3. Click an empty kitchen tile to drop it. Rotate if the ghost shows a facing.\n\n" +
            "Place in the kitchen, not the checkered lobby. Leave aisles so workers can walk.\n\n" +
            "A station with no flow and no worker will sit idle even if it looks perfect."),
        new Page(
            "Inventory",
            "Floor",
            "The Floor tab grows the kitchen grid.\n\n" +
            "Expand when stations no longer fit or queues block walkways. The button shows current size and the next size. If it says the floor is maxed, you cannot grow further.\n\n" +
            "<b>Undo Floor</b> sits under Expand and reverses the last expand.\n\n" +
            "Extra floor does nothing by itself. Fill it with a clearer path from prep to the heat lamp."),
        new Page(
            "Kitchen",
            "What each station does",
            "<b>Register</b> — customers line up and order here.\n" +
            "<b>Freezer</b> — raw burger patties pulled from kitchen stock.\n" +
            "<b>Grill</b> — cooks those patties.\n" +
            "<b>Fryer</b> — fries; its own short path, not through the freezer.\n" +
            "<b>Drink fountain</b> — drinks; often finished at the counter, not the lamp.\n" +
            "<b>Assembly</b> — finishes burgers (bun, patty, toppings).\n" +
            "<b>Heat lamp</b> — holds finished meals until pickup. Food can expire if it sits too long.\n" +
            "<b>Pantry</b> — extra ingredients workers pull during assembly.\n\n" +
            "Burgers typically run freezer → grill → assembly → heat lamp. Fries run fryer → heat lamp."),
        new Page(
            "Management",
            "Management",
            "Open <b>Management</b> from the top-left tabs or press <b>M</b>. Time pauses so you can think, but you can still click the kitchen.\n\n" +
            "Tabs:\n" +
            "• <b>Workers</b> — hire, create/edit flows, assign people\n" +
            "• <b>Food</b> — menu toggles and ingredient packs\n" +
            "• <b>Store Stats</b> — queues, wait, throughput, waste\n" +
            "• <b>Customers</b> — visit trend and demand\n\n" +
            "Clicking a station in Manage is for inspecting that machine (rates, recipe, heat-lamp inventory). Staffing and delivery paths are owned by <b>flows</b> on the Workers tab — not by a per-station Assign Worker / Assign Output panel.\n\n" +
            "Inventory and Management cannot stay open together. Closing Management returns you to Play."),
        new Page(
            "Management",
            "Hire workers",
            "The Workers tab is where you hire. The <b>first hire is free</b>; later hires cost cash.\n\n" +
            "Hiring alone does nothing. After you hire:\n" +
            "1. Create (or select) a flow.\n" +
            "2. On that worker's card, click <b>Assign to Current Flow</b>.\n\n" +
            "One worker can cover up to <b>three</b> stations. Unassigned workers stand idle. Overloaded workers bounce between too many jobs and become the bottleneck.\n\n" +
            "Undo under the list can reverse a hire if you clicked too soon."),
        new Page(
            "Management",
            "Create a flow",
            "A <b>flow</b> is the cooking route: which stations run in which order, and where food is handed next.\n\n" +
            "1. Open Management → <b>Workers</b>.\n" +
            "2. Click <b>Create Flow</b>. The panel hides so you can see the kitchen.\n" +
            "3. Click stations <b>in cooking order</b> (example: freezer → grill → assembly → heat lamp).\n" +
            "4. Confirm when the path looks right. Esc cancels a new capture.\n\n" +
            "Make separate flows for fries or drinks if those lines should run on their own. The lines drawn in the world are the route workers will follow."),
        new Page(
            "Management",
            "Edit and assign",
            "<b>Edit Flow</b> — select a flow chip, then Edit Flow. Click a station already on the path to trim it back. Click a new station to extend it. Esc restores the previous path.\n\n" +
            "<b>Assign to Current Flow</b> — select the flow, then use that button on a worker card. The name appears on the flow. Click the name chip to unassign.\n\n" +
            "Without at least one worker on a flow, that line will not cook — even if every station is placed and stocked."),
        new Page(
            "Management",
            "Food",
            "The Food tab (called Ingredients in the tutorial) shows kitchen stock and lets you buy packs with cash. Toggle which menu items new customers may order.\n\n" +
            "Stations cannot cook what you do not have. If a flow looks assigned but nothing moves, check this list before you rebuild the layout.\n\n" +
            "Order a little ahead of the rush so the freezer and fryer do not starve. Huge unused piles are wasted money — the same idea as heat-lamp waste, just earlier in the chain.\n\n" +
            "Undo works here if you ordered the wrong pack."),
        new Page(
            "Management",
            "Store Stats",
            "Store Stats is a live report of the kitchen as a system. Use it when lines form or money stalls.\n\n" +
            "• <b>Customers in system</b> — people inside the flow (WIP). High numbers mean congestion.\n" +
            "• <b>Queue length</b> — where people wait. Long queues mark bottlenecks.\n" +
            "• <b>Wait / completion time</b> — service speed from the customer's view.\n" +
            "• <b>Throughput</b> — orders finished per minute.\n" +
            "• <b>Utilization</b> — how busy stations and workers are. 100% often means they are the limit.\n" +
            "• <b>Heat lamp / waste / revenue</b> — expired food is money lost; sales from the lamp are money earned."),
        new Page(
            "Management",
            "Reading the numbers",
            "If wait time is high and one station's queue is long, add a worker to that flow, add a matching station, or edit the flow so food actually reaches the heat lamp.\n\n" +
            "If workers are idle but customers wait, the layout, the flow order, or ingredient stock is probably wrong — not headcount.\n\n" +
            "If the heat lamp is empty, upstream stations are too slow. If it is full and meals expire, you are overproducing.\n\n" +
            "Stats will not place stations for you. They tell you <i>where</i> to look next."),
        new Page(
            "Service",
            "Customers",
            "Customers spawn at the door, walk the lobby, and queue at the <b>register</b>. After they order they wait at the <b>heat lamp</b> pickup for matching food.\n\n" +
            "They only have so much patience in line. If they wait too long they leave and you lose the sale.\n\n" +
            "A shift runs on the clock at the top of the screen (pause / play / fast-forward). When the day ends you get a summary: revenue, walkouts, waste, and wait times. Then the next day starts."),
        new Page(
            "Progress",
            "Tasks and milestones",
            "The side panel has <b>Tasks</b> (current missions) and <b>Progression</b> (the chapter list).\n\n" +
            "Progression stays locked until you finish or skip the first-run kitchen tutorial.\n\n" +
            "<b>Tutorial: Restaurant Basics</b> completes on its own when its work is done — there is no quiz on that chapter.\n\n" +
            "Later milestones still work the same way: finish every task, then pass the quiz (every answer correct) to unlock the next chapter."),
        new Page(
            "Guide",
            "A good first shift",
            "1. Inventory → Stations: place freezer, grill, fryer, drinks, assembly, heat lamp, and pantry. First copy of each is free.\n" +
            "2. Inventory → Floor: expand if you have no walking room.\n" +
            "3. Management → Food: buy burger, fries, and drink packs.\n" +
            "4. Management → Workers: hire at least one person (first hire free).\n" +
            "5. Create Flow and click stations in order toward the heat lamp.\n" +
            "6. Assign that worker to the flow.\n" +
            "7. Close Management and serve. When the line grows, open Store Stats and fix the slowest step — do not buy everything at once."),
        new Page(
            "Guide",
            "Remember",
            "Inventory builds the kitchen. Management runs it with <b>flows</b>. Store Stats tells you if the plan is working.\n\n" +
            "Place → Stock → Hire → Create flow → Assign workers → Watch the numbers.\n\n" +
            "Come back to this book whenever a screen feels unclear. The tabs in the game match the chapters here."),
        new Page(
            "Guide",
            "Common stuck points",
            "• <b>Nothing cooks</b> — no flow, no worker on the flow, or Food stock is empty.\n" +
            "• <b>Bought a station but cannot click Next in the tutorial</b> — it must be placed on the floor, not only sitting in inventory.\n" +
            "• <b>Customers order but never eat</b> — the flow does not end at the heat lamp, or the lamp is full of the wrong meals.\n" +
            "• <b>Cannot open Progression</b> — finish the first-run tutorial first.\n" +
            "• <b>Tight on cash</b> — first station of each type and the first hire are free; extra copies and extra hires are not."),
    };
}
