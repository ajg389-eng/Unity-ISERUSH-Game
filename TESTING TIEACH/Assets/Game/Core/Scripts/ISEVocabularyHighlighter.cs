using System.Text.RegularExpressions;

/// <summary>Formats common Industrial and Systems Engineering terms in learning text.</summary>
public static class ISEVocabularyHighlighter
{
    const string ReadTextColor = "#70CDBD";
    const string UnreadTextColor = "#FFD45A";
    static readonly Regex InjectedHighlight = new Regex(
        @"<link=""ise:[^""]+"">(?:<mark=[^>]+>)?<b>(?:<u>)?<color=[^>]+>(?<term>.*?)</color>(?:</u>)?</b>(?:</mark>)?</link>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    static readonly Regex Vocabulary = new Regex(
        @"\b(?:ISE|facility\s+layout\s+and\s+design|facility\s+layout|process\s+analysis|"
        + @"engineering\s+economy|systems?\s+integration|inventory\s+position|reorder\s+point|"
        + @"safety\s+stock|holding\s+cost|payback(?:\s+period)?|material\s+handling|"
        + @"industrial\s+(?:(?:&|and)\s+)?systems?\s+engineer(?:ing)?|"
        + @"industrial\s+engineer(?:ing)?|systems?\s+engineer(?:ing)?|"
        + @"operations\s+research|systems?\s+(?:thinking|analysis|design)|human\s+factors|work\s+design|"
        + @"ergonomics|"
        + @"continuous\s+improvement|process\s+improvement|work\s+in\s+process|"
        + @"capacity\s+planning|workforce\s+planning|resource\s+allocation|"
        + @"line\s+balancing|work\s+measurement|standard\s+work|"
        + @"production\s+(?:rate|line|system)|target\s+(?:output\s+)?rate|target\s*/\s*min|planned\s+output|supply\s+chain|value\s+stream|"
        + @"quality\s+control|freshness|cycle\s+time|lead\s+time|takt\s+time|"
        + @"bottlenecks?|throughput|utilization|efficiency|capacity|inventory|"
        + @"variability|measurement|constraints?|queue\s+analysis|queue(?:s|ing)?|queuing|"
        + @"starv(?:ation|ed)|replenishment|flows?|demand|waste|shortfalls?|patience|"
        + @"optimi[sz](?:e|ation)|forecast(?:ing)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Highlight(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Vocabulary.Replace(text, match =>
        {
            bool unread = !GuidebookUI.IsGlossaryTermDiscovered(match.Value);
            string textColor = unread ? UnreadTextColor : ReadTextColor;
            string underlineOpen = unread ? "<u>" : string.Empty;
            string underlineClose = unread ? "</u>" : string.Empty;
            return "<link=\"ise:" + match.Value + "\"><b>" + underlineOpen
                + "<color=" + textColor + ">" + match.Value + "</color>"
                + underlineClose + "</b></link>";
        });
    }

    public static string RemoveHighlightMarkup(string text)
    {
        return string.IsNullOrEmpty(text) ? text : InjectedHighlight.Replace(text, "${term}");
    }
}
