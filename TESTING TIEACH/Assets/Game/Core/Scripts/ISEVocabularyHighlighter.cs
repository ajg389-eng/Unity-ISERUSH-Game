using System.Text.RegularExpressions;

/// <summary>Formats common Industrial and Systems Engineering terms in learning text.</summary>
public static class ISEVocabularyHighlighter
{
    const string HighlightTextColor = "#FFD45A";
    const string ClaimedHighlightTextColor = "#FFFFFF";
    static readonly Regex InjectedHighlight = new Regex(
        @"<link=""ise:[^""]+"">(?:<mark=[^>]+>)?<b>(?:<u>)?<color=[^>]+>(?<term>.*?)</color>(?:</u>)?</b>(?:</mark>)?</link>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    static readonly Regex Vocabulary = new Regex(
        @"\b(?:ISE|industrial\s+(?:(?:&|and)\s+)?systems?\s+engineering|"
        + @"systems?\s+engineering|systems?|process(?:es)?|flows?|demand|capacity|"
        + @"bottlenecks?|throughput|queues?|work\s+in\s+process|continuous\s+improvement)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Highlight(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = RemoveHighlightMarkup(text);
        return Vocabulary.Replace(text, match =>
        {
            string color = GuidebookUI.IsGlossaryTermRewardClaimed(match.Value)
                ? ClaimedHighlightTextColor
                : HighlightTextColor;
            return "<link=\"ise:" + match.Value + "\"><b><u>"
                + "<color=" + color + ">" + match.Value + "</color>"
                + "</u></b></link>";
        });
    }

    public static string RemoveHighlightMarkup(string text)
    {
        return string.IsNullOrEmpty(text) ? text : InjectedHighlight.Replace(text, "${term}");
    }
}
