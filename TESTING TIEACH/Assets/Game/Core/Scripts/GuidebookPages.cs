/// <summary>
/// Six-page ISE field guide for the in-game guidebook. Two pages are shown at a time.
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
            "ISE Loop",
            "Improve the whole system",
            "<b>The problem</b>\nA restaurant depends on connected people, equipment, materials, and customer service. Improving one step can create a problem somewhere else.\n\n" +
            "<b>ISE idea</b>\nIndustrial and systems engineering improves the complete process, not just one machine or task.\n\n" +
            "<b>In ISE Rush</b>\nUse the loop: design the kitchen, run a shift, diagnose results, then improve the design.\n\n" +
            "<b>Try this</b>\nTrace one menu item from supply through production to Pickup. Where does its pace change?"),
        new Page(
            "Plan",
            "Set a pace that meets demand",
            "<b>The problem</b>\nStations can make too little to serve customers or make more than the restaurant can sell.\n\n" +
            "<b>ISE idea</b>\nTakt time is the pace needed to meet demand: available time divided by required output. At 15 items/min, the process needs one item every 4 seconds.\n\n" +
            "<b>In ISE Rush</b>\nSet station targets before the shift, then compare demand with items delivered to Pickup. Every required recipe branch must supply its share.\n\n" +
            "<b>Try this</b>\nPick one product and check whether each ingredient branch can support its target rate."),
        new Page(
            "Diagnose",
            "Find what limits completed output",
            "<b>The problem</b>\nLow output can come from missing inputs, station processing, worker transfers, travel, or Pickup space. The slowest individual station is not always the constraint, because parallel branches can combine.\n\n" +
            "<b>ISE idea</b>\nA constraint is the part that limits the whole system's completed output. Diagnose the cause before changing capacity.\n\n" +
            "<b>In ISE Rush</b>\nStart with delivered output and trace backward through the recipe and its queues.\n\n" +
            "<b>Try this</b>\nFind the first step where items stop accumulating at the pace the next step needs."),
        new Page(
            "Flow",
            "Read queues and work in process",
            "<b>The problem</b>\nA growing queue means work is arriving faster than the next step can handle it. Extra production can simply create more waiting food.\n\n" +
            "<b>ISE idea</b>\nWork in process (WIP) is work waiting or being processed. Queue location helps show where flow is restricted.\n\n" +
            "<b>In ISE Rush</b>\nRegister queues are customers waiting to order. Pickup queues are ordered products waiting to be collected. Station buffers show items waiting between operations.\n\n" +
            "<b>Try this</b>\nWhen a queue grows, inspect the next step's capacity, inputs, and available space before increasing upstream output."),
        new Page(
            "Work Design",
            "Make work easier to perform",
            "<b>The problem</b>\nLong routes, blocked access, and confusing layouts add travel and make item transfers less reliable.\n\n" +
            "<b>ISE idea</b>\nHuman factors and work design shape how people interact with tasks, tools, and space. A good process should be clear and practical to perform.\n\n" +
            "<b>In ISE Rush</b>\nWorkers physically travel between flow nodes, so layout and reachability affect transfer time.\n\n" +
            "<b>Try this</b>\nFollow a worker's route. Move related stations closer and clear blocked paths, then compare delivered output."),
        new Page(
            "Improve",
            "Test one change at a time",
            "<b>The problem</b>\nChanging several things at once makes it hard to know what improved performance. A local gain can also move the constraint elsewhere.\n\n" +
            "<b>ISE idea</b>\nMeasure a baseline, change one likely cause, and compare the same measures afterward. Check service and waste together.\n\n" +
            "<b>In ISE Rush</b>\nUse the next shift and Analytics to review orders, produced items, waits, and queues.\n\n" +
            "<b>Try this</b>\nState the problem, predict what your change will do, then check whether the evidence supports your prediction.")
    };
}
