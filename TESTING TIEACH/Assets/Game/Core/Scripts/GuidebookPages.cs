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
            "Use the arrows (or the left and right keys) to turn the page. You always see two pages at once, like a real book.\n\n" +
            "Start with Inventory to buy and place equipment. Switch to Management when you are ready to hire people, assign work, order food, and read how busy the store is.\n\n" +
            "If you get stuck mid-shift, open this book from the pause menu. The game stays paused while you read."),
        new Page(
            "Guidebook",
            "What's inside",
            "<b>Inventory</b>\n" +
            "• Stations — buy and place kitchen equipment\n" +
            "• Floor — expand the kitchen grid\n\n" +
            "<b>Management</b>\n" +
            "• Stations — assign workers and where food goes next\n" +
            "• Store Stats — live numbers that show bottlenecks\n" +
            "• Workers — hire staff\n" +
            "• Ingredients — keep the pantry stocked\n\n" +
            "Tip: Inventory changes the layout. Management runs the layout. You need both."),
        new Page(
            "Inventory",
            "Inventory",
            "Open <b>Inventory</b> from the top-left tabs. While it is open you are in Build mode: the clock still exists, but you are editing the kitchen instead of serving.\n\n" +
            "Inventory has two tabs:\n" +
            "• <b>Stations</b> — equipment you can buy and drop on the grid\n" +
            "• <b>Floor</b> — spend money to grow the walkable kitchen\n\n" +
            "Purchases can be undone with the Undo control under the list if you placed something by mistake.\n\n" +
            "Close Inventory when you are done so you return to Play mode."),
        new Page(
            "Inventory",
            "Stations",
            "The Stations tab is your catalog of kitchen equipment (grill, fryer, freezer, assembly, drinks, heat lamp, and more).\n\n" +
            "<b>How to place</b>\n" +
            "1. Pick a card if you can afford it.\n" +
            "2. Move the ghost onto an empty grid cell.\n" +
            "3. Click to drop it. Rotate if the station offers it.\n\n" +
            "Leave space for workers to walk and for customers at the register. A cramped kitchen looks busy on Store Stats even when you have enough equipment.\n\n" +
            "A station that is placed but has no worker will sit idle."),
        new Page(
            "Inventory",
            "Floor",
            "The Floor tab is how you grow the kitchen itself.\n\n" +
            "Expanding adds grid cells (and costs money). Use it when stations no longer fit, queues pile into walkways, or you want a clearer path from prep to the heat lamp or register.\n\n" +
            "The button shows your current size and the next size. If it says the floor is maxed, you cannot grow further.\n\n" +
            "Expand before you buy a large new station if you are already packed. Extra floor does nothing by itself — fill it with a better layout."),
        new Page(
            "Inventory",
            "Build checklist",
            "A simple setup that can serve orders:\n\n" +
            "• Storage / freezer or pantry for ingredients\n" +
            "• Cook stations (grill, fryer, etc.)\n" +
            "• Assembly so meals can be finished\n" +
            "• Heat lamp to hold finished food\n" +
            "• Register so customers can pay\n\n" +
            "After you place them, open <b>Management</b> and connect the flow: worker on the station, output pointing to the next station. Buying equipment is only half the job."),
        new Page(
            "Management",
            "Management",
            "Open <b>Management</b> from the top-left tabs (or press M). Time pauses so you can think, but you can still click stations and workers in the world.\n\n" +
            "Tabs inside Management:\n" +
            "• Stations (assign people and outputs in the world)\n" +
            "• Store Stats (how healthy the system is)\n" +
            "• Workers (hire)\n" +
            "• Ingredients (order stock)\n\n" +
            "Closing Management returns you to Play. Inventory and Management cannot stay open together."),
        new Page(
            "Management",
            "Stations",
            "Click a station in the kitchen. A panel opens on the right.\n\n" +
            "<b>Assign Worker</b> — pick who operates this station. One worker can run up to <b>three</b> stations. An unassigned station does not cook.\n\n" +
            "<b>Assign Output</b> — choose where finished work is delivered (for example grill → assembly, assembly → heat lamp). Workers only carry to the output you set.\n\n" +
            "You can clear a worker or output if you made a mistake. Watch the lines in the world — they show who is tied to which station."),
        new Page(
            "Management",
            "Store Stats",
            "Store Stats is a live report of your kitchen as a system. Use it when lines form or money stalls.\n\n" +
            "• <b>Customers in system</b> — how many people are inside the flow (WIP). High numbers mean congestion.\n" +
            "• <b>Queue length</b> — where people wait. Long queues mark bottlenecks.\n" +
            "• <b>Average wait / completion time</b> — service speed from the customer's view.\n" +
            "• <b>Throughput</b> — orders finished per minute.\n" +
            "• <b>Utilization</b> — how busy stations and workers are. 100% often means they are the limit.\n" +
            "• <b>Heat lamp / waste / revenue</b> — extra food that expires is money lost; sales from the lamp are money earned."),
        new Page(
            "Management",
            "Reading the numbers",
            "If wait time is high and one station's queue is long, add a worker, add a matching station, or fix that station's output.\n\n" +
            "If workers are idle but customers wait, the layout or ingredient stock is probably wrong — not headcount.\n\n" +
            "If the heat lamp is empty, upstream stations are too slow. If it is full and meals expire, you are overproducing.\n\n" +
            "Stats will not place stations for you. They tell you <i>where</i> to look next."),
        new Page(
            "Management",
            "Workers",
            "The Workers tab is where you hire. Each hire costs money and adds a person who can be assigned to stations.\n\n" +
            "Hiring alone does nothing. After you hire:\n" +
            "1. Stay in Management.\n" +
            "2. Click the station they should run.\n" +
            "3. Assign that worker (max three stations each).\n\n" +
            "Unassigned workers stand idle. Overloaded workers bounce between too many jobs and become the bottleneck.\n\n" +
            "Undo under the list can reverse a hire if you clicked too soon."),
        new Page(
            "Management",
            "Ingredients",
            "The Ingredients tab shows kitchen stock and lets you buy packs with cash.\n\n" +
            "Stations cannot cook what you do not have. If a station looks assigned but nothing moves, check this list before you rebuild the layout.\n\n" +
            "Order a little ahead of the rush so cook stations do not starve. Ordering huge piles of food that never cook is wasted money — the same idea as heat-lamp waste, just earlier in the chain.\n\n" +
            "Undo works here too if you ordered the wrong pack."),
        new Page(
            "Guide",
            "A good first hour",
            "1. Inventory → Stations: place a basic cook line and a register.\n" +
            "2. Inventory → Floor: expand if you have no walking room.\n" +
            "3. Management → Workers: hire at least one person.\n" +
            "4. Management → Stations: assign that worker and set outputs toward the heat lamp / register.\n" +
            "5. Management → Ingredients: buy starter stock.\n" +
            "6. Close Management and serve.\n" +
            "7. When the line grows, open Store Stats and fix the slowest step — do not buy everything at once."),
        new Page(
            "Guide",
            "Remember",
            "Inventory builds the kitchen. Management runs it. Store Stats tells you if the plan is working.\n\n" +
            "Place → Hire → Assign → Stock → Watch the numbers.\n\n" +
            "Come back to this book whenever a screen feels unclear. The tabs in the game match the chapters here."),
    };
}
