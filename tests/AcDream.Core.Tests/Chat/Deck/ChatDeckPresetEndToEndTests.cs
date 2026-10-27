using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

/// <summary>
/// Presets against lines as ChatLog words and classifies them, not hand-made facts:
/// a change in wording or channels that breaks a preset fails here.
/// </summary>
public sealed class ChatDeckPresetEndToEndTests
{
    private const uint PlayerGuid = 0x50000002u;
    private const uint NpcGuid = 0x80000123u;

    private static readonly Dictionary<string, (Action<ChatLog> Hit, Action<ChatLog> Miss)> Cases = new()
    {
        ["social.general"] = (
            l => l.OnChannelBroadcast(0u, "Bob", "anyone around?", (uint)RetailLogTextType.TurbineGeneral, "General"),
            l => l.OnLocalSpeech("Bob", "[General] is quiet", PlayerGuid, isRanged: false, (uint)RetailLogTextType.Speech)),
        ["social.bot_spam"] = (
            l => l.OnLocalSpeech("BuffBot", "Buffs at the Holtburg lifestone -b-", PlayerGuid, isRanged: false, (uint)RetailLogTextType.Speech),
            l => l.OnLocalSpeech("BuffBot", "Buffs at the Holtburg lifestone", PlayerGuid, isRanged: false, (uint)RetailLogTextType.Speech)),
        ["combat.melee_dealt"] = (
            l => l.OnCombatLine("You slash Drudge Slinker for 54 points of slashing damage!", (uint)RetailLogTextType.CombatSelf),
            l => l.OnLocalSpeech("Bob", "You slash for fun", PlayerGuid, isRanged: false, (uint)RetailLogTextType.Speech)),
        ["cast.fizzle"] = (
            l => l.OnSystemMessage("Your spell fizzled.", (uint)RetailLogTextType.Magic),
            l => l.OnTellReceived("Bob", "Your spell fizzled again", PlayerGuid, (uint)RetailLogTextType.Tell)),
        ["npc.arbitrator_spam"] = (
            l => l.OnTellReceived("Master Arbitrator", "You shall be known as a champion!", NpcGuid, (uint)RetailLogTextType.Tell),
            l => l.OnTellReceived("Master Arbitrator", "Well done!", NpcGuid, (uint)RetailLogTextType.Tell)),
        ["item.salvage"] = (
            l => l.OnSystemMessage("You obtain 12 Steel (ws 7.00) using your knowledge of Weapon Tinkering.", (uint)RetailLogTextType.Salvaging),
            l => l.OnLocalSpeech("Bob", "You obtain 12 Steel (ws 7.00) using your knowledge of Weapon Tinkering.", PlayerGuid, isRanged: false, (uint)RetailLogTextType.Speech)),
        ["social.emotes"] = (
            l => l.OnEmote("Bob", "waves.", PlayerGuid),
            l => l.OnLocalSpeech("Bob", "waves", PlayerGuid, isRanged: false, (uint)RetailLogTextType.Speech)),
    };

    [Theory]
    [InlineData("social.general")]
    [InlineData("social.bot_spam")]
    [InlineData("combat.melee_dealt")]
    [InlineData("cast.fizzle")]
    [InlineData("npc.arbitrator_spam")]
    [InlineData("item.salvage")]
    [InlineData("social.emotes")]
    public void HidesTheRealLine_KeepsTheNearMiss(string presetId)
    {
        Assert.NotNull(ChatDeckPresets.Get(presetId));
        var settings = ChatDeckDefaults.Create() with
        {
            Rules =
            [
                new ChatDeckRule
                {
                    Id = "p", Kind = ChatDeckMatchKind.Preset, PresetId = presetId,
                    Action = ChatDeckRuleAction.Hide,
                },
            ],
        };
        var log = new ChatLog();
        using var model = new ChatDeckModel(log, settings, "Davian");
        (Action<ChatLog> hit, Action<ChatLog> miss) = Cases[presetId];

        hit(log);
        Assert.Single(log.Snapshot());
        model.Tick();
        Assert.Equal(0, model.FindTab(ChatDeckDefaults.AllTabId)!.Count);

        miss(log);
        model.Tick();
        Assert.Equal(1, model.FindTab(ChatDeckDefaults.AllTabId)!.Count);
    }
}
