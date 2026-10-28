using AcDream.Core.Chat;
using AcDream.Runtime.Chat;

namespace AcDream.App.UI.Hud;

/// <summary>The "[Channel] " the retail wording puts in front of a named-channel line.</summary>
internal static class ChatDeckPrefix
{
    /// <summary>The line without that prefix; any line that does not start with it comes back unchanged.</summary>
    public static ChatProjectedLine Strip(ChatProjectedLine projected, in ChatEntry entry)
    {
        if (entry.Kind != ChatKind.Channel || string.IsNullOrEmpty(entry.ChannelName)) return projected;
        string prefix = $"[{entry.ChannelName}] ";
        if (!projected.Text.StartsWith(prefix, StringComparison.Ordinal)) return projected;
        string text = projected.Text[prefix.Length..];
        if (projected.Spans is null) return new ChatProjectedLine(text, null);
        var spans = new List<ChatTextSpan>();
        int cut = prefix.Length;
        foreach (ChatTextSpan span in projected.Spans)
        {
            if (cut >= span.Text.Length) { cut -= span.Text.Length; continue; }
            spans.Add(cut > 0 ? span with { Text = span.Text[cut..] } : span);
            cut = 0;
        }
        return new ChatProjectedLine(text, spans);
    }
}
