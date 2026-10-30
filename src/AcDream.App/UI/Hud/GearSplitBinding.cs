namespace AcDream.App.UI.Hud;

/// <summary>The split picker: how many to take off a stack, and where they go. Built once and re-pointed per open.</summary>
public sealed class GearSplitBinding
{
    private GearSplitRequest? _request;
    private Func<uint, uint, int, int, bool>? _send;

    public const string Markup = """
        <panel x="120" y="120" w="260" h="146" title="Split stack" theme="plugin" layout="column" padding="10" gap="8">
          <label text="{Caption}" h="20" />
          <group layout="row" gap="8" align="center" h="22">
            <label text="Amount" w="60" />
            <number value="{Amount}" min="1" max="999999" step="1" onchange="{SetAmount}" w="90" />
            <button text="Half" onclick="{Half}" w="50" h="22" />
          </group>
          <group layout="row" gap="8" h="22">
            <group grow="1" />
            <button text="Cancel" onclick="{Cancel}" w="70" h="22" />
            <button text="Split" onclick="{Ok}" w="70" h="22" />
          </group>
        </panel>
        """;

    internal void Open(GearSplitRequest request, Func<uint, uint, int, int, bool> send)
    {
        _request = request;
        _send = send;
        Amount = Math.Max(1, request.Max / 2);
    }

    public string Caption => _request is { } r ? $"{r.Name} ({r.Max})" : "";
    public int Amount { get; private set; } = 1;
    public Action<int> SetAmount => v => Amount = _request is { } r ? Math.Clamp(v, 1, Math.Max(1, r.Max - 1)) : 1;
    public Action Half => () => SetAmount(_request is { } r ? r.Max / 2 : 1);
    public Action? Close { get; set; }
    public Action Cancel => () => { _request = null; Close?.Invoke(); };
    public Action Ok => () =>
    {
        if (_request is not { } r || _send is null) return;
        if (!_send(r.Stack, r.Container, r.Slot, Amount)) return;   // refused: stay open to retry or cancel
        _request = null;
        Close?.Invoke();
    };
}
