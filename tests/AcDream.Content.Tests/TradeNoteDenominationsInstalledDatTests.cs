using System.Collections.Generic;
using DatReaderWriter;
using DatReaderWriter.Options;

namespace AcDream.Content.Tests;

[Trait("Lane", "InstalledDat")]
public sealed class TradeNoteDenominationsInstalledDatTests
{
    [Fact]
    public void TheInstalledMapGivesEachNoteClassItsValue()
    {
        string? datDir = ContentConformanceDats.ResolveDatDir();
        if (datDir is null)
        {
            Assert.Fail(
                "Lane=InstalledDat requires an installed retail DAT directory; "
                + "see docs/release-gate.md.");
            return;
        }

        using var dats = new DatCollection(datDir, DatAccessType.Read);
        using var adapter = new DatCollectionAdapter(dats);
        IReadOnlyDictionary<uint, int> notes = TradeNoteDenominations.Load(adapter);

        Assert.Equal(14, notes.Count);
        Assert.Equal(100, notes[2621u]);
        Assert.Equal(500, notes[2622u]);
        Assert.Equal(1_000, notes[2623u]);
        Assert.Equal(5_000, notes[2624u]);
        Assert.Equal(10_000, notes[2625u]);
        Assert.Equal(250_000, notes[20630u]);
        Assert.False(notes.ContainsKey(273u));
    }
}
