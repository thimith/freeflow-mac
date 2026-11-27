using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace FreeFlow;

/// Reads a few characters around the text cursor of the focused field (via UI Automation, like screen readers do),
/// so dictation can continue mid-sentence naturally.
static class CaretContext
{
    public sealed record Around(string Before, string After);

    /// Runs off the UI thread with a time limit: some apps are slow to answer accessibility queries.
    public static Task<Around> CaptureAsync() => Task.Run(() =>
    {
        var t = Task.Run(Read);
        return t.Wait(600) ? t.Result : null;
    });

    static Around Read()
    {
        try
        {
            var el = AutomationElement.FocusedElement;
            if (el == null) return null;
            string placeholder = "", help = "";
            try { placeholder = (el.Current.Name ?? "").Trim(); help = (el.Current.HelpText ?? "").Trim(); } catch { }
            bool IsPlaceholder(string s) { s = s.Trim(); return s.Length == 0 || s == placeholder || s == help; }

            // The field's real value (inputs, text areas, most browser boxes) is the most reliable "is it empty?" signal.
            string value = null;
            try { if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp) && vp is ValuePattern v) value = v.Current.Value; } catch { }
            if (value != null && IsPlaceholder(value)) return new Around("", "");   // empty box → new sentence

            if (el.TryGetCurrentPattern(TextPattern.Pattern, out var tp) && tp is TextPattern text)
            {
                var doc = text.DocumentRange;
                var all = doc.GetText(4000) ?? "";
                if (IsPlaceholder(all)) return new Around("", "");

                var sel = text.GetSelection();
                if (sel.Length == 0) return null;
                var caret = sel[0];
                var before = caret.Clone();
                before.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
                before.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -60);
                // browsers/Electron apps sometimes walk out of the box into page text (labels, earlier messages):
                // never look past the start/end of this field
                if (before.CompareEndpoints(TextPatternRangeEndpoint.Start, doc, TextPatternRangeEndpoint.Start) < 0)
                    before.MoveEndpointByRange(TextPatternRangeEndpoint.Start, doc, TextPatternRangeEndpoint.Start);
                var after = caret.Clone();
                after.MoveEndpointByRange(TextPatternRangeEndpoint.Start, caret, TextPatternRangeEndpoint.End);
                after.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 20);
                if (after.CompareEndpoints(TextPatternRangeEndpoint.End, doc, TextPatternRangeEndpoint.End) > 0)
                    after.MoveEndpointByRange(TextPatternRangeEndpoint.End, doc, TextPatternRangeEndpoint.End);
                var b = before.GetText(60) ?? "";
                var a = after.GetText(20) ?? "";
                if (IsPlaceholder(b + a)) return new Around("", "");
                // if the app gave us text that isn't actually in this field, we can't trust it → treat as new sentence
                if (all.Length < 4000 && b.Trim().Length > 0 && !all.Contains(b.Trim())) return null;
                return new Around(b, a);
            }
            // apps that don't expose the cursor: we can't know → treat as a new sentence (capitalized)
        }
        catch { }
        return null;
    }

    /// True only when we're sure the cursor sits inside a sentence: same line, right after a word or a comma.
    /// Start of the box, a new line, or after . ! ? : → a new sentence (capitalized).
    public static bool MidSentence(Around a)
    {
        if (a == null) return false;
        var b = a.Before.Replace("\r", "\n");
        int nl = b.LastIndexOf('\n');
        if (nl >= 0) b = b[(nl + 1)..];
        b = b.TrimEnd(' ', '\t', '\u00A0');
        if (b.Length == 0) return false;
        char last = b[^1];
        return char.IsLetterOrDigit(last) || ",;-–—(\"'“‘".Contains(last);
    }

    /// Adjust dictated text so it continues the sentence around the cursor.
    public static string Fit(string text, Around a)
    {
        if (string.IsNullOrEmpty(text)) return text;
        // the speech engine often hands back " hello" with a leading space: we decide the spacing ourselves
        var t = text.TrimStart(' ', '\t', '\u00A0');
        if (t.Length == 0) return t;
        if (a == null) return UpperFirst(t);
        if (!MidSentence(a)) return UpperFirst(t);   // new sentence → always a capital
        {
            t = LowerFirst(t);
            // leading space when we're right after a word
            if (a.Before.Length > 0 && !char.IsWhiteSpace(a.Before[^1]) && !",.;:!?)".Contains(t[0])) t = " " + t;
        }
        // text continues right after the cursor → no closing period
        var after = a.After.TrimStart(' ');
        if (after.Length > 0 && char.IsLetterOrDigit(after[0]) && t.EndsWith('.') && !t.EndsWith("..."))
            t = t[..^1];
        return t;
    }

    static string UpperFirst(string t)
    {
        int i = 0;
        while (i < t.Length && !char.IsLetter(t[i])) i++;
        if (i >= t.Length || char.IsUpper(t[i])) return t;
        return t[..i] + char.ToUpper(t[i]) + t[(i + 1)..];
    }

    static string LowerFirst(string t)
    {
        int i = 0;
        while (i < t.Length && !char.IsLetter(t[i])) i++;
        if (i >= t.Length || !char.IsUpper(t[i])) return t;
        int end = i;
        while (end < t.Length && (char.IsLetter(t[end]) || t[end] == '\'' || t[end] == '’')) end++;
        var word = t[i..end];
        if (word == "I" || word.StartsWith("I'") || word.StartsWith("I’")) return t;              // "I", "I'm"
        if (word.Length > 1 && word.Skip(1).Any(char.IsUpper)) return t;                            // acronyms, iPhone-style names
        if (Vocabulary.Parse().words.Any(w => w.Equals(word, StringComparison.Ordinal))) return t;  // names you added
        return t[..i] + char.ToLower(t[i]) + t[(i + 1)..];
    }
}
