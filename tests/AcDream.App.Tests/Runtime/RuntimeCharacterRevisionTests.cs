namespace AcDream.App.Tests.Runtime;

public sealed class RuntimeCharacterRevisionTests
{
    [Fact]
    public void RevisionsMoveWhenTheCharacterOrSpellbookChanges()
    {
        var runtime = GameRuntimeTestFactory.Create();
        long character = runtime.CharacterOwner.CharacterRevision;
        long spellbook = runtime.CharacterOwner.SpellbookRevision;

        runtime.CharacterOwner.LocalPlayer.OnVitalUpdate(vitalId: 1u, ranks: 0u, start: 10u, xp: 0u, current: 10u);
        Assert.True(runtime.CharacterOwner.CharacterRevision > character);

        runtime.CharacterOwner.Spellbook.OnSpellLearned(1);
        Assert.True(runtime.CharacterOwner.SpellbookRevision > spellbook);
    }
}
