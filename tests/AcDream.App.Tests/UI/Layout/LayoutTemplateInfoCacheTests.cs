using System;
using System.Collections.Generic;
using AcDream.App.UI.Layout;

namespace AcDream.App.Tests.UI.Layout;

public sealed class LayoutTemplateInfoCacheTests
{
    [Fact]
    public void EachTemplateIsReadOnceForAnyNumberOfRows()
    {
        var reads = new List<(uint, uint)>();
        var cache = new LayoutTemplateInfoCache((layout, element) =>
        {
            reads.Add((layout, element));
            return new ElementInfo { Id = element };
        });

        ElementInfo? first = null;
        for (int row = 0; row < 150; row++)
        {
            ElementInfo? info = cache.Get(0x21000009u, 0x10000040u);
            first ??= info;
            Assert.Same(first, info);
        }
        ElementInfo? other = cache.Get(0x21000009u, 0x10000041u);

        Assert.Equal(0x10000041u, other!.Id);
        Assert.Equal(
            new[] { (0x21000009u, 0x10000040u), (0x21000009u, 0x10000041u) },
            reads);
    }

    [Fact]
    public void AMissingTemplateIsReadOnce()
    {
        int reads = 0;
        var cache = new LayoutTemplateInfoCache((_, _) =>
        {
            reads++;
            return null;
        });

        Assert.Null(cache.Get(1u, 2u));
        Assert.Null(cache.Get(1u, 2u));
        Assert.Equal(1, reads);
    }
}
