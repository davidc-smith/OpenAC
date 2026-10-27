using AcDream.App.UI.Hud;
using AcDream.Core.Combat;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.Tests.UI.Hud;

public sealed class CommandDeckModelTests
{
    private static CommandDeckInput Input(
        CombatMode mode = CombatMode.Melee,
        DeckVitalInput? health = null,
        bool pending = false, string? pendingName = null, string? lastName = null,
        JumpChargeSnapshot jump = default,
        long session = 1) => new(
            Health: health ?? new DeckVitalInput(0.61f, 412, 675),
            Stamina: new DeckVitalInput(0.88f, 390, 443),
            Mana: new DeckVitalInput(0.45f, 180, 400),
            Mode: mode,
            DesiredPower: 0.75f,
            PowerBarLevel: 0.4f,
            RequestedHeight: AttackHeight.Medium,
            HasPendingCast: pending,
            PendingSpellName: pendingName,
            OutlinedSpellName: lastName,
            Jump: jump,
            RepeatAttacks: true,
            AutoTarget: false,
            KeepInView: false,
            SessionKey: session);

    [Theory]
    [InlineData(CombatMode.Melee, DeckContextRow.Power, true)]
    [InlineData(CombatMode.Missile, DeckContextRow.Power, true)]
    [InlineData(CombatMode.Magic, DeckContextRow.Casting, false)]
    [InlineData(CombatMode.NonCombat, DeckContextRow.Recall, false)]
    [InlineData(CombatMode.Undef, DeckContextRow.Recall, false)]
    public void RowAndStudsFollowTheMode(CombatMode mode, DeckContextRow row, bool studs)
    {
        CommandDeckView view = new CommandDeckModel().Update(Input(mode), 0.0);
        Assert.Equal(row, view.Row);
        Assert.Equal(studs, view.ShowStuds);
    }

    [Fact]
    public void PowerLabelIsAccuracyForMissile()
    {
        var model = new CommandDeckModel();
        Assert.Equal("POWER", model.Update(Input(CombatMode.Melee), 0).PowerLabel);
        Assert.Equal("ACCURACY", model.Update(Input(CombatMode.Missile), 0).PowerLabel);
    }

    [Fact]
    public void VitalText()
    {
        var model = new CommandDeckModel();
        CommandDeckView known = model.Update(Input(), 0);
        Assert.Equal("412 / 675", known.Health.Text);

        CommandDeckView percentOnly = model.Update(
            Input(health: new DeckVitalInput(0.61f, null, null)), 0);
        Assert.Equal("61%", percentOnly.Health.Text);

        CommandDeckView unknown = new CommandDeckModel().Update(
            Input(health: new DeckVitalInput(null, null, null)), 0);
        Assert.False(unknown.Health.Known);
        Assert.Equal("-", unknown.Health.Text);
        Assert.Equal(0f, unknown.Health.Fill);
        Assert.Equal(0f, unknown.Health.Trail);
    }

    [Fact]
    public void LowHealthBelowAQuarter()
    {
        var model = new CommandDeckModel();
        Assert.False(model.Update(Input(health: new DeckVitalInput(0.25f, 25, 100)), 0).LowHealth);
        Assert.True(model.Update(Input(health: new DeckVitalInput(0.24f, 24, 100)), 0).LowHealth);
    }

    [Theory]
    [InlineData(true, "Flame Bolt", null, "Flame Bolt", true)]
    [InlineData(true, null, "Frost Bolt", "Casting...", true)]
    [InlineData(false, null, "Frost Bolt", "Frost Bolt", false)]
    [InlineData(false, null, null, "No spell selected", false)]
    public void CastingText_PendingThenOutlined(bool pending, string? pendingName, string? outlined, string text, bool lit)
    {
        var view = new CommandDeckModel().Update(default(CommandDeckInput) with
        {
            Mode = CombatMode.Magic, HasPendingCast = pending,
            PendingSpellName = pendingName, OutlinedSpellName = outlined,
        }, 0);
        Assert.Equal(text, view.CastingText);
        Assert.Equal(lit, view.CastingLit);
        Assert.Equal(!pending && outlined is not null, view.CastEnabled);
        Assert.Equal(outlined is null && !pending, view.CastingDim);
    }

    [Fact]
    public void JumpFillOnlyWhileCharging()
    {
        var model = new CommandDeckModel();
        Assert.Equal(0f, model.Update(Input(jump: new JumpChargeSnapshot(false, 0.7f)), 0).JumpFill);
        CommandDeckView charging = model.Update(Input(jump: new JumpChargeSnapshot(true, 0.7f)), 0);
        Assert.Equal(0.7f, charging.JumpFill);
        Assert.True(charging.JumpCharging);
    }

    [Fact]
    public void SessionChangeReseedsTrails()
    {
        var model = new CommandDeckModel();
        model.Update(Input(health: new DeckVitalInput(1.0f, 100, 100)), 0.0);
        CommandDeckView dropped = model.Update(Input(health: new DeckVitalInput(0.5f, 50, 100)), 1.0);
        Assert.Equal(1.0f, dropped.Health.Trail);

        CommandDeckView newSession = model.Update(
            Input(health: new DeckVitalInput(0.3f, 30, 100), session: 2), 1.05);
        Assert.Equal(0.3f, newSession.Health.Trail);
    }

    /// <summary>DefaultFont bakes only printable ASCII, so nothing the deck draws may use other characters.</summary>
    [Fact]
    public void EveryDeckStringIsAscii()
    {
        CombatMode[] modes = [CombatMode.NonCombat, CombatMode.Melee, CombatMode.Missile, CombatMode.Magic];
        DeckVitalInput[] vitals = [new(0.61f, 412, 675), new(null, null, null)];
        (bool Pending, string? PendingName, string? Last)[] casts =
        [
            (false, null, null), (true, null, null), (true, "Flame Bolt", "Frost Bolt"), (false, null, "Frost Bolt"),
        ];
        JumpChargeSnapshot[] jumps = [default, new(true, 0.4f)];

        foreach (CombatMode mode in modes)
        foreach (DeckVitalInput vital in vitals)
        foreach (var cast in casts)
        foreach (JumpChargeSnapshot jump in jumps)
        {
            var input = Input(mode, vital, cast.Pending, cast.PendingName, cast.Last, jump) with
            {
                Stamina = vital, Mana = vital,
            };
            CommandDeckView view = new CommandDeckModel().Update(input, 0.0);
            var strings = new List<string?>
            {
                view.Health.Text, view.Stamina.Text, view.Mana.Text, view.CastingText, view.PowerLabel,
            };
            foreach (DeckZone zone in Enum.GetValues<DeckZone>())
            {
                HudTooltipContent? tip = UiCommandDeck.TooltipFor(zone, view);
                if (tip is null) continue;
                strings.AddRange([tip.Title, tip.State, tip.Detail, tip.Hint]);
            }
            foreach (string? text in strings)
                if (text is not null)
                    Assert.True(text.All(c => c >= ' ' && c <= '~'), $"{mode}: \"{text}\"");
        }
    }
}
