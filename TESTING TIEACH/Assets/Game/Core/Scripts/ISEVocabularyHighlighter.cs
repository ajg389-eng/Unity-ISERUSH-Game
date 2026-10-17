using System.Text.RegularExpressions;

/// <summary>Formats common Industrial and Systems Engineering terms in learning text.</summary>
public static class ISEVocabularyHighlighter
{
    const string HighlightColor = "#D8B365";
    static readonly Regex Vocabulary = new Regex(
        @"\b(?:ISE|industrial\s+(?:(?:&|and)\s+)?systems?\s+engineer(?:ing)?|"
        + @"industrial\s+engineer(?:ing)?|systems?\s+engineer(?:ing)?|"
        + @"operations\s+research|systems?\s+(?:thinking|analysis|design)|human\s+factors|work\s+design|"
        + @"ergonomics|engineering|"
        + @"continuous\s+improvement|process\s+improvement|work\s+in\s+process|"
        + @"capacity\s+planning|workforce\s+planning|resource\s+allocation|"
        + @"line\s+balancing|work\s+measurement|standard\s+work|"
        + @"production\s+(?:rate|line|system)|target\s+(?:output\s+)?rate|target\s*/\s*min|planned\s+output|supply\s+chain|value\s+stream|"
        + @"quality\s+control|freshness|cycle\s+time|lead\s+time|takt\s+time|"
        + @"bottlenecks?|throughput|utilization|efficiency|capacity|inventory|"
        + @"variability|measurement|constraints?|queues?|flows?|demand|waste|"
        + @"optimi[sz](?:e|ation)|forecast(?:ing)?|process(?:es)?|systems?|production)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Highlight(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Vocabulary.Replace(text, match =>
            "<b><color=" + HighlightColor + ">" + match.Value + "</color></b>");
    }
}
