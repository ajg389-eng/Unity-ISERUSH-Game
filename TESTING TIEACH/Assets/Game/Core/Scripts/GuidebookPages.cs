using System;
using System.Text.RegularExpressions;

/// <summary>
/// ISE field guide and glossary for the in-game guidebook. Two pages are shown at a time.
/// </summary>
public static class GuidebookPages
{
    static readonly Regex GlossaryTermNormalizer = new Regex("[^a-z0-9]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex GlossaryHeadingMatcher = new Regex("<b>(?<heading>.*?)</b>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public struct Page
    {
        public string chapter;
        public string title;
        public string body;
        public string[] glossaryTerms;

        public Page(string chapter, string title, string body, params string[] glossaryTerms)
        {
            this.chapter = chapter;
            this.title = title;
            this.body = body;
            this.glossaryTerms = glossaryTerms ?? Array.Empty<string>();
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
            "<b>Try this</b>\nState the problem, predict what your change will do, then check whether the evidence supports your prediction."),
        new Page(
            "ISE Glossary",
            "ISE and systems",
            "<b>ISE / Industrial and Systems Engineering</b>: Improving connected systems of people, equipment, materials, information, and work.\n\n" +
            "<b>Engineering</b>: Applying scientific, mathematical, and practical knowledge to design or improve products, processes, and systems.\n\n" +
            "<b>System</b>: A set of connected parts that work together toward an outcome. People, equipment, materials, information, and customer actions can all interact within one system.\n\n" +
            "<b>Industrial systems engineer / industrial engineer / systems engineer</b>: A professional who studies how an operation performs and finds practical ways to improve it.",
            "ISE", "industrial and systems engineering", "industrial & systems engineering",
            "industrial and systems engineer", "industrial & systems engineer", "industrial systems engineering",
            "industrial engineer", "industrial engineering", "industrial systems engineer", "system engineer",
            "systems engineer", "system engineering", "systems engineering", "engineering", "system", "systems"),
        new Page(
            "ISE Glossary",
            "Systems and performance",
            "<b>Systems thinking</b>: Looking at how parts interact, including effects that move to other parts of the operation.\n\n" +
            "<b>Component interdependence</b>: How a change to one resource or process step affects the connected steps that rely on it.\n\n" +
            "<b>Holistic performance evaluation</b>: Judging the whole operation using connected measures such as output, service, cost, and waste.",
            "system thinking", "systems thinking", "component interdependence", "holistic performance evaluation"),
        new Page(
            "ISE Glossary",
            "Analysis and improvement",
            "<b>Onboarding</b>: A guided introduction to the tools, goals, and first tasks needed to begin work.\n\n" +
            "<b>Systems analysis</b>: Measuring how a system works to understand its performance and limits.\n\n" +
            "<b>Systems design</b>: Choosing and arranging people, equipment, materials, and rules to meet a goal.\n\n" +
            "<b>Operations research</b>: Using models and data to compare choices and make better decisions.",
            "onboarding", "system analysis", "systems analysis", "system design", "systems design", "operations research"),
        new Page(
            "ISE Glossary",
            "Improvement methods",
            "<b>Optimization</b>: Finding the best option for a stated goal and its constraints.\n\n" +
            "<b>Measurement</b>: Collecting comparable data to describe performance.\n\n" +
            "<b>Continuous improvement / process improvement</b>: Repeatedly testing changes and using results to improve the operation.",
            "optimization", "optimisation", "optimize", "optimise", "measurement", "continuous improvement", "process improvement"),
        new Page(
            "ISE Glossary",
            "Planning and demand",
            "<b>Demand</b>: The rate and amount of products customers want.\n\n" +
            "<b>Demand forecasting</b>: Estimating future demand from available information, such as prior sales and expected customer traffic. Forecasts guide plans but are not guarantees.\n\n" +
            "<b>Shortfall</b>: The gap when expected demand exceeds planned production or available ingredient stock.",
            "demand", "forecast", "forecasting", "demand forecasting", "shortfall", "shortfalls"),
        new Page(
            "ISE Glossary",
            "Layout and capacity",
            "<b>Facility layout and design</b>: Arranging workspaces, stations, and routes to support clear, direct work.\n\n" +
            "<b>Capacity / capacity planning</b>: The maximum work a resource can complete, and the process of matching that capacity to expected demand.\n\n" +
            "<b>Resource allocation</b>: Deciding where to assign limited resources such as workers, stock, or equipment.",
            "facility layout", "facility layout and design", "capacity", "capacity planning", "resource allocation"),
        new Page(
            "ISE Glossary",
            "Supply and inventory",
            "<b>Inventory holding cost</b>: The cost of keeping stock available, including cash tied up, storage, handling, and spoilage.\n\n" +
            "<b>Inventory</b>: Materials or products held for future use. The stock cap is storage capacity; daily use can exceed it when replenishment arrives.\n\n" +
            "<b>Supply chain</b>: The suppliers, deliveries, storage, and processes that move materials where they are needed.",
            "inventory holding cost", "inventory", "supply chain"),
        new Page(
            "ISE Glossary",
            "Replenishment planning",
            "<b>Purchase lead time</b>: The delay between placing an ingredient order and receiving the delivery. Stock planning must cover expected use during this wait.\n\n" +
            "<b>Safety stock</b>: Extra inventory kept as a buffer against uncertain demand, usage, or delivery timing.\n\n" +
            "<b>Automatic replenishment</b>: A repeating supply rule that orders or restores ingredients as stock is consumed, reducing manual ordering.",
            "purchase lead time", "lead time", "safety stock", "automatic replenishment"),
        new Page(
            "ISE Glossary",
            "Supply and inventory (continued)",
            "<b>Supply rate control</b>: Choosing how quickly each ingredient is replenished. Too little risks a stockout; too much can create excess stock.\n\n" +
            "<b>Stockout risk</b>: The chance an ingredient runs out before more arrives, interrupting production or service.\n\n" +
            "<b>Variability</b>: Natural differences in demand, processing, or delivery over time.\n\n" +
            "<b>Freshness</b>: How suitable an ingredient remains after storage or waiting.\n\n" +
            "<b>Waste</b>: Resources used without creating useful output, including spoiled or excess production.",
            "supply rate control", "stockout risk", "variability", "freshness", "waste"),
        new Page(
            "ISE Glossary",
            "Production and processes",
            "<b>Production</b>: Creating goods or services by combining work, equipment, materials, and information.\n\n" +
            "<b>Process</b>: A sequence of steps that transforms inputs into an output.\n\n" +
            "<b>Production system / production line</b>: Connected resources and steps that make a product.\n\n" +
            "<b>Flow / value stream</b>: The path materials and work follow from supply to the finished product.\n\n" +
            "<b>Target rate / planned output</b>: The output pace a station is asked to achieve. The actual pace can be lower if a resource is starved, blocked, or constrained.\n\n" +
            "<b>Production rate / target per minute</b>: The number of units planned or completed per minute.",
            "process", "processes", "production", "production system", "production line", "production rate",
            "flow", "flows", "value stream", "target rate", "target output rate", "target / min", "planned output", "target per minute"),
        new Page(
            "ISE Glossary",
            "Production pace",
            "<b>Cycle time</b>: The time needed to complete one unit at a step.\n\n" +
            "<b>Takt time</b>: The pace required to meet demand, calculated as available time divided by required output.",
            "cycle time", "takt time"),
        new Page(
            "ISE Glossary",
            "Capacity and flow",
            "<b>Throughput</b>: The rate of completed output from the whole process.\n\n" +
            "<b>Bottleneck identification</b>: Finding the condition that limits completed system output. Parallel branches can combine, so inspect the full flow.\n\n" +
            "<b>Constraint</b>: A resource, step, or condition that limits the system's completed output. A bottleneck is the current constraint.",
            "throughput", "bottleneck identification", "bottleneck", "bottlenecks", "constraint", "constraints",
            "system", "systems"),
        new Page(
            "ISE Glossary",
            "Efficiency and queues",
            "<b>Station utilization</b>: The share of available time a station spends working. High utilization alone does not guarantee high system output.\n\n" +
            "<b>Efficiency</b>: How well resources produce useful output relative to the work or time available.\n\n" +
            "<b>Queue / work in process (WIP)</b>: Items or orders waiting or being worked on between steps.",
            "station utilization", "utilization", "efficiency", "work in process", "queue", "queues"),
        new Page(
            "ISE Glossary",
            "Queues and work balance",
            "<b>Queue analysis</b>: Studying where and why work waits by comparing queue location, size, and growth.\n\n" +
            "<b>Queueing</b>: Work or customers waiting for a resource to become available. Queueing analysis compares arrival and service rates to understand how waits grow.\n\n" +
            "<b>Workload balancing</b>: Distributing work across steps and workers to reduce overload, starvation, and avoidable waiting.\n\n" +
            "<b>Batch processing</b>: Processing several units together in one operation or production run.",
            "queue analysis", "queueing", "queuing", "workload balancing", "batch processing"),
        new Page(
            "ISE Glossary",
            "Production planning and economics",
            "<b>Production scheduling</b>: Choosing what to produce, in what order, and when, so available capacity and materials meet demand.\n\n" +
            "<b>Simple payback</b>: The time an investment takes to recover its initial cost through benefits or savings.\n\n" +
            "<b>Profit analysis</b>: Comparing sales revenue with ingredient, labor, operating, and upgrade costs to understand profitability.",
            "production scheduling", "simple payback", "profit analysis"),
        new Page(
            "ISE Glossary",
            "People and work",
            "<b>Human factors</b>: Designing work around people's abilities and limits so it is safe, clear, and reliable.\n\n" +
            "<b>Customer patience / patience</b>: How long a customer is willing to wait for an order before leaving without it. In ISE Rush, patience starts after the customer orders.\n\n" +
            "<b>Ergonomics</b>: Fitting tools, tasks, and workspaces to the people using them.\n\n" +
            "<b>Work design</b>: Defining tasks, responsibilities, methods, and routes so work can be performed effectively.",
            "human factors", "customer patience", "patience", "ergonomics", "work design"),
        new Page(
            "ISE Glossary",
            "Staffing and line balance",
            "<b>Workforce planning</b>: Matching the number and skills of workers to the expected workload.\n\n" +
            "<b>Labor allocation</b>: Assigning available worker time to the flows and tasks where it best supports planned output.\n\n" +
            "<b>Line balancing</b>: Distributing work across steps so one step does not create avoidable waiting or overload.",
            "workforce planning", "labor allocation", "line balancing"),
        new Page(
            "ISE Glossary",
            "Work methods and quality",
            "<b>Work measurement</b>: Estimating how long tasks take and how much labor capacity they require.\n\n" +
            "<b>Standard work</b>: A clear, repeatable method for completing a task.\n\n" +
            "<b>Quality control</b>: Checking that output meets the required standard and correcting causes of defects.",
            "work measurement", "standard work", "quality control")
    };

    public static int FindGlossaryPageIndex(string term)
    {
        string normalizedTerm = NormalizeGlossaryTerm(term);
        int firstGlossaryPage = -1;
        for (int pageIndex = 0; pageIndex < All.Length; pageIndex++)
        {
            Page page = All[pageIndex];
            if (!string.Equals(page.chapter, "ISE Glossary", StringComparison.OrdinalIgnoreCase))
                continue;

            if (firstGlossaryPage < 0) firstGlossaryPage = pageIndex;
            if (page.glossaryTerms == null) continue;
            for (int i = 0; i < page.glossaryTerms.Length; i++)
                if (NormalizeGlossaryTerm(page.glossaryTerms[i]) == normalizedTerm)
                    return pageIndex;
        }

        return firstGlossaryPage >= 0 ? firstGlossaryPage : 0;
    }

    public static string HighlightGlossaryEntry(string body, string term)
    {
        if (string.IsNullOrEmpty(body) || string.IsNullOrWhiteSpace(term)) return body;

        string[] termTokens = GetGlossaryMatchTokens(term);
        if (termTokens.Length == 0) return body;

        MatchCollection headings = GlossaryHeadingMatcher.Matches(body);
        int bestIndex = -1;
        int bestScore = -1;
        for (int i = 0; i < headings.Count; i++)
        {
            string heading = headings[i].Groups["heading"].Value;
            int score = ScoreGlossaryHeading(termTokens, GetGlossaryMatchTokens(heading));
            if (score <= bestScore) continue;
            bestScore = score;
            bestIndex = i;
        }

        if (bestIndex >= 0 && bestScore >= 500)
        {
            Match headingMatch = headings[bestIndex];
            const string emphasisStart = "<mark=#51430F padding=\"20,0,20,0\"><color=#FFE15A>";
            const string emphasisEnd = "</color></mark>";
            return body.Substring(0, headingMatch.Index)
                + emphasisStart + headingMatch.Value + emphasisEnd
                + body.Substring(headingMatch.Index + headingMatch.Length);
        }

        int textIndex = body.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (textIndex < 0) return body;
        return body.Substring(0, textIndex)
            + "<mark=#51430F padding=\"20,0,20,0\"><color=#FFE15A>"
            + body.Substring(textIndex, term.Length) + "</color></mark>"
            + body.Substring(textIndex + term.Length);
    }

    static int ScoreGlossaryHeading(string[] termTokens, string[] headingTokens)
    {
        if (termTokens.Length == 0 || headingTokens.Length == 0) return -1;
        int matches = 0;
        for (int i = 0; i < termTokens.Length; i++)
        {
            bool found = false;
            for (int j = 0; j < headingTokens.Length; j++)
            {
                if (termTokens[i] != headingTokens[j]) continue;
                found = true;
                break;
            }
            if (found) matches++;
        }

        if (matches == 0) return -1;
        int score = 500 * matches / termTokens.Length;
        if (matches == termTokens.Length && headingTokens.Length == termTokens.Length)
            score += 300;
        else if (headingTokens.Length > 0 && termTokens[0] == headingTokens[0])
            score += 100;
        return score;
    }

    static string[] GetGlossaryMatchTokens(string value)
    {
        string normalized = NormalizeGlossaryTerm(value).Replace(" and ", " ");
        string[] tokens = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            if (token == "forecasting") token = "forecast";
            else if (token == "min") token = "rate";
            else if (token == "queuing") token = "queueing";
            else if (token == "optimization" || token == "optimisation"
                || token == "optimize" || token == "optimise") token = "optimization";
            else if (token.EndsWith("ies", StringComparison.Ordinal) && token.Length > 4)
                token = token.Substring(0, token.Length - 3) + "y";
            else if (token.EndsWith("ses", StringComparison.Ordinal) && token.Length > 4)
                token = token.Substring(0, token.Length - 2);
            else if (token.EndsWith("s", StringComparison.Ordinal) && token.Length > 3)
                token = token.Substring(0, token.Length - 1);
            tokens[i] = token;
        }
        return tokens;
    }

    static string NormalizeGlossaryTerm(string term)
    {
        return GlossaryTermNormalizer.Replace((term ?? string.Empty).ToLowerInvariant(), " ").Trim();
    }
}
