using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

/// <summary>Channels as grouped checkboxes over one channel set: a tab's, or the rule form's.</summary>
public sealed class ChatDeckChannelPicker
{
    private readonly Func<IReadOnlySet<ChatDeckChannel>> _get;
    private readonly Action<IReadOnlySet<ChatDeckChannel>> _set;

    public ChatDeckChannelPicker(Func<IReadOnlySet<ChatDeckChannel>> get, Action<IReadOnlySet<ChatDeckChannel>> set)
    {
        _get = get;
        _set = set;
        Groups = ChatDeckChannels.Groups.Select(g => new GroupRow(this, g)).ToList();
    }

    public IReadOnlyList<GroupRow> Groups { get; }

    private bool Has(ChatDeckChannel c) => _get().Contains(c);

    private void Set(IEnumerable<ChatDeckChannel> channels, bool on)
    {
        var next = _get().ToHashSet();
        foreach (ChatDeckChannel c in channels)
            if (on) next.Add(c);
            else next.Remove(c);
        _set(next);
    }

    /// <summary>One group: a box for the whole group (null when only some are on), then its channels.</summary>
    public sealed class GroupRow(ChatDeckChannelPicker owner, ChatDeckChannelGroup group)
    {
        public string Name => group.Name;
        public bool? All
        {
            get
            {
                int on = group.Channels.Count(owner.Has);
                return on == 0 ? false : on == group.Channels.Count ? true : null;
            }
        }
        public Action<bool> SetAll => on => owner.Set(group.Channels, on);
        public IReadOnlyList<ChannelRow> Channels { get; } = group.Channels.Select(c => new ChannelRow(owner, c)).ToList();
    }

    public sealed class ChannelRow(ChatDeckChannelPicker owner, ChatDeckChannel channel)
    {
        public string Id => channel.ToString();
        public string Name => ChatDeckChannels.DisplayName(channel);
        public bool On => owner.Has(channel);
        public Action<bool> SetOn => on => owner.Set([channel], on);
    }
}
