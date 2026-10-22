using System.Numerics;
using AcDream.App.UI.Layout;

namespace AcDream.App.UI;

/// <summary>
/// A plugin dialog in the retail confirmation box's art (layout 0x15). The
/// title is a gold first line over the message, since the retail box has no
/// caption. Its buttons are retail buttons in the box's button bar: two sit
/// where the retail Yes and No do, one is centred, and three or four are
/// centred 16 px apart, widening the box when they need it. Buttons past
/// the layout's own two are taken from further copies of the layout.
/// </summary>
internal sealed class UiRetailPluginDialog : UiPluginDialog
{
    public const float RowGap = 16f;
    public const float RowMargin = 24f;
    /// <summary>The gold of the markup focus outline.</summary>
    public static readonly Vector4 TitleColor = UiMarkupHotkey.FocusColor;

    private UiRetailPluginDialog(int buttonCount, UiElement popup, UiText message, IReadOnlyList<UiButton> buttons)
        : base(buttonCount)
    {
        Popup = popup;
        Message = message;
        Buttons = buttons;
    }

    public UiElement Popup { get; }

    public UiText Message { get; }

    public IReadOnlyList<UiButton> Buttons { get; }

    /// <summary>
    /// Builds the dialog from a freshly imported confirmation layout, or
    /// returns null when the layout lacks a part it needs (the host then
    /// draws one instead).
    /// </summary>
    public static UiRetailPluginDialog? TryCreate(
        ImportedLayout layout, Func<ImportedLayout?> moreLayouts,
        string title, string message, IReadOnlyList<string> buttons)
    {
        if (layout.FindElement(RetailConfirmationDialogView.PopupElementId) is not { } popup
            || layout.FindElement(RetailConfirmationDialogView.MessageElementId) is not UiText text
            || layout.FindElement(RetailConfirmationDialogView.AcceptButtonId) is not UiButton accept
            || layout.FindElement(RetailConfirmationDialogView.RejectButtonId) is not UiButton reject
            || accept.Parent is not { } bar
            || !ReferenceEquals(reject.Parent, bar))
            return null;

        var row = new List<UiButton>(buttons.Count) { accept };
        for (int i = 1; i < buttons.Count - 1; i++)
        {
            if (moreLayouts()?.FindElement(RetailConfirmationDialogView.AcceptButtonId) is not UiButton extra)
                return null;
            extra.Parent?.RemoveChild(extra);
            extra.Top = accept.Top;
            bar.AddChild(extra);
            row.Add(extra);
        }
        if (buttons.Count > 1)
            row.Add(reject);
        else
            reject.Visible = false;

        layout.Root.RemoveChild(popup);
        popup.LayoutPolicy = null;
        popup.Anchors = AnchorEdges.None;
        popup.Left = 0f;
        popup.Top = 0f;
        text.LayoutPolicy = null;
        text.Anchors = AnchorEdges.None;
        text.Padding = 0f;
        text.Selectable = false;
        if (layout.FindElement(RetailConfirmationDialogView.PendingDisplayId) is IUiDatStateful pending)
            pending.TrySetRetailState(0x19u); // the retail "nothing else waiting" state

        float buttonWidth = accept.Width;
        float popupHeight = popup.Height;
        if (row.Count >= 3)
        {
            float grow = MathF.Max(popup.Width,
                row.Count * buttonWidth + (row.Count - 1) * RowGap + 2f * RowMargin) - popup.Width;
            popup.Width += grow;
            text.Width += grow; // keep the 15 px margins either side
        }
        SetLines(popup, text, title, message);

        // Take the button bar out of layout: it spans the popup at the bottom
        // edge. Left to its policy it would stay at its 400 px width and be
        // re-centred in a widened popup, shifting the row.
        bar.LayoutPolicy = null;
        bar.Anchors = AnchorEdges.None;
        bar.Left = 0f;
        bar.Width = popup.Width;
        bar.Top += popup.Height - popupHeight;

        var dialog = new UiRetailPluginDialog(buttons.Count, popup, text, row);
        // The bar spans the popup, so its width is the popup's.
        float rowWidth = row.Count * buttonWidth + (row.Count - 1) * RowGap;
        float start = MathF.Round((popup.Width - rowWidth) * 0.5f);
        for (int i = 0; i < row.Count; i++)
        {
            UiButton button = row[i];
            int index = i;
            button.LayoutPolicy = null;
            button.Anchors = AnchorEdges.None;
            button.Label = buttons[i];
            button.OnClick = () => dialog.ChooseByPress(index);
            if (row.Count == 1)
                button.Left = MathF.Round((popup.Width - buttonWidth) * 0.5f);
            else if (row.Count >= 3)
                button.Left = start + i * (buttonWidth + RowGap);
            // Two buttons keep the retail Yes/No places.
        }

        MakeClickThrough(popup);
        dialog.AddChild(popup);
        dialog.Width = popup.Width;
        dialog.Height = popup.Height;
        return dialog;
    }

    // As RetailConfirmationDialogView.SetMessage, with the title as a gold
    // first line: the popup grows by as much as the text does.
    private static void SetLines(UiElement popup, UiText text, string title, string message)
    {
        float basePopupHeight = popup.Height;
        float baseMessageHeight = text.Height;
        float maximumWidth = Math.Max(1f, text.Width - 2f * text.Padding);
        Func<string, float> measure = text.DatFont is { } font
            ? font.MeasureWidth
            : static value => value.Length * 8f;
        var lines = new List<UiText.Line>();
        if (title.Length > 0)
        {
            foreach (string line in UiText.WrapWords(title, measure, maximumWidth))
                lines.Add(new UiText.Line(line, TitleColor));
        }
        if (message.Length > 0)
        {
            foreach (string line in UiText.WrapWords(message, measure, maximumWidth))
                lines.Add(new UiText.Line(line, text.DefaultColor));
        }
        UiText.Line[] shown = lines.ToArray();
        text.LinesProvider = () => shown;

        float lineHeight = text.DatFont?.LineHeight ?? 16f;
        text.Height = Math.Max(baseMessageHeight, shown.Length * lineHeight);
        popup.Height = basePopupHeight + (text.Height - baseMessageHeight);
    }

    // The art and text are decoration: a press on them lands on the dialog,
    // which takes the keyboard focus. Only the buttons stay pressable.
    private static void MakeClickThrough(UiElement element)
    {
        if (element is UiButton)
            return;
        element.ClickThrough = true;
        foreach (UiElement child in element.Children)
            MakeClickThrough(child);
    }
}
