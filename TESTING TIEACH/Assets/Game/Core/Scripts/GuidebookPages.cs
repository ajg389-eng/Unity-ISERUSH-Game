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
            "<b>What is ISE?</b>\nIndustrial and systems engineering improves how people, materials, equipment, and work fit together.\n\n" +
            "<b>In ISE Rush</b>\nDesign the kitchen, run a shift, find what limits output, then improve the process.\n\n" +
            "<b>Try this</b>\nTrace one menu item from ingredients to Pickup. Where does its flow slow down?",
            "ISE", "industrial and systems engineering", "system", "systems", "process", "processes", "flow", "flows"),
        new Page(
            "Plan",
            "Match production to demand",
            "<b>Demand</b> is what customers are expected to order. <b>Capacity</b> is how much the kitchen can produce.\n\n" +
            "<b>In ISE Rush</b>\nSet station targets to support menu demand. Every recipe branch must supply its share.\n\n" +
            "<b>Try this</b>\nCheck whether each ingredient path can keep up with the item target.",
            "demand", "capacity", "process", "flow"),
        new Page(
            "Diagnose",
            "Find what limits output",
            "<b>Bottleneck</b> means the step or condition limiting completed output. Improving another step may not increase what reaches Pickup.\n\n" +
            "<b>In ISE Rush</b>\nUse station status and delivered items to find where the process slows down.\n\n" +
            "<b>Try this</b>\nFind the step that cannot keep up with work arriving to it.",
            "bottleneck", "throughput", "process", "capacity"),
        new Page(
            "Flow",
            "Read queues",
            "A <b>queue</b> forms when work waits for the next step. The items waiting or being worked on are called work in process.\n\n" +
            "<b>In ISE Rush</b>\nQueues and station buffers show where work is backing up. A growing queue can point to a bottleneck.\n\n" +
            "<b>Try this</b>\nCheck the next step before increasing upstream production.",
            "queue", "queues", "work in process", "bottleneck", "flow"),
        new Page(
            "Work Design",
            "Arrange work for people and flow",
            "A clear layout helps workers reach stations and move materials through the process. Long or blocked routes slow transfers.\n\n" +
            "<b>In ISE Rush</b>\nWorkers travel between flow nodes, so station placement affects the time it takes to move items.\n\n" +
            "<b>Try this</b>\nMove frequently linked stations closer and compare delivered output.",
            "flow", "process", "throughput"),
        new Page(
            "Improve",
            "Measure, Change, Compare",
            "<b>Continuous improvement</b> means making a change, checking the result, and learning from the evidence.\n\n" +
            "<b>In ISE Rush</b>\nCompare delivered output, waits, and service before and after a change. Change one likely cause at a time.\n\n" +
            "<b>Try this</b>\nPredict what your change will improve, then check the next shift.",
            "continuous improvement", "process", "system", "throughput"),
        new Page(
            "ISE Glossary",
            "ISE basics",
            "<b>ISE / Industrial and Systems Engineering</b>: Improving how people, materials, equipment, and work fit together.\n\n" +
            "<b>System</b>: Connected parts working together toward an outcome.\n\n" +
            "<b>Process</b>: Steps that turn inputs into an output.\n\n" +
            "<b>Flow</b>: The path work follows through a process.\n\n" +
            "<b>Demand</b>: The amount or rate customers want.\n\n" +
            "<b>Capacity</b>: The amount or rate a resource can produce.",
            "ISE", "industrial and systems engineering", "system", "systems", "process", "processes", "flow", "flows", "demand", "capacity"),
        new Page(
            "ISE Glossary",
            "Production basics",
            "<b>Bottleneck</b>: The step or condition that limits completed output.\n\n" +
            "<b>Throughput</b>: The rate finished items leave the whole process.\n\n" +
            "<b>Queue</b>: Work waiting for the next step.\n\n" +
            "<b>Work in process</b>: Items waiting or being worked on.\n\n" +
            "<b>Continuous improvement</b>: Making a change, measuring its result, and using what you learn.",
            "bottleneck", "bottlenecks", "throughput", "queue", "queues", "work in process", "continuous improvement")
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
            else if (token == "starved") token = "starvation";
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
