# Markup controls, plan 12: `<color>` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `<color>` markup element shows a bound colour as a swatch. Clicking it (or Enter/Space while focused) opens a picker popup. The popup has a saturation/value square, a hue strip, an optional alpha strip, a hex field, palette swatches and recent colours. It reports the edited colour to the plugin live.

**Architecture:**
- **Value codec.** `Markup/MarkupColorValue` handles packed `0xAARRGGBB`: text form, strict and lenient parse, HSV both ways. `MarkupBindings.BindRequiredColor` turns a `value`/`onchange` pair of `uint`, `int` or `string` into one `Func<uint>`/`Action<uint>` pair.
- **Textures.** A new public `IMarkupTextureService` (upload RGBA8, release) is backed in the client by `RetailMarkupTextureService` over `TextureCache.UploadReleasableRgba8`/`ReleaseUiTexture`. The popup uploads four **static** gradient textures when it opens and frees them when it closes:
  - white→clear (horizontal) and clear→black (vertical), drawn over a fill of the pure hue, which gives the exact HSV square;
  - a hue strip;
  - a white alpha ramp, tinted with the current RGB.
  - Nothing is rebuilt when the hue changes. With no service (tests, older callers) or a failed upload, it draws coarse grids of fills instead.
- **Recent colours.** `Markup/MarkupRecentColors` holds up to 8 colours, newest first, shared by every picker. It is remembered per character through a new `MarkupControlStateStore.Shared` state (window `*`, control `color`, kind `recent`) using the existing `RememberedValue` generation contract. With a null state it is in memory only, one list per document.
- **Widgets.** `UiMarkupColor` is the swatch: a tab stop that doesn't take focus on click. Its child `UiMarkupColorPopup` takes no room and draws in the overlay pass, like `UiMarkupContextMenu`. While open, the popup holds the root's popup slot and the keyboard focus (so Escape and hex typing reach it), with `IsEditControl` set so `UiRoot.OnChar` delivers characters. Edits set a pending colour that is reported on the next tick (at most once per frame) and on close.
- **Wiring.** `MarkupBuildContext` gains `Textures` and `RecentColors`. `MarkupDocument.Build` gains `textures` and `sharedControlState`, and creates one `MarkupRecentColors` per build. `RetailUiRuntime.MountPlugins` passes the retail service and `_controlState.Shared`.

**Status:** not started.

**Tech Stack:** C# / .NET 10, xUnit, headless `UiRoot` + `RecordingGpuDevice` (`MarkupDrawProbe`) tests, Silk.NET `Key`.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md` (branch `docs/markup-controls-spec`), "Shared rules", section 1 (`IMarkupTextureService`) and section 13.

## Global Constraints

- **Workspace.** Worktree `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls`, branch `feat/markup-controls` (HEAD 618e80d4, from upstream 391e709a).
  - Never merge fork `main` in, and never add `docs/superpowers/` or `.superpowers/` files.
  - Push only when the controller says so.
- **Build hygiene.** Every shell runs `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH` first. After building, run `git checkout -- '*.lock.json'`; never commit lock files.
- **Commits.** Every commit must build and pass its own tests. Commit messages end with `Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC`.
- **Helper conventions.** `BindRequired*` throw `FormatException` when the attribute is absent or mistyped. The contexts are `"color value"` / `"color onchange"`. The `<color>` case adds exactly one element to its parent (the swatch; the popup is its child), so the theme `Register` still fires.
- **Colour values.**
  - Packed `0xAARRGGBB`, the byte order of the `#AARRGGBB` literal.
  - A bound `string` is read strictly: exactly `#` + 8 hex digits, any case. Anything else, including null, reads as opaque white `0xFFFFFFFF`, as a bound colour attribute does.
  - A string is written as `#AARRGGBB` in upper case. `int` is the same bits (`unchecked`).
  - The hex field accepts an optional `#`, then 6 or 8 hex digits, any case, surrounding spaces ignored. Six digits keep the current alpha.
- **`<color>` contract.**
  - `value`: required binding of type `uint`, `int` or `string`.
  - `onchange`: required `Action<T>` of the same `T`. Any mismatch throws at build.
  - `alpha`: `true`/`false`, default `false`; any other text throws `<color alpha="…"> must be true or false`. Without alpha, every reported colour has alpha `FF`.
  - Common attributes (`name id visible enabled tooltip anchor`) and `x y w h`. No others.
- **Popup layout** (popup-local, `UiMarkupColorPopup` constants):
  - `Pad = 6`. The square is at (6, 6), 128×128. The hue strip is at x = 140, 16×128. The alpha strip is at x = 162, 16×128, only with alpha.
  - Width is 162 without alpha, 184 with alpha.
  - The hex field is at (6, 140), width − 12 wide, 18 high.
  - The palette row is at y = 164 and the recent row at y = 180. Swatches are 14×14 on a 16 px step from x = 6.
  - Height is 200.
  - The popup sits 2 px below the swatch, left edges aligned. If it would cross the bottom of the screen it goes 2 px above the swatch instead. It is then clamped onto the screen on both axes.
- **Popup behaviour.**
  - **Opening.**
    - A click or Enter/Space on an enabled swatch opens the popup. Opening reads the bound colour once (`opened`).
    - The working colour is `opened`, with alpha forced to `FF` without alpha. The working HSV comes from it.
    - Grey (s = 0) keeps the previous hue; black (v = 0) keeps the previous hue and saturation. The hue starts at 0 on each open.
    - The popup takes the keyboard focus and remembers what had it.
  - **Editing the colour.**
    - **Square:** press or drag. s = x/128, v = 1 − y/128, clamped.
    - **Hue strip:** press or drag. h = y/128 × 360, top = red, clamped.
    - **Alpha strip:** press or drag. a = 1 − y/128, so the top is opaque.
    - **Palette or recent swatch:** press sets that colour.
    - Every edit sets a **pending** colour.
  - **Reporting.**
    - On each popup tick, a pending colour is reported through the swatch's `Changed`, if it differs from the bound value. It is then cleared. So `onchange` fires at most once per frame.
  - **Hex field.**
    - A press on the field starts editing with the current colour's text: `#RRGGBB`, or `#AARRGGBB` with alpha.
    - Characters `0-9 a-f A-F #` append, up to 9. Backspace deletes the last one.
    - Enter applies. A press anywhere else in the popup applies (focus loss), and so does any closing except Escape.
    - Applying text that doesn't parse leaves the colour unchanged.
  - **Closing and keeping the colour.**
    - These close the popup and keep the colour:
      - a press outside the popup (`UiRoot` swallows it), including on the swatch;
      - Enter while the hex field isn't being edited;
      - Tab, or anything else that takes the focus;
      - the swatch being disabled (on the next tick) or hidden or removed (the root's popup dismiss).
    - Keeping applies the hex text and reports the pending colour.
    - If the final colour differs from `opened`, it is added to the recent colours.
  - **Escape** closes the popup and drops the pending colour. If the bound value now differs from `opened`, it reports `opened`. Nothing is added to the recent colours.
  - **After closing.**
    - The textures are released and the popup slot is cleared.
    - Focus goes back to what had it, if the popup still has it.
    - Plugin callbacks run last, after all of that.
  - **Swallowed input.** All other keys and mouse buttons go to the popup while it is open and are swallowed.
- **Swatch rows.**
  - Palette with `theme="plugin"`: the palette's Background, Field, Border, Text, Muted, Accent and Selected, in that order.
  - Palette in Classic: `0xFFFFFFFF, 0xFF000000, 0xFF808080, 0xFFD03030, 0xFFFFD140, 0xFF40B040, 0xFF4070D0`.
  - Recent row: the recent colours. Empty slots are drawn as outlines only.
- **Looks.**
  - **Swatch:**
    - A 1 px outline: Classic `UiCheckLamp.UncheckedInner`, themed `Border`. While focused or open it is `UiMarkupHotkey.FocusColor` or themed `Accent`.
    - The colour fills the inside (inset 1 px). When alpha < `FF` a 4 px checkerboard (`(0.8,0.8,0.8,1)` / `(0.5,0.5,0.5,1)`) is drawn behind it.
    - Disabled: alpha × `UiMarkupToggle.DisabledAlpha`.
  - **Popup:**
    - Background, border and text as `UiMarkupContextMenu`: Classic constants, themed `Field`/`Border`/`Text`.
    - Hex field: `UiMarkupHotkey` Classic background/border, or themed `Field`/`Border`. The border is the focus colour while editing.
    - Markers: a 7×7 black outline around a 5×5 white outline at the square point. On each strip, a 3 px black bar with a 1 px white line.
    - It draws at full alpha (`PushAlphaAbsolute(1f)`), like the context menu.

**User decisions (already made):**
- One branch, one commit series per control, upstream-portable.
- Non-text controls don't take focus on click, except text-editing controls (`<number>`, the colour popup's hex field) and `<hotkey>` (spec "Keyboard").
- Remembered state follows the "both" rule. Recent colours are "remembered per character, shared by all `<color>`s" (spec section 13).

**Plan choices (details the spec leaves open; flag in the final review if wrong):**
- **Static gradient textures** layered over a hue fill, uploaded on open and freed on close, instead of a square texture rebuilt on every hue change. The result looks the same with far fewer uploads.
- The popup takes the keyboard focus for its whole lifetime, as the context menu does, not only while the hex field is edited. This is how Escape reaches it, and the game gets the focus back when it closes.
- The Classic palette row is a fixed 7-colour set (Classic has no palette tokens).
- Recent colours are stored under the reserved window key `*`. A colour is "picked" when the popup closes with a different colour (not on Escape).
- With six hex digits the current alpha is kept. Enter outside the hex field closes the popup.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.App/UI/Markup/MarkupColorValue.cs` (new) | Packed colour text, parse, HSV |
| `src/AcDream.App/UI/Markup/MarkupBindings.cs` | `BindRequiredColor` |
| `src/AcDream.App/UI/Markup/IMarkupTextureService.cs` (new) | Upload/release contract |
| `src/AcDream.App/UI/Markup/MarkupColorRamps.cs` (new) | Gradient pixel buffers |
| `src/AcDream.App/UI/Markup/MarkupRecentColors.cs` (new) | Shared recent list + codec |
| `src/AcDream.App/UI/Markup/MarkupControlStateStore.cs` | `SharedWindow`, `Shared` |
| `src/AcDream.App/UI/UiMarkupColor.cs` (new) | Swatch widget |
| `src/AcDream.App/UI/UiMarkupColorPopup.cs` (new) | Popup widget |
| `src/AcDream.App/UI/Markup/MarkupColor.cs` (new) | Builder |
| `src/AcDream.App/UI/Markup/MarkupBuildContext.cs` | `Textures`, `RecentColors` |
| `src/AcDream.App/UI/MarkupDocument.cs` | `textures`, `sharedControlState` params; `case "color"` |
| `src/AcDream.App/UI/PluginMarkupTheme.cs` | Font + palette for `UiMarkupColor` |
| `src/AcDream.App/UI/RetailMarkupTextureService.cs` (new) | `TextureCache`-backed service |
| `src/AcDream.App/UI/RetailUiRuntime.cs` | Passes textures + shared state |
| `tests/AcDream.App.Tests/UI/MarkupColorValueTests.cs`, `MarkupColorSupportTests.cs`, `UiMarkupColorTests.cs`, `MarkupColorTests.cs` (new) | Tests |
| `docs/plugin-ui-markup.md` | Colour picker docs |

---

### Task 1: Colour value codec and colour bind helper

**Goal:** `MarkupColorValue` (format, parse, HSV) and `MarkupBindings.BindRequiredColor` exist with tests.

**Files:**
- Create: `src/AcDream.App/UI/Markup/MarkupColorValue.cs`
- Modify: `src/AcDream.App/UI/Markup/MarkupBindings.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupColorValueTests.cs`

**Acceptance Criteria:**
- [ ] `Format(0x80FF0000)` = `#80FF0000`; `Format(0x80FF0000, withAlpha: false)` = `#FF0000`.
- [ ] `Parse` accepts `#RRGGBB`/`RRGGBB`/`#AARRGGBB`, any case, trimmed; six digits take `alphaForRgb`; rejects other lengths, non-hex, null.
- [ ] `ParseStrict` accepts only `#` + 8 hex digits.
- [ ] `ToHsv`/`FromHsv` agree with known values (red, cyan, 50 % grey, `#804040` = hsv(0, 0.5, 0.5)) and round-trip a sample of colours exactly.
- [ ] `BindRequiredColor` reads/writes `uint`, `int` (same bits) and `string` (`#AARRGGBB` upper case; bad text reads white); throws `FormatException` naming `color value`/`color onchange` for absent, literal, missing or mistyped bindings, and for an `onchange` whose type differs from `value`'s.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~MarkupColorValueTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — `tests/AcDream.App.Tests/UI/MarkupColorValueTests.cs`:

```csharp
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

public class MarkupColorValueTests
{
    [Fact]
    public void Format_WritesUpperCaseHex()
    {
        Assert.Equal("#80FF0000", MarkupColorValue.Format(0x80FF0000u));
        Assert.Equal("#FF0000", MarkupColorValue.Format(0x80FF0000u, withAlpha: false));
        Assert.Equal("#00ABCDEF", MarkupColorValue.Format(0x00ABCDEFu));
    }

    [Theory]
    [InlineData("#FF0000", 0xFFFF0000u)]
    [InlineData("ff0000", 0xFFFF0000u)]
    [InlineData("  #80ff0000 ", 0x80FF0000u)]
    [InlineData("80FF0000", 0x80FF0000u)]
    public void Parse_AcceptsSixOrEightDigits(string text, uint expected) =>
        Assert.Equal(expected, MarkupColorValue.Parse(text));

    [Fact]
    public void Parse_SixDigitsTakeTheGivenAlpha() =>
        Assert.Equal(0x40123456u, MarkupColorValue.Parse("#123456", alphaForRgb: 0x40));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#123456789")]
    [InlineData("#GG0000")]
    [InlineData("##FF0000")]
    public void Parse_RejectsEverythingElse(string? text) => Assert.Null(MarkupColorValue.Parse(text));

    [Theory]
    [InlineData("#80ff0000", 0x80FF0000u)]
    [InlineData("#FFFFFFFF", 0xFFFFFFFFu)]
    public void ParseStrict_AcceptsOnlyHashAndEightDigits(string text, uint expected) =>
        Assert.Equal(expected, MarkupColorValue.ParseStrict(text));

    [Theory]
    [InlineData("FF000000")]
    [InlineData("#FF0000")]
    [InlineData(" #FF000000")]
    [InlineData(null)]
    public void ParseStrict_RejectsOtherForms(string? text) => Assert.Null(MarkupColorValue.ParseStrict(text));

    [Theory]
    [InlineData(0xFFFF0000u, 0f, 1f, 1f)]
    [InlineData(0xFF00FFFFu, 180f, 1f, 1f)]
    [InlineData(0xFF808080u, 0f, 0f, 0.502f)]
    [InlineData(0xFF804040u, 0f, 0.5f, 0.502f)]
    [InlineData(0xFF000000u, 0f, 0f, 0f)]
    public void ToHsv_KnownColours(uint argb, float h, float s, float v)
    {
        var (gotH, gotS, gotV) = MarkupColorValue.ToHsv(argb);
        Assert.Equal(h, gotH, 1);
        Assert.Equal(s, gotS, 2);
        Assert.Equal(v, gotV, 2);
    }

    [Theory]
    [InlineData(0f, 1f, 1f, 0xFFFF0000u)]
    [InlineData(180f, 1f, 1f, 0xFF00FFFFu)]
    [InlineData(0f, 0.5f, 0.5f, 0xFF804040u)]
    [InlineData(360f, 1f, 1f, 0xFFFF0000u)]
    [InlineData(120f, 1f, 1f, 0xFF00FF00u)]
    [InlineData(240f, 1f, 1f, 0xFF0000FFu)]
    public void FromHsv_KnownColours(float h, float s, float v, uint expected) =>
        Assert.Equal(expected, MarkupColorValue.FromHsv(h, s, v, 0xFF));

    [Fact]
    public void Hsv_RoundTripsEveryByteColourInASample()
    {
        for (uint i = 0; i < 4096; i++)
        {
            uint r = (i * 37u) & 0xFF, g = (i * 101u + 13u) & 0xFF, b = (i * 211u + 7u) & 0xFF;
            uint argb = 0xFF000000u | r << 16 | g << 8 | b;
            var (h, s, v) = MarkupColorValue.ToHsv(argb);
            Assert.Equal(argb, MarkupColorValue.FromHsv(h, s, v, 0xFF));
        }
    }

    [Fact]
    public void FromHsv_KeepsTheGivenAlpha() =>
        Assert.Equal(0x40FF0000u, MarkupColorValue.FromHsv(0f, 1f, 1f, 0x40));

    private sealed class Binding
    {
        public uint U { get; set; } = 0x80112233u;
        public int I { get; set; } = unchecked((int)0xFF445566u);
        public string? S { get; set; } = "#ff778899";
        public double D { get; set; }
        public uint? LastU;
        public int? LastI;
        public string? LastS;
        public Action<uint> SetU => v => LastU = v;
        public Action<int> SetI => v => LastI = v;
        public Action<string> SetS => v => LastS = v;
    }

    [Fact]
    public void BindRequiredColor_Uint()
    {
        var b = new Binding();
        var (read, write) = MarkupBindings.BindRequiredColor("{U}", "{SetU}", b, "color");
        Assert.Equal(0x80112233u, read());
        write(0x01020304u);
        Assert.Equal(0x01020304u, b.LastU);
    }

    [Fact]
    public void BindRequiredColor_IntIsTheSameBits()
    {
        var b = new Binding();
        var (read, write) = MarkupBindings.BindRequiredColor("{I}", "{SetI}", b, "color");
        Assert.Equal(0xFF445566u, read());
        write(0xFF000001u);
        Assert.Equal(unchecked((int)0xFF000001u), b.LastI);
    }

    [Fact]
    public void BindRequiredColor_StringReadsStrictlyAndWritesUpperCase()
    {
        var b = new Binding();
        var (read, write) = MarkupBindings.BindRequiredColor("{S}", "{SetS}", b, "color");
        Assert.Equal(0xFF778899u, read());
        b.S = "#778899";   // not #AARRGGBB: white, as a bound colour attribute
        Assert.Equal(0xFFFFFFFFu, read());
        b.S = null;
        Assert.Equal(0xFFFFFFFFu, read());
        write(0x0A0B0C0Du);
        Assert.Equal("#0A0B0C0D", b.LastS);
    }

    [Theory]
    [InlineData(null, "{SetU}", "color value")]
    [InlineData("#FF000000", "{SetU}", "color value")]
    [InlineData("{Missing}", "{SetU}", "color value")]
    [InlineData("{D}", "{SetU}", "color value")]
    [InlineData("{U}", null, "color onchange")]
    [InlineData("{U}", "{Missing}", "color onchange")]
    [InlineData("{U}", "{SetI}", "color onchange")]
    [InlineData("{I}", "{SetU}", "color onchange")]
    [InlineData("{S}", "{SetU}", "color onchange")]
    public void BindRequiredColor_BadBindingsThrow(string? value, string? onchange, string context)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredColor(value, onchange, new Binding(), "color"));
        Assert.Contains(context, ex.Message);
    }
}
```

- [ ] **Step 2: Run, expect a compile failure** (`MarkupColorValue` missing):

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~MarkupColorValueTests" 2>&1 | tail -5
```

- [ ] **Step 3: `src/AcDream.App/UI/Markup/MarkupColorValue.cs`**

```csharp
using System.Globalization;
using System.Numerics;

namespace AcDream.App.UI.Markup;

/// <summary>
/// Colours as the colour picker handles them: packed 0xAARRGGBB (the byte
/// order of the #AARRGGBB literal), their text, and HSV with the hue in
/// degrees [0, 360) and saturation and value in [0, 1].
/// </summary>
internal static class MarkupColorValue
{
    public const uint White = 0xFFFFFFFFu;

    public static string Format(uint argb, bool withAlpha = true) =>
        withAlpha ? $"#{argb:X8}" : $"#{argb & 0xFFFFFFu:X6}";

    /// <summary>
    /// What the hex field takes: an optional '#', then 6 or 8 hex digits, any
    /// case, surrounding spaces ignored. Six digits get <paramref name="alphaForRgb"/>.
    /// </summary>
    public static uint? Parse(string? text, byte alphaForRgb = 0xFF)
    {
        if (text is null)
            return null;
        ReadOnlySpan<char> digits = text.AsSpan().Trim();
        if (digits.Length > 0 && digits[0] == '#')
            digits = digits[1..];
        if (digits.Length is not (6 or 8))
            return null;
        foreach (char c in digits)
        {
            if (!char.IsAsciiHexDigit(c))
                return null;
        }
        uint value = uint.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return digits.Length == 6 ? (uint)alphaForRgb << 24 | value : value;
    }

    /// <summary>Exactly '#' and 8 hex digits, as a bound or stored colour string.</summary>
    public static uint? ParseStrict(string? text) =>
        text is { Length: 9 } && text[0] == '#' ? Parse(text) : null;

    public static byte Alpha(uint argb) => (byte)(argb >> 24);

    public static uint WithAlpha(uint argb, byte alpha) => (argb & 0xFFFFFFu) | (uint)alpha << 24;

    public static Vector4 ToVector4(uint argb) => new(
        ((argb >> 16) & 0xFFu) / 255f,
        ((argb >> 8) & 0xFFu) / 255f,
        (argb & 0xFFu) / 255f,
        (argb >> 24) / 255f);

    public static uint FromVector4(Vector4 color) =>
        Pack(ToByte(color.W), ToByte(color.X), ToByte(color.Y), ToByte(color.Z));

    public static uint Pack(byte a, byte r, byte g, byte b) =>
        (uint)a << 24 | (uint)r << 16 | (uint)g << 8 | b;

    public static byte ToByte(float channel) =>
        (byte)MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f, MidpointRounding.AwayFromZero);

    /// <summary>The colour's hue, saturation and value; grey has hue 0.</summary>
    public static (float H, float S, float V) ToHsv(uint argb)
    {
        float r = ((argb >> 16) & 0xFFu) / 255f;
        float g = ((argb >> 8) & 0xFFu) / 255f;
        float b = (argb & 0xFFu) / 255f;
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float delta = max - min;
        float s = max <= 0f ? 0f : delta / max;
        float h;
        if (delta <= 0f)
            h = 0f;
        else if (max == r)
            h = 60f * ((g - b) / delta);
        else if (max == g)
            h = 60f * ((b - r) / delta + 2f);
        else
            h = 60f * ((r - g) / delta + 4f);
        if (h < 0f)
            h += 360f;
        return (h, s, max);
    }

    /// <summary>The colour at a hue (any degrees, wrapped), saturation and value, with <paramref name="alpha"/>.</summary>
    public static uint FromHsv(float h, float s, float v, byte alpha)
    {
        h = ((h % 360f) + 360f) % 360f;
        s = Math.Clamp(s, 0f, 1f);
        v = Math.Clamp(v, 0f, 1f);
        float c = v * s;
        float x = c * (1f - MathF.Abs(h / 60f % 2f - 1f));
        float m = v - c;
        (float r, float g, float b) = (int)(h / 60f) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return Pack(alpha, ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }
}
```

- [ ] **Step 4: `BindRequiredColor`** — in `src/AcDream.App/UI/Markup/MarkupBindings.cs`, after `BindRequiredStringAction`:

```csharp
    /// <summary>
    /// A required colour pair: <c>value</c> a <c>uint</c>, <c>int</c> or
    /// <c>string</c> (#AARRGGBB) property and <c>onchange</c> an <c>Action</c> of
    /// the same type, read and written as packed 0xAARRGGBB. Text that is not
    /// exactly #AARRGGBB reads as opaque white, as a bound colour attribute does.
    /// <c>element</c> is the element name; the contexts are "&lt;element&gt; value"
    /// and "&lt;element&gt; onchange".
    /// </summary>
    public static (Func<uint> Read, Action<uint> Write) BindRequiredColor(
        string? value, string? onchange, object binding, string element)
    {
        string valueContext = element + " value";
        string changeContext = element + " onchange";
        if (string.IsNullOrWhiteSpace(value) || !MarkupDocument.IsBinding(value))
            throw new FormatException($"{valueContext} must be a uint, int or string binding");
        PropertyInfo? property = binding.GetType().GetProperty(value[1..^1]);
        Type? type = property?.PropertyType;
        if (property is null || (type != typeof(uint) && type != typeof(int) && type != typeof(string)))
        {
            throw new FormatException(
                $"{value} did not resolve to a uint, int or string property on "
                + binding.GetType().Name + $" ({valueContext})");
        }
        if (type == typeof(uint))
        {
            Action<uint> set = BindOptionalAction<uint>(onchange, binding, changeContext, "uint")
                ?? throw new FormatException($"{changeContext} must be an Action<uint> binding");
            return (() => property.GetValue(binding) is uint packed ? packed : MarkupColorValue.White, set);
        }
        if (type == typeof(int))
        {
            Action<int> set = BindOptionalAction<int>(onchange, binding, changeContext, "int")
                ?? throw new FormatException($"{changeContext} must be an Action<int> binding");
            return (() => property.GetValue(binding) is int packed ? unchecked((uint)packed) : MarkupColorValue.White,
                packed => set(unchecked((int)packed)));
        }
        Action<string> setText = BindOptionalAction<string>(onchange, binding, changeContext, "string")
            ?? throw new FormatException($"{changeContext} must be an Action<string> binding");
        return (() => MarkupColorValue.ParseStrict(property.GetValue(binding) as string) ?? MarkupColorValue.White,
            packed => setText(MarkupColorValue.Format(packed)));
    }
```

- [ ] **Step 5: Run the tests**

```bash
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~MarkupColorValueTests" 2>&1 | tail -3
git checkout -- '*.lock.json' 2>/dev/null
```
Expected: all pass. If `Hsv_RoundTripsEveryByteColourInASample` fails for a colour, fix `ToHsv`/`FromHsv` (do not weaken the test). For example, compute the hue sector with `Math.Min((int)(h / 60f), 5)`.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/Markup/MarkupColorValue.cs src/AcDream.App/UI/Markup/MarkupBindings.cs tests/AcDream.App.Tests/UI/MarkupColorValueTests.cs
git commit -m "feat(ui): colour values and colour bind helper for the colour picker

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 2: Texture service, gradient ramps and recent colours

**Goal:** `IMarkupTextureService` + `RetailMarkupTextureService`, `MarkupColorRamps`, `MarkupRecentColors` and `MarkupControlStateStore.Shared` exist with tests.

**Files:**
- Create: `src/AcDream.App/UI/Markup/IMarkupTextureService.cs`, `src/AcDream.App/UI/RetailMarkupTextureService.cs`, `src/AcDream.App/UI/Markup/MarkupColorRamps.cs`, `src/AcDream.App/UI/Markup/MarkupRecentColors.cs`
- Modify: `src/AcDream.App/UI/Markup/MarkupControlStateStore.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupColorSupportTests.cs`

**Acceptance Criteria:**
- [ ] Ramps: `WhiteToClear` alpha is 255 at x = 0 and 0 at x = w−1, RGB white; `ClearToBlack` alpha is 0 at y = 0 and 255 at y = h−1, RGB black; `Hue` row 0 is red, the middle row is cyan (±2), opaque; `AlphaRamp` alpha is 255 at y = 0 and 0 at y = h−1, RGB white. Each buffer is `w*h*4` bytes.
- [ ] `MarkupRecentColors`: `Add` puts newest first, moves a repeat to the front, keeps at most 8; with a state it stores `#AARRGGBB` comma text under control `color`, kind `recent`; it reads the stored list on first use and again after the state's `Generation` moves; malformed entries are skipped, duplicates dropped, at most 8 kept; null state = memory only.
- [ ] `MarkupControlStateStore.Shared` reads/writes the window key `*` (`MarkupControlStateStore.SharedWindow`).

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~MarkupColorSupportTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — `tests/AcDream.App.Tests/UI/MarkupColorSupportTests.cs`:

```csharp
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

public class MarkupColorSupportTests
{
    private static byte At(byte[] rgba, int w, int x, int y, int channel) => rgba[(y * w + x) * 4 + channel];

    [Fact]
    public void WhiteToClear_FadesLeftToRight()
    {
        byte[] p = MarkupColorRamps.WhiteToClear(128, 4);
        Assert.Equal(128 * 4 * 4, p.Length);
        Assert.Equal(255, At(p, 128, 0, 2, 3));
        Assert.Equal(0, At(p, 128, 127, 2, 3));
        Assert.Equal(255, At(p, 128, 64, 0, 0));
        Assert.Equal(255, At(p, 128, 64, 0, 2));
    }

    [Fact]
    public void ClearToBlack_DarkensTopToBottom()
    {
        byte[] p = MarkupColorRamps.ClearToBlack(4, 128);
        Assert.Equal(0, At(p, 4, 1, 0, 3));
        Assert.Equal(255, At(p, 4, 1, 127, 3));
        Assert.Equal(0, At(p, 4, 1, 64, 0));
    }

    [Fact]
    public void Hue_RunsFromRedThroughCyan()
    {
        byte[] p = MarkupColorRamps.Hue(16, 128);
        Assert.Equal((255, 0, 255), (At(p, 16, 0, 0, 0), At(p, 16, 0, 0, 1), At(p, 16, 0, 0, 3)));
        Assert.InRange(At(p, 16, 8, 64, 0), 0, 2);
        Assert.InRange(At(p, 16, 8, 64, 1), 253, 255);
        Assert.InRange(At(p, 16, 8, 64, 2), 253, 255);
    }

    [Fact]
    public void AlphaRamp_IsOpaqueAtTheTop()
    {
        byte[] p = MarkupColorRamps.AlphaRamp(16, 128);
        Assert.Equal(255, At(p, 16, 3, 0, 3));
        Assert.Equal(0, At(p, 16, 3, 127, 3));
        Assert.Equal(255, At(p, 16, 3, 127, 1));
    }

    private sealed class FakeState : IMarkupControlState
    {
        public Dictionary<(string, string), string> Values { get; } = new();
        public int Generation { get; set; } = 1;
        public int Sets { get; private set; }
        public string? Get(string control, string kind) => Values.GetValueOrDefault((control, kind));
        public void Set(string control, string kind, string value) { Sets++; Values[(control, kind)] = value; }
        public void Remove(string control, string kind) => Values.Remove((control, kind));
    }

    [Fact]
    public void Recent_NewestFirstNoRepeatsAtMostEight()
    {
        var recent = new MarkupRecentColors(null);
        for (uint i = 1; i <= 10; i++)
            recent.Add(0xFF000000u | i);
        recent.Add(0xFF000005u);
        Assert.Equal(
            new uint[] { 0xFF000005, 0xFF00000A, 0xFF000009, 0xFF000008, 0xFF000007, 0xFF000006, 0xFF000004, 0xFF000003 },
            recent.Colors);
    }

    [Fact]
    public void Recent_StoresCommaTextInTheSharedState()
    {
        var state = new FakeState();
        var recent = new MarkupRecentColors(state);
        recent.Add(0x80FF0000u);
        recent.Add(0xFF00FF00u);
        Assert.Equal("#FF00FF00,#80FF0000", state.Values[("color", "recent")]);
    }

    [Fact]
    public void Recent_ReadsTheStoredListAndSkipsJunk()
    {
        var state = new FakeState();
        state.Values[("color", "recent")] = "#FF0000FF,junk,#ff0000ff,#12,#80112233";
        var recent = new MarkupRecentColors(state);
        Assert.Equal(new uint[] { 0xFF0000FF, 0x80112233 }, recent.Colors);
    }

    [Fact]
    public void Recent_RereadsAfterACharacterSwitch()
    {
        var state = new FakeState();
        var recent = new MarkupRecentColors(state);
        recent.Add(0xFF111111u);
        state.Values[("color", "recent")] = "#FF222222";
        state.Generation++;
        Assert.Equal(new uint[] { 0xFF222222 }, recent.Colors);
        recent.Add(0xFF333333u);
        Assert.Equal("#FF333333,#FF222222", state.Values[("color", "recent")]);
    }

    [Fact]
    public void Recent_KeepsAtMostEightFromStoredText()
    {
        var state = new FakeState();
        state.Values[("color", "recent")] = string.Join(",", Enumerable.Range(1, 12).Select(i => $"#FF0000{i:X2}"));
        Assert.Equal(MarkupRecentColors.Capacity, new MarkupRecentColors(state).Colors.Count);
    }

    [Fact]
    public void Store_SharedUsesTheReservedWindowKey()
    {
        var store = new MarkupControlStateStore(null, () => "Alice");
        store.Shared.Set("color", "recent", "#FF000000");
        Assert.Equal("#FF000000", store.ForWindow(MarkupControlStateStore.SharedWindow).Get("color", "recent"));
        Assert.Null(store.ForWindow("other").Get("color", "recent"));
    }
}
```

- [ ] **Step 2: Run, expect a compile failure.**

- [ ] **Step 3: `src/AcDream.App/UI/Markup/IMarkupTextureService.cs`**

```csharp
namespace AcDream.App.UI.Markup;

/// <summary>
/// Textures a markup control draws for a while and then gives back, such as
/// the colour picker's gradients. UI thread only. A build without one gets
/// controls that draw without textures.
/// </summary>
public interface IMarkupTextureService
{
    /// <summary>Uploads straight-alpha RGBA8 pixels, rows top to bottom; 0 when it can't.</summary>
    uint Upload(byte[] rgba, int width, int height, string debugName);

    /// <summary>Gives back a texture from <see cref="Upload"/>.</summary>
    void Release(uint texture);
}
```

- [ ] **Step 4: `src/AcDream.App/UI/RetailMarkupTextureService.cs`**

```csharp
using AcDream.App.Rendering;
using AcDream.App.UI.Markup;

namespace AcDream.App.UI;

/// <summary>The client's markup texture service: releasable interface textures from the texture cache.</summary>
internal sealed class RetailMarkupTextureService(TextureCache textures) : IMarkupTextureService
{
    public uint Upload(byte[] rgba, int width, int height, string debugName) =>
        textures.UploadReleasableRgba8(rgba, width, height, debugName);

    public void Release(uint texture) => textures.ReleaseUiTexture(texture);
}
```

- [ ] **Step 5: `src/AcDream.App/UI/Markup/MarkupColorRamps.cs`**

```csharp
namespace AcDream.App.UI.Markup;

/// <summary>
/// The colour picker's gradient pixels, straight-alpha RGBA8, rows top to
/// bottom. The square is a fill of the pure hue with <see cref="WhiteToClear"/>
/// and then <see cref="ClearToBlack"/> drawn over it, which is exactly the
/// saturation/value square, so no texture depends on the hue.
/// </summary>
internal static class MarkupColorRamps
{
    /// <summary>White, opaque at the left, clear at the right.</summary>
    public static byte[] WhiteToClear(int width, int height) =>
        Fill(width, height, (x, _) => (255, 255, 255, Ramp(x, width, falling: true)));

    /// <summary>Black, clear at the top, opaque at the bottom.</summary>
    public static byte[] ClearToBlack(int width, int height) =>
        Fill(width, height, (_, y) => (0, 0, 0, Ramp(y, height, falling: false)));

    /// <summary>The hues top to bottom, red at the top, opaque.</summary>
    public static byte[] Hue(int width, int height) =>
        Fill(width, height, (_, y) =>
        {
            uint c = MarkupColorValue.FromHsv(360f * y / height, 1f, 1f, 0xFF);
            return ((byte)(c >> 16), (byte)(c >> 8), (byte)c, 255);
        });

    /// <summary>White, opaque at the top, clear at the bottom; drawn tinted with the colour.</summary>
    public static byte[] AlphaRamp(int width, int height) =>
        Fill(width, height, (_, y) => (255, 255, 255, Ramp(y, height, falling: true)));

    private static byte Ramp(int i, int count, bool falling)
    {
        float t = count > 1 ? (float)i / (count - 1) : 0f;
        return MarkupColorValue.ToByte(falling ? 1f - t : t);
    }

    private static byte[] Fill(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                int i = (y * width + x) * 4;
                rgba[i] = r;
                rgba[i + 1] = g;
                rgba[i + 2] = b;
                rgba[i + 3] = a;
            }
        }
        return rgba;
    }
}
```

- [ ] **Step 6: `src/AcDream.App/UI/Markup/MarkupRecentColors.cs`**

```csharp
namespace AcDream.App.UI.Markup;

/// <summary>
/// The colours the player picked last, newest first, at most
/// <see cref="Capacity"/>, shared by every colour picker that gets this list.
/// With a state (the store's <see cref="MarkupControlStateStore.Shared"/>) the
/// list is remembered for the character as "#AARRGGBB" text, comma-separated;
/// without one it lasts only as long as the list does. UI thread only.
/// </summary>
public sealed class MarkupRecentColors
{
    public const int Capacity = 8;
    public const string Control = "color";
    public const string Kind = "recent";

    private readonly RememberedValue _remembered;
    private List<uint> _colors = new();

    public MarkupRecentColors(IMarkupControlState? state)
    {
        _remembered = new RememberedValue(state, Control, Kind);
    }

    public IReadOnlyList<uint> Colors
    {
        get
        {
            Refresh();
            return _colors;
        }
    }

    /// <summary>Puts a picked colour first (moving it if it is already there).</summary>
    public void Add(uint argb)
    {
        Refresh();
        _colors.Remove(argb);
        _colors.Insert(0, argb);
        if (_colors.Count > Capacity)
            _colors.RemoveRange(Capacity, _colors.Count - Capacity);
        _remembered.Remember(Encode(_colors));
    }

    private void Refresh()
    {
        if (_remembered.TryRefresh(out string? text))
            _colors = Decode(text);
    }

    internal static string Encode(IEnumerable<uint> colors) =>
        string.Join(",", colors.Select(c => MarkupColorValue.Format(c)));

    /// <summary>The stored list: unreadable entries skipped, repeats dropped, at most <see cref="Capacity"/>.</summary>
    internal static List<uint> Decode(string? text)
    {
        var colors = new List<uint>();
        if (string.IsNullOrEmpty(text))
            return colors;
        foreach (string part in text.Split(','))
        {
            if (MarkupColorValue.ParseStrict(part) is { } c && !colors.Contains(c))
                colors.Add(c);
            if (colors.Count == Capacity)
                break;
        }
        return colors;
    }
}
```

- [ ] **Step 7: Store** — in `src/AcDream.App/UI/Markup/MarkupControlStateStore.cs`, after `ForWindow`:

```csharp
    /// <summary>The window key for values every markup window shares, such as recent colours.</summary>
    public const string SharedWindow = "*";

    /// <summary>The values every markup window shares for the character.</summary>
    public IMarkupControlState Shared => ForWindow(SharedWindow);
```

- [ ] **Step 8: Run the tests, then commit**

```bash
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~MarkupColorSupportTests" 2>&1 | tail -3
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/Markup/IMarkupTextureService.cs src/AcDream.App/UI/RetailMarkupTextureService.cs src/AcDream.App/UI/Markup/MarkupColorRamps.cs src/AcDream.App/UI/Markup/MarkupRecentColors.cs src/AcDream.App/UI/Markup/MarkupControlStateStore.cs tests/AcDream.App.Tests/UI/MarkupColorSupportTests.cs
git commit -m "feat(ui): markup texture service, colour gradients and shared recent colours

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 3: `UiMarkupColor` swatch and `UiMarkupColorPopup`

**Goal:** The swatch and its popup implement every rule under "Popup behaviour", "Swatch rows" and "Looks" in Global Constraints, tested headlessly through `UiRoot`.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupColor.cs`, `src/AcDream.App/UI/UiMarkupColorPopup.cs`
- Test: `tests/AcDream.App.Tests/UI/UiMarkupColorTests.cs`

**Acceptance Criteria:**
- [ ] A left click on an enabled swatch opens the popup, which takes keyboard focus; on a disabled swatch nothing opens.
- [ ] Enter/Space on a focused swatch opens it; Escape then hands focus back to the swatch.
- [ ] Square, hue and alpha presses/drags set the colour by the documented formulas. Reports happen on `root.Tick` only, at most once per tick, with the latest colour. No report happens when it equals the bound value.
- [ ] Without alpha, reports always have alpha `FF` and there is no alpha strip (width 162); with alpha the width is 184.
- [ ] Palette swatches (Classic set; themed tokens) and recent swatches set the colour.
- [ ] Hex: a press on the field starts editing with the current text; chars filter and cap at 9; Backspace; Enter applies; invalid text changes nothing; a press elsewhere in the popup applies first.
- [ ] Escape drops the pending colour, reports `opened` if the bound value moved, and adds nothing to the recent colours.
- [ ] An outside press (also on the swatch) closes, keeps and reports the pending colour, and adds the final colour to the recent colours when it differs from `opened`. That press reaches nothing else.
- [ ] Disabling the swatch closes the popup on the next tick; hiding it closes it at once. Both keep the colour.
- [ ] With a texture service, opening uploads 3 textures (4 with alpha) of the documented sizes and closing releases exactly those; without a service it draws fills.
- [ ] Placement: below the swatch, or above it near the bottom of the screen, clamped to the screen.
- [ ] Draw: Classic swatch border = 4 `ClassicBorder` quads; checkerboard quads when alpha < `FF`; themed swatch uses `Border`; the overlay draws without throwing with and without textures and draws sprites with the uploaded handles.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~UiMarkupColorTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — `tests/AcDream.App.Tests/UI/UiMarkupColorTests.cs`. Geometry: the swatch is at (10, 10), 40×18, so the popup is at screen (10, 30). Square top-left (16, 36); hue strip x 150..166; alpha strip x 172..188; hex field (16, 170); palette row y 194; recent row y 210.

```csharp
using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Markup;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public class UiMarkupColorTests
{
    private sealed class Model
    {
        public uint Value;
        public List<uint> Reports { get; } = new();
    }

    private sealed class FakeTextures : IMarkupTextureService
    {
        private uint _next = 100;
        public List<(uint Handle, int W, int H, string Name)> Uploads { get; } = new();
        public List<uint> Released { get; } = new();
        public uint Upload(byte[] rgba, int width, int height, string debugName)
        {
            Assert.Equal(width * height * 4, rgba.Length);
            uint handle = _next++;
            Uploads.Add((handle, width, height, debugName));
            return handle;
        }
        public void Release(uint texture) => Released.Add(texture);
    }

    private static (UiRoot Root, UiMarkupColor Swatch, Model Model) Mount(
        uint value = 0xFFFF0000u, bool alpha = false, IMarkupTextureService? textures = null,
        float top = 10f, MarkupRecentColors? recent = null)
    {
        var model = new Model { Value = value };
        var swatch = new UiMarkupColor
        {
            Left = 10, Top = top, Width = 40, Height = 18,
            ValueSource = () => model.Value,
            Changed = v => { model.Reports.Add(v); model.Value = v; },
            AllowAlpha = alpha,
            Textures = textures,
        };
        if (recent is not null)
            swatch.Recent = recent;
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(swatch);
        return (root, swatch, model);
    }

    private static void Press(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    private static void Key1(UiRoot root, Key key)
    {
        root.OnKeyDown((int)key);
        root.OnKeyUp((int)key);
    }

    private static void Tick(UiRoot root) => root.Tick(0.016, 16);

    private static (UiRoot, UiMarkupColor, Model) Opened(uint value = 0xFFFF0000u, bool alpha = false,
        IMarkupTextureService? textures = null)
    {
        var mounted = Mount(value, alpha, textures);
        Press(mounted.Root, 20, 15);
        Assert.True(mounted.Swatch.IsOpen);
        return mounted;
    }

    [Fact]
    public void Click_OpensThePopupWhichTakesFocus()
    {
        var (root, swatch, _) = Opened();
        Assert.Same(swatch.Popup, root.KeyboardFocus);
        Assert.Equal((10f, 30f, 162f, 200f), swatch.Popup.PopupScreenRect);
    }

    [Fact]
    public void Click_OnADisabledSwatchDoesNothing()
    {
        var (root, swatch, _) = Mount();
        swatch.Enabled = false;
        Press(root, 20, 15);
        Assert.False(swatch.IsOpen);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Keyboard_EnterOpensAndEscapeGivesFocusBack()
    {
        var (root, swatch, _) = Mount();
        root.SetKeyboardFocus(swatch);
        Key1(root, Key.Enter);
        Assert.True(swatch.IsOpen);
        Key1(root, Key.Escape);
        Assert.False(swatch.IsOpen);
        Assert.Same(swatch, root.KeyboardFocus);
    }

    [Fact]
    public void Square_ReportsOnTheNextTickOnly()
    {
        var (root, _, model) = Opened();
        Press(root, 16 + 64, 36 + 64);   // s = 0.5, v = 0.5 of red
        Assert.Empty(model.Reports);
        Tick(root);
        Assert.Equal(new uint[] { 0xFF804040u }, model.Reports);
        Tick(root);
        Assert.Single(model.Reports);
    }

    [Fact]
    public void Drag_ReportsOncePerTickWithTheLatestColour()
    {
        var (root, _, model) = Opened();
        root.OnMouseDown(UiMouseButton.Left, 16 + 64, 36 + 64);
        root.OnMouseMove(16 + 32, 36 + 32);
        root.OnMouseMove(16, 36 + 300);   // clamped: s = 0, v = 0
        Assert.Empty(model.Reports);
        Tick(root);
        Assert.Equal(new uint[] { 0xFF000000u }, model.Reports);
        root.OnMouseUp(UiMouseButton.Left, 16, 36 + 300);
    }

    [Fact]
    public void Drag_BackToTheBoundColourReportsNothing()
    {
        var (root, _, model) = Opened();
        root.OnMouseDown(UiMouseButton.Left, 16 + 64, 36 + 64);
        root.OnMouseMove(16 + 500, 36 - 50);   // clamped: s = 1, v = 1, pure red = the bound value
        Tick(root);
        root.OnMouseUp(UiMouseButton.Left, 16 + 500, 36 - 50);
        Assert.Empty(model.Reports);
    }

    [Fact]
    public void HueStrip_SetsTheHue()
    {
        var (root, _, model) = Opened();
        Press(root, 158, 36 + 64);   // h = 180 at s = v = 1
        Tick(root);
        Assert.Equal(new uint[] { 0xFF00FFFFu }, model.Reports);
    }

    [Fact]
    public void WithoutAlpha_ReportsAreOpaqueAndThereIsNoAlphaStrip()
    {
        var (root, swatch, model) = Opened(0x80FF0000u);
        Assert.Equal(162f, swatch.Popup.PopupScreenRect.W);
        Press(root, 16 + 7, 194 + 7);   // first Classic palette swatch: white
        Tick(root);
        Assert.Equal(new uint[] { 0xFFFFFFFFu }, model.Reports);
    }

    [Fact]
    public void WithAlpha_TheAlphaStripSetsTheAlpha()
    {
        var (root, swatch, model) = Opened(0xFFFF0000u, alpha: true);
        Assert.Equal(184f, swatch.Popup.PopupScreenRect.W);
        Press(root, 180, 36 + 64);   // a = 0.5
        Tick(root);
        Assert.Equal(new uint[] { 0x80FF0000u }, model.Reports);
    }

    [Fact]
    public void ThemedPalette_RowUsesTheTokens()
    {
        var (root, swatch, model) = Mount();
        swatch.ThemePalette = PluginUiPalette.Moss;
        Press(root, 20, 15);
        Press(root, 16 + 5 * 16 + 7, 194 + 7);   // sixth swatch: Accent
        Tick(root);
        Assert.Equal(new[] { MarkupColorValue.FromVector4(PluginUiPalette.Moss.Accent) }, model.Reports);
    }

    [Fact]
    public void Hex_TypedColourAppliesOnEnter()
    {
        var (root, _, model) = Opened();
        Press(root, 30, 175);
        Assert.Equal("#FF0000", ((UiMarkupColor)root.Children[0]).Popup.HexText);
        for (int i = 0; i < 7; i++)
            Key1(root, Key.Backspace);
        foreach (char c in "00ff00zz")
            root.OnChar(c);
        Key1(root, Key.Enter);
        Tick(root);
        Assert.Equal(new uint[] { 0xFF00FF00u }, model.Reports);
    }

    [Fact]
    public void Hex_CapsAtNineCharacters()
    {
        var (root, swatch, _) = Opened(alpha: true);
        Press(root, 30, 175);
        foreach (char c in "123456789")
            root.OnChar(c);
        Assert.Equal("#FFFF0000", swatch.Popup.HexText);
    }

    [Fact]
    public void Hex_UnreadableTextChangesNothing()
    {
        var (root, _, model) = Opened();
        Press(root, 30, 175);
        for (int i = 0; i < 5; i++)
            Key1(root, Key.Backspace);
        Key1(root, Key.Enter);   // "#F" doesn't parse
        Tick(root);
        Assert.Empty(model.Reports);
    }

    [Fact]
    public void Hex_APressElsewhereInThePopupAppliesIt()
    {
        var (root, _, model) = Opened();
        Press(root, 30, 175);
        Key1(root, Key.Backspace);
        root.OnChar('F');               // "#FF000F"
        Press(root, 100, 215);          // an empty slot in the (empty) recent row
        Tick(root);
        Assert.Equal(new uint[] { 0xFFFF000Fu }, model.Reports);
    }

    [Fact]
    public void Escape_RestoresTheOpeningColour()
    {
        var recent = new MarkupRecentColors(null);
        var (root, swatch, model) = Mount(recent: recent);
        Press(root, 20, 15);
        Press(root, 158, 36 + 64);
        Tick(root);
        Press(root, 16 + 64, 36 + 64);   // pending, never reported
        Key1(root, Key.Escape);
        Tick(root);
        Assert.Equal(new uint[] { 0xFF00FFFFu, 0xFFFF0000u }, model.Reports);
        Assert.False(swatch.IsOpen);
        Assert.Null(root.KeyboardFocus);
        Assert.Empty(recent.Colors);
    }

    [Fact]
    public void OutsidePress_KeepsTheColourAndRemembersIt()
    {
        var recent = new MarkupRecentColors(null);
        var (root, swatch, model) = Mount(recent: recent);
        Press(root, 20, 15);
        Press(root, 158, 36 + 64);       // pending cyan
        int fallThrough = 0;
        root.WorldMouseFallThrough += (_, _, _, _) => fallThrough++;
        Press(root, 600, 500);
        Assert.False(swatch.IsOpen);
        Assert.Equal(new uint[] { 0xFF00FFFFu }, model.Reports);
        Assert.Equal(new uint[] { 0xFF00FFFFu }, recent.Colors);
        Assert.Equal(0, fallThrough);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void OutsidePress_WithNoChangeRemembersNothing()
    {
        var recent = new MarkupRecentColors(null);
        var (root, swatch, model) = Mount(recent: recent);
        Press(root, 20, 15);
        Press(root, 20, 15);             // the swatch itself counts as outside
        Assert.False(swatch.IsOpen);
        Assert.Empty(model.Reports);
        Assert.Empty(recent.Colors);
    }

    [Fact]
    public void RecentRow_SetsTheColour()
    {
        var recent = new MarkupRecentColors(null);
        recent.Add(0xFF123456u);
        var (root, _, model) = Mount(recent: recent);
        Press(root, 20, 15);
        Press(root, 16 + 7, 210 + 7);
        Tick(root);
        Assert.Equal(new uint[] { 0xFF123456u }, model.Reports);
    }

    [Fact]
    public void Disabling_ClosesOnTheNextTickAndKeeps()
    {
        var (root, swatch, model) = Opened();
        Press(root, 158, 36 + 64);
        swatch.Enabled = false;
        Tick(root);
        Assert.False(swatch.IsOpen);
        Assert.Equal(new uint[] { 0xFF00FFFFu }, model.Reports);
    }

    [Fact]
    public void Hiding_ClosesAtOnce()
    {
        var textures = new FakeTextures();
        var (_, swatch, _) = Opened(textures: textures);
        swatch.Visible = false;
        Assert.False(swatch.IsOpen);
        Assert.Equal(textures.Uploads.Select(u => u.Handle), textures.Released);
    }

    [Fact]
    public void Textures_UploadedOnOpenReleasedOnClose()
    {
        var textures = new FakeTextures();
        var (root, _, _) = Opened(textures: textures);
        Assert.Equal(
            new[] { (128, 128), (128, 128), (16, 128) },
            textures.Uploads.Select(u => (u.W, u.H)));
        Assert.Empty(textures.Released);
        Key1(root, Key.Escape);
        Assert.Equal(textures.Uploads.Select(u => u.Handle), textures.Released);
    }

    [Fact]
    public void Textures_AlphaAddsTheRamp()
    {
        var textures = new FakeTextures();
        Opened(alpha: true, textures: textures);
        Assert.Equal(4, textures.Uploads.Count);
        Assert.Equal((16, 128), (textures.Uploads[3].W, textures.Uploads[3].H));
    }

    [Fact]
    public void Placement_FlipsAboveNearTheBottom()
    {
        var (root, swatch, _) = Mount(top: 570f);
        Press(root, 20, 575);
        Assert.Equal(570f - 2f - 200f, swatch.Popup.PopupScreenRect.Y);
    }

    [Fact]
    public void Draw_ClassicSwatchBorder()
    {
        var (_, swatch, _) = Mount();
        var renderer = MarkupDrawProbe.Draw(swatch);
        Assert.Equal(4, MarkupDrawProbe.CountQuads(renderer, UiMarkupColor.ClassicBorder));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupColor.CheckerLight));
    }

    [Fact]
    public void Draw_CheckerboardBehindTranslucentColours()
    {
        var (_, swatch, _) = Mount(0x80FF0000u);
        var renderer = MarkupDrawProbe.Draw(swatch);
        Assert.True(MarkupDrawProbe.CountQuads(renderer, UiMarkupColor.CheckerLight) > 0);
    }

    [Fact]
    public void Draw_ThemedSwatchUsesTheBorderToken()
    {
        var (_, swatch, _) = Mount();
        swatch.ThemePalette = PluginUiPalette.Moss;
        var renderer = MarkupDrawProbe.Draw(swatch);
        Assert.Equal(4, MarkupDrawProbe.CountQuads(renderer, PluginUiPalette.Moss.Border));
    }

    [Fact]
    public void Draw_PopupWithTexturesUsesTheHandles()
    {
        var textures = new FakeTextures();
        var (root, _, _) = Opened(alpha: true, textures: textures);
        var renderer = MarkupDrawProbe.DrawOverlays(root);
        var drawn = renderer.DebugSpriteSegmentVerts.Select(s => s.Texture).ToHashSet();
        foreach (var upload in textures.Uploads)
            Assert.Contains(upload.Handle, drawn);
    }

    [Fact]
    public void Draw_PopupWithoutTexturesDrawsFills()
    {
        var (root, _, _) = Opened(alpha: true);
        var renderer = MarkupDrawProbe.DrawOverlays(root);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(renderer, UiMarkupContextMenu.ClassicBackground));
        Assert.True(renderer.DebugSpriteSegmentVerts.Where(s => s.Texture == 0u).Sum(s => s.Verts.Count) / 48 > 256);
    }
}
```

  Both drag tests move far past any drag threshold, so the captured popup gets every `MouseMove`.

- [ ] **Step 2: Run, expect a compile failure.**

- [ ] **Step 3: `src/AcDream.App/UI/UiMarkupColor.cs`**

```csharp
using System.Numerics;
using AcDream.App.UI.Markup;

namespace AcDream.App.UI;

/// <summary>
/// A markup colour picker: a swatch of the bound colour that opens a
/// <see cref="UiMarkupColorPopup"/> on click, or Enter/Space while focused.
/// The plugin owns the colour; the swatch draws whatever
/// <see cref="ValueSource"/> returns. Clicking doesn't take keyboard focus
/// from the game; the popup holds it while it is open.
/// </summary>
public sealed class UiMarkupColor : UiElement
{
    public const float CheckerCell = 4f;

    public static readonly Vector4 ClassicBorder = UiCheckLamp.UncheckedInner;
    public static readonly Vector4 FocusColor = UiMarkupHotkey.FocusColor;
    public static readonly Vector4 CheckerLight = new(0.8f, 0.8f, 0.8f, 1f);
    public static readonly Vector4 CheckerDark = new(0.5f, 0.5f, 0.5f, 1f);

    private readonly UiKeyboardActivation _keyboard = new();

    public UiMarkupColor()
    {
        AcceptsFocus = true;
        FocusOnMouseClick = false;
        TabStop = true;
        Popup = new UiMarkupColorPopup(this);
        AddChild(Popup);
    }

    public UiMarkupColorPopup Popup { get; }

    /// <summary>The bound colour, packed 0xAARRGGBB.</summary>
    public Func<uint>? ValueSource { get; set; }

    /// <summary>Called with each colour the player picks, at most once per tick.</summary>
    public Action<uint>? Changed { get; set; }

    /// <summary>Whether the picker edits alpha; without it every reported colour is opaque.</summary>
    public bool AllowAlpha { get; set; }

    public PluginUiPalette? ThemePalette { get; set; }
    public UiDatFont? DatFont { get; set; }

    /// <summary>Where the popup's gradients come from; null draws them with fills.</summary>
    public IMarkupTextureService? Textures { get; set; }

    /// <summary>The recent colours row, shared with the document's other pickers.</summary>
    public MarkupRecentColors Recent { get; set; } = new(null);

    public uint Value => ValueSource?.Invoke() ?? MarkupColorValue.White;

    public bool IsOpen => Popup.IsOpen;

    public override bool HandlesClick => true;

    public bool Open() => Enabled && Popup.TryOpen();

    public override bool OnEvent(in UiEvent e)
    {
        if (_keyboard.HandleEvent(e, TabStop, Enabled, () => Open()))
            return true;
        if (e.Type == UiEventType.Click)
        {
            Open();
            return true;
        }
        return false;
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        PluginUiPalette? p = ThemePalette;
        uint argb = Value;
        float w = Width - 2f, h = Height - 2f;
        if (MarkupColorValue.Alpha(argb) < 0xFF)
            DrawChecker(ctx, 1f, 1f, w, h, Fade);
        ctx.DrawFill(1f, 1f, w, h, Fade(MarkupColorValue.ToVector4(argb)));
        bool focused = _keyboard.Focused || IsOpen;
        ctx.DrawRectOutline(0f, 0f, Width, Height,
            Fade(focused ? p?.Accent ?? FocusColor : p?.Border ?? ClassicBorder), 1f);
    }

    /// <summary>The light/dark squares drawn behind a translucent colour.</summary>
    internal static void DrawChecker(UiRenderContext ctx, float x, float y, float w, float h,
        Func<Vector4, Vector4> fade)
    {
        for (float cy = 0f; cy < h; cy += CheckerCell)
        {
            for (float cx = 0f; cx < w; cx += CheckerCell)
            {
                bool light = ((int)(cx / CheckerCell) + (int)(cy / CheckerCell)) % 2 == 0;
                ctx.DrawFill(x + cx, y + cy, MathF.Min(CheckerCell, w - cx), MathF.Min(CheckerCell, h - cy),
                    fade(light ? CheckerLight : CheckerDark));
            }
        }
    }

    private Vector4 Fade(Vector4 color) =>
        Enabled ? color : color with { W = color.W * UiMarkupToggle.DisabledAlpha };
}
```

- [ ] **Step 4: `src/AcDream.App/UI/UiMarkupColorPopup.cs`**

```csharp
using System.Numerics;
using AcDream.App.UI.Markup;
using Silk.NET.Input;

namespace AcDream.App.UI;

/// <summary>
/// The colour picker's popup. It has a saturation/value square, a hue strip,
/// an alpha strip when the picker allows alpha, a hex field and two swatch
/// rows (palette, then recent colours). It is a child of its
/// <see cref="UiMarkupColor"/>, takes no room there and draws in the overlay
/// pass, like <see cref="UiMarkupContextMenu"/>. Open, it holds the root's
/// popup slot and the keyboard focus.
/// <para>Edits are reported through the owner's Changed on the next tick,
/// so at most once per frame. Closing keeps the colour, except Escape, which
/// puts back the colour the popup opened with.</para>
/// </summary>
public sealed class UiMarkupColorPopup : UiElement
{
    public const float Pad = 6f;
    public const float SquareSize = 128f;
    public const float StripWidth = 16f;
    public const float RowHeight = 18f;
    public const float SwatchSize = 14f;
    public const float SwatchStep = 16f;
    public const float Gap = 2f;
    public const float HueX = Pad + SquareSize + Pad;              // 140
    public const float AlphaX = HueX + StripWidth + Pad;           // 162
    public const float HexY = Pad + SquareSize + Pad;              // 140
    public const float PaletteY = HexY + RowHeight + Pad;          // 164
    public const float RecentY = PaletteY + SwatchStep;            // 180
    public const float PopupHeight = RecentY + SwatchSize + Pad;   // 200
    public const int MaxHexLength = 9;

    public static readonly uint[] ClassicSwatches =
    [
        0xFFFFFFFFu, 0xFF000000u, 0xFF808080u, 0xFFD03030u, 0xFFFFD140u, 0xFF40B040u, 0xFF4070D0u,
    ];

    private static readonly Vector4 MarkerDark = new(0f, 0f, 0f, 1f);
    private static readonly Vector4 MarkerLight = new(1f, 1f, 1f, 1f);

    private enum DragTarget { None, Square, Hue, Alpha }

    private readonly UiMarkupColor _owner;
    private bool _open;
    private float _x, _y, _w, _h;   // the popup, relative to this element's origin (the owner's)
    private float _hue, _sat, _val, _alpha;
    private uint _color;             // the working colour
    private uint _opened;            // the bound colour when it opened
    private uint? _pending;          // edited, not yet reported
    private DragTarget _drag;
    private bool _hexEditing;
    private string _hexText = string.Empty;
    private UiElement? _returnFocus;
    private UiRoot? _root;
    private IMarkupTextureService? _textureService;
    private uint _texWhite, _texBlack, _texHue, _texAlpha;

    internal UiMarkupColorPopup(UiMarkupColor owner)
    {
        _owner = owner;
        AcceptsFocus = true;
        FocusOnMouseClick = true;    // a press inside keeps the focus here
        CapturesPointerDrag = true;  // dragging in the square never moves the window
        Anchors = AnchorEdges.None;
    }

    public bool IsOpen => _open;

    /// <summary>The working colour.</summary>
    public uint Color => _color;

    /// <summary>The hex field's text: what is being typed, else the working colour.</summary>
    public string HexText => _hexEditing ? _hexText : MarkupColorValue.Format(_color, _owner.AllowAlpha);

    /// <summary>The open popup in canvas coordinates.</summary>
    internal (float X, float Y, float W, float H) PopupScreenRect
    {
        get
        {
            Vector2 origin = ScreenPosition;
            return (origin.X + _x, origin.Y + _y, _w, _h);
        }
    }

    public override bool HandlesClick => true;
    protected override bool ClipsChildren => false;
    protected override bool ExpandsClipForPopup => true;

    private byte AlphaByte(float alpha) => _owner.AllowAlpha ? MarkupColorValue.ToByte(alpha) : (byte)0xFF;

    internal bool TryOpen()
    {
        if (_open || FindRoot() is not { } root || !Available(_owner))
            return false;
        _opened = _owner.Value;
        _hue = 0f;
        _sat = 0f;
        SetWorking(_owner.AllowAlpha ? _opened : MarkupColorValue.WithAlpha(_opened, 0xFF));
        _pending = null;
        _drag = DragTarget.None;
        _hexEditing = false;
        Place(root);
        _open = true;
        _root = root;
        IsEditControl = true;   // UiRoot only sends Char events to edit controls
        root.SetActivePopup(this, () => Close(keep: true));
        _returnFocus = root.KeyboardFocus;
        root.SetKeyboardFocus(this);
        UploadTextures();
        return true;
    }

    private void Place(UiRoot root)
    {
        _w = (_owner.AllowAlpha ? AlphaX : HueX) + StripWidth + Pad;
        _h = PopupHeight;
        Vector2 origin = ScreenPosition;
        float x = Math.Clamp(origin.X, 0f, MathF.Max(0f, root.Width - _w));
        float y = origin.Y + _owner.Height + Gap;
        if (y + _h > root.Height)
            y = origin.Y - Gap - _h;
        y = Math.Clamp(y, 0f, MathF.Max(0f, root.Height - _h));
        _x = x - origin.X;
        _y = y - origin.Y;
    }

    /// <summary>
    /// Closes the popup. Keeping applies the hex text and reports the colour;
    /// otherwise the colour it opened with is put back. Callbacks run last.
    /// </summary>
    public void Close(bool keep = true, bool restoreFocus = true)
    {
        if (!_open)
            return;
        if (keep)
            CommitHex();
        uint? report = keep ? _pending : _opened;
        bool remember = keep && _color != _opened;
        uint final = _color;

        _open = false;
        _pending = null;
        _drag = DragTarget.None;
        _hexEditing = false;
        IsEditControl = false;
        ReleaseTextures();
        UiElement? previous = _returnFocus;
        _returnFocus = null;
        UiRoot? root = _root;
        _root = null;
        if (root is not null)
        {
            root.ClearActivePopup(this);
            if (restoreFocus && ReferenceEquals(root.KeyboardFocus, this))
                root.SetKeyboardFocus(previous);   // the root drops it if no longer available
        }

        if (remember)
            _owner.Recent.Add(final);
        if (report is { } color && color != _owner.Value)
            _owner.Changed?.Invoke(color);
    }

    protected override void OnTick(double deltaSeconds)
    {
        if (!_open)
            return;
        if (!Available(_owner))
        {
            Close(keep: true);
            return;
        }
        Flush();
    }

    private void Flush()
    {
        if (_pending is not { } color)
            return;
        _pending = null;
        if (color != _owner.Value)
            _owner.Changed?.Invoke(color);
    }

    /// <summary>Whether an element and all its ancestors are shown and enabled.</summary>
    private static bool Available(UiElement element)
    {
        for (UiElement? e = element; e is not null; e = e.Parent)
        {
            if (!e.Visible || !e.Enabled)
                return false;
        }
        return true;
    }

    protected override bool OnHitTest(float x, float y) =>
        _open && x >= _x && x < _x + _w && y >= _y && y < _y + _h;

    public override bool OnEvent(in UiEvent e)
    {
        if (e.Type == UiEventType.FocusLost)
        {
            Close(keep: true, restoreFocus: false);
            return true;
        }
        if (!_open)
            return ReferenceEquals(e.Target, this);   // the tail of the press that closed it
        switch (e.Type)
        {
            case UiEventType.KeyDown:
                KeyDown((Key)e.Data0);
                return true;
            case UiEventType.Char:
                TypeHex((char)e.Data0);
                return true;
            case UiEventType.MouseDown:
                Press(e.Data1 - _x, e.Data2 - _y);
                return true;
            case UiEventType.MouseMove:
                if (_drag != DragTarget.None)
                    DragTo(e.Data1 - _x, e.Data2 - _y);
                return true;
            case UiEventType.MouseUp:
            case UiEventType.CaptureChanged:
                _drag = DragTarget.None;
                return true;
            default:
                return true;   // clicks, key ups, other buttons and the wheel all stay here
        }
    }

    private void KeyDown(Key key)
    {
        switch (key)
        {
            case Key.Escape:
                Close(keep: false);
                break;
            case Key.Enter:
            case Key.KeypadEnter:
                if (_hexEditing)
                    CommitHex();
                else
                    Close(keep: true);
                break;
            case Key.Backspace:
                if (_hexEditing && _hexText.Length > 0)
                    _hexText = _hexText[..^1];
                break;
        }
    }

    private void TypeHex(char c)
    {
        if (_hexEditing && (char.IsAsciiHexDigit(c) || c == '#') && _hexText.Length < MaxHexLength)
            _hexText += c;
    }

    // Points from here on are relative to the popup's top-left corner.
    private static bool In(float x, float y, float left, float top, float w, float h) =>
        x >= left && x < left + w && y >= top && y < top + h;

    private bool InHex(float x, float y) => In(x, y, Pad, HexY, _w - 2f * Pad, RowHeight);

    private void Press(float x, float y)
    {
        if (_hexEditing && !InHex(x, y))
            CommitHex();
        if (In(x, y, Pad, Pad, SquareSize, SquareSize))
            _drag = DragTarget.Square;
        else if (In(x, y, HueX, Pad, StripWidth, SquareSize))
            _drag = DragTarget.Hue;
        else if (_owner.AllowAlpha && In(x, y, AlphaX, Pad, StripWidth, SquareSize))
            _drag = DragTarget.Alpha;
        else if (InHex(x, y))
        {
            if (!_hexEditing)
            {
                _hexText = HexText;
                _hexEditing = true;
            }
            return;
        }
        else
        {
            if (SwatchAt(x, y) is { } swatch)
                Pick(swatch);
            return;
        }
        DragTo(x, y);
    }

    private void DragTo(float x, float y)
    {
        float across = Math.Clamp((x - Pad) / SquareSize, 0f, 1f);
        float down = Math.Clamp((y - Pad) / SquareSize, 0f, 1f);
        switch (_drag)
        {
            case DragTarget.Square:
                _sat = across;
                _val = 1f - down;
                break;
            case DragTarget.Hue:
                _hue = down * 360f;
                break;
            case DragTarget.Alpha:
                _alpha = 1f - down;
                break;
            default:
                return;
        }
        _color = MarkupColorValue.FromHsv(_hue, _sat, _val, AlphaByte(_alpha));
        _pending = _color;
    }

    /// <summary>A swatch colour (palette or recent) under a point, or null.</summary>
    private uint? SwatchAt(float x, float y)
    {
        if (x < Pad)
            return null;
        int index = (int)((x - Pad) / SwatchStep);
        if (x - Pad - index * SwatchStep >= SwatchSize)
            return null;
        IReadOnlyList<uint> row;
        if (In(x, y, Pad, PaletteY, _w, SwatchSize))
            row = PaletteSwatches();
        else if (In(x, y, Pad, RecentY, _w, SwatchSize))
            row = _owner.Recent.Colors;
        else
            return null;
        return index < row.Count ? row[index] : null;
    }

    internal IReadOnlyList<uint> PaletteSwatches()
    {
        if (_owner.ThemePalette is not { } p)
            return ClassicSwatches;
        return new[] { p.Background, p.Field, p.Border, p.Text, p.Muted, p.Accent, p.Selected }
            .Select(MarkupColorValue.FromVector4).ToArray();
    }

    private void Pick(uint color)
    {
        SetWorking(_owner.AllowAlpha ? color : MarkupColorValue.WithAlpha(color, 0xFF));
        _pending = _color;
    }

    private void CommitHex()
    {
        if (!_hexEditing)
            return;
        _hexEditing = false;
        if (MarkupColorValue.Parse(_hexText, MarkupColorValue.Alpha(_color)) is { } color && color != _color)
            Pick(color);
    }

    /// <summary>Makes a colour the working one; grey keeps the hue, black the hue and saturation.</summary>
    private void SetWorking(uint color)
    {
        _color = color;
        var (h, s, v) = MarkupColorValue.ToHsv(color);
        if (v > 0f)
        {
            if (s > 0f)
                _hue = h;
            _sat = s;
        }
        _val = v;
        _alpha = MarkupColorValue.Alpha(color) / 255f;
    }

    private void UploadTextures()
    {
        _textureService = _owner.Textures;
        if (_textureService is not { } t)
            return;
        int side = (int)SquareSize, strip = (int)StripWidth;
        _texWhite = t.Upload(MarkupColorRamps.WhiteToClear(side, side), side, side, "markup-color-white");
        _texBlack = t.Upload(MarkupColorRamps.ClearToBlack(side, side), side, side, "markup-color-black");
        _texHue = t.Upload(MarkupColorRamps.Hue(strip, side), strip, side, "markup-color-hue");
        if (_owner.AllowAlpha)
            _texAlpha = t.Upload(MarkupColorRamps.AlphaRamp(strip, side), strip, side, "markup-color-alpha");
    }

    private void ReleaseTextures()
    {
        if (_textureService is { } t)
        {
            foreach (uint texture in new[] { _texWhite, _texBlack, _texHue, _texAlpha })
            {
                if (texture != 0u)
                    t.Release(texture);
            }
        }
        _texWhite = _texBlack = _texHue = _texAlpha = 0u;
        _textureService = null;
    }

    protected override void OnDrawOverlay(UiRenderContext ctx)
    {
        if (!_open)
            return;
        PluginUiPalette? p = _owner.ThemePalette;
        Vector4 border = p?.Border ?? UiMarkupContextMenu.ClassicBorder;
        ctx.PushAlphaAbsolute(1f);
        try
        {
            ctx.DrawFill(_x, _y, _w, _h, p?.Field ?? UiMarkupContextMenu.ClassicBackground);
            ctx.DrawRectOutline(_x, _y, _w, _h, border, 1f);
            DrawSquare(ctx, _x + Pad, _y + Pad);
            DrawHue(ctx, _x + HueX, _y + Pad);
            if (_owner.AllowAlpha)
                DrawAlpha(ctx, _x + AlphaX, _y + Pad);
            DrawHex(ctx, p);
            DrawRow(ctx, PaletteSwatches(), _y + PaletteY, border, recent: false);
            DrawRow(ctx, _owner.Recent.Colors, _y + RecentY, border, recent: true);
        }
        finally { ctx.PopAlpha(); }
    }

    private void DrawSquare(UiRenderContext ctx, float x, float y)
    {
        if (_texWhite != 0u && _texBlack != 0u)
        {
            ctx.DrawFill(x, y, SquareSize, SquareSize, MarkupColorValue.ToVector4(MarkupColorValue.FromHsv(_hue, 1f, 1f, 0xFF)));
            ctx.DrawSprite(_texWhite, x, y, SquareSize, SquareSize, 0f, 0f, 1f, 1f, Vector4.One);
            ctx.DrawSprite(_texBlack, x, y, SquareSize, SquareSize, 0f, 0f, 1f, 1f, Vector4.One);
        }
        else
        {
            const int cells = 16;
            float cell = SquareSize / cells;
            for (int j = 0; j < cells; j++)
            {
                for (int i = 0; i < cells; i++)
                {
                    uint c = MarkupColorValue.FromHsv(_hue, (i + 0.5f) / cells, 1f - (j + 0.5f) / cells, 0xFF);
                    ctx.DrawFill(x + i * cell, y + j * cell, cell, cell, MarkupColorValue.ToVector4(c));
                }
            }
        }
        float mx = x + _sat * SquareSize, my = y + (1f - _val) * SquareSize;
        ctx.DrawRectOutline(mx - 3f, my - 3f, 7f, 7f, MarkerDark, 1f);
        ctx.DrawRectOutline(mx - 2f, my - 2f, 5f, 5f, MarkerLight, 1f);
    }

    private void DrawHue(UiRenderContext ctx, float x, float y)
    {
        if (_texHue != 0u)
            ctx.DrawSprite(_texHue, x, y, StripWidth, SquareSize, 0f, 0f, 1f, 1f, Vector4.One);
        else
        {
            const int cells = 32;
            float cell = SquareSize / cells;
            for (int j = 0; j < cells; j++)
            {
                uint c = MarkupColorValue.FromHsv(360f * (j + 0.5f) / cells, 1f, 1f, 0xFF);
                ctx.DrawFill(x, y + j * cell, StripWidth, cell, MarkupColorValue.ToVector4(c));
            }
        }
        DrawStripMarker(ctx, x, y + _hue / 360f * SquareSize);
    }

    private void DrawAlpha(UiRenderContext ctx, float x, float y)
    {
        UiMarkupColor.DrawChecker(ctx, x, y, StripWidth, SquareSize, static c => c);
        Vector4 rgb = MarkupColorValue.ToVector4(_color) with { W = 1f };
        if (_texAlpha != 0u)
            ctx.DrawSprite(_texAlpha, x, y, StripWidth, SquareSize, 0f, 0f, 1f, 1f, rgb);
        else
        {
            const int cells = 16;
            float cell = SquareSize / cells;
            for (int j = 0; j < cells; j++)
                ctx.DrawFill(x, y + j * cell, StripWidth, cell, rgb with { W = 1f - (j + 0.5f) / cells });
        }
        DrawStripMarker(ctx, x, y + (1f - _alpha) * SquareSize);
    }

    private static void DrawStripMarker(UiRenderContext ctx, float x, float y)
    {
        ctx.DrawFill(x - 1f, y - 1f, StripWidth + 2f, 3f, MarkerDark);
        ctx.DrawFill(x, y, StripWidth, 1f, MarkerLight);
    }

    private void DrawHex(UiRenderContext ctx, PluginUiPalette? p)
    {
        float x = _x + Pad, y = _y + HexY, w = _w - 2f * Pad;
        ctx.DrawFill(x, y, w, RowHeight, p?.Field ?? UiMarkupHotkey.ClassicBackground);
        Vector4 border = _hexEditing ? p?.Accent ?? UiMarkupHotkey.FocusColor : p?.Border ?? UiMarkupHotkey.ClassicBorder;
        ctx.DrawRectOutline(x, y, w, RowHeight, border, 1f);
        UiCaption.Draw(ctx, _owner.DatFont, HexText, x + UiMarkupHotkey.TextInset, y, RowHeight,
            p?.Text ?? UiMarkupContextMenu.ClassicText, outline: p is null);
    }

    private void DrawRow(UiRenderContext ctx, IReadOnlyList<uint> colors, float y, Vector4 border, bool recent)
    {
        // The recent row shows all its slots, empty ones as outlines.
        int slots = recent ? MarkupRecentColors.Capacity : colors.Count;
        for (int i = 0; i < slots; i++)
        {
            float x = _x + Pad + i * SwatchStep;
            if (i < colors.Count)
            {
                uint c = colors[i];
                if (MarkupColorValue.Alpha(c) < 0xFF)
                    UiMarkupColor.DrawChecker(ctx, x, y, SwatchSize, SwatchSize, static v => v);
                ctx.DrawFill(x, y, SwatchSize, SwatchSize, MarkupColorValue.ToVector4(c));
            }
            ctx.DrawRectOutline(x, y, SwatchSize, SwatchSize, border, 1f);
        }
    }
}
```

- [ ] **Step 5: Run the tests**

```bash
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~UiMarkupColorTests" 2>&1 | tail -15
git checkout -- '*.lock.json' 2>/dev/null
```
Expected: all pass. Notes for failures:
- If `OnChar` never reaches the popup, confirm `IsEditControl` is true while open (`UiRoot.OnChar` checks it).
- If `Hiding_ClosesAtOnce` fails, `UiRoot.OnElementVisibilityChanging` → `ClearSubtreeOwnership` must invoke the dismiss. It does for any popup under the hidden element, so check that `Visible = false` on a root child goes through it.
- Fix the code, not the expected values. The only exception is a test geometry slip; recheck it against the constants in Global Constraints.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/UiMarkupColor.cs src/AcDream.App/UI/UiMarkupColorPopup.cs tests/AcDream.App.Tests/UI/UiMarkupColorTests.cs
git commit -m "feat(ui): colour picker swatch and popup

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 4: `<color>` builder, document wiring, theme and client textures

**Goal:** `<color>` builds from markup with its bindings and `alpha`, follows the plugin theme, and gets the texture service and shared recent colours from the document build and the client.

**Files:**
- Create: `src/AcDream.App/UI/Markup/MarkupColor.cs`
- Modify: `src/AcDream.App/UI/Markup/MarkupBuildContext.cs`, `src/AcDream.App/UI/MarkupDocument.cs`, `src/AcDream.App/UI/PluginMarkupTheme.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupColorTests.cs`

**Acceptance Criteria:**
- [ ] `<color x y w h value onchange>` builds one `UiMarkupColor` with position, bindings, `AllowAlpha` from `alpha` (default false), `ApplyCommon` attributes and the document's `DatFont`.
- [ ] Bad bindings and a bad `alpha` throw `FormatException` naming `color value` / `color onchange` / `alpha`.
- [ ] `MarkupDocument.Build(..., textures:, sharedControlState:)` hands every `<color>` in the document (also inside a `group`) the same texture service and the same `MarkupRecentColors`, backed by `sharedControlState`.
- [ ] `theme="plugin"` sets `ThemePalette` (Moss) and the panel font on the swatch.
- [ ] `RetailUiRuntime.MountPlugins` passes `new RetailMarkupTextureService(_bindings.Assets.TextureCache)` and `_controlState.Shared`.
- [ ] A UiRoot-mounted test: click a built swatch, press the hue strip, tick → the binding gets the colour in its own type (string `#FF00FFFF`).

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~MarkupColorTests|FullyQualifiedName~MarkupHotkeyTests|FullyQualifiedName~AcDream.App.Tests.UI.Markup"` → all pass; `dotnet build AcDream.slnx -c Release` → 0 warnings.

**Steps:**

- [ ] **Step 1: Write the failing tests** — `tests/AcDream.App.Tests/UI/MarkupColorTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

public class MarkupColorTests
{
    private sealed class Binding
    {
        public uint Bar { get; set; } = 0xFFFF0000u;
        public string Text { get; set; } = "#FFFF0000";
        public double Wrong { get; set; }
        public uint? LastBar;
        public Action<uint> SetBar => v => { Bar = v; LastBar = v; };
        public Action<string> SetText => v => Text = v;
    }

    private sealed class FakeTextures : IMarkupTextureService
    {
        public uint Upload(byte[] rgba, int width, int height, string debugName) => 1u;
        public void Release(uint texture) { }
    }

    private sealed class FakeState : IMarkupControlState
    {
        public Dictionary<(string, string), string> Values { get; } = new();
        public int Generation => 1;
        public string? Get(string control, string kind) => Values.GetValueOrDefault((control, kind));
        public void Set(string control, string kind, string value) => Values[(control, kind)] = value;
        public void Remove(string control, string kind) => Values.Remove((control, kind));
    }

    private const string Attrs = "value=\"{Bar}\" onchange=\"{SetBar}\"";

    private static UiNineSlicePanel BuildPanel(string inner, object binding,
        IMarkupTextureService? textures = null, IMarkupControlState? shared = null) =>
        MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"300\" h=\"100\">{inner}</panel>",
            binding, _ => (1u, 16, 16), textures: textures, sharedControlState: shared);

    private static UiMarkupColor BuildOne(string attributes, object binding) =>
        Assert.IsType<UiMarkupColor>(Assert.Single(
            BuildPanel($"<color x=\"8\" y=\"8\" w=\"40\" h=\"18\" {attributes}/>", binding).Children));

    [Fact]
    public void Color_BuildsWithPositionBindingsAndAlpha()
    {
        var binding = new Binding();
        var color = BuildOne(Attrs + " alpha=\"true\" name=\"bar\" enabled=\"false\" tooltip=\"Bar\"", binding);
        Assert.Equal((8f, 8f, 40f, 18f), (color.Left, color.Top, color.Width, color.Height));
        Assert.Equal(0xFFFF0000u, color.ValueSource!());
        color.Changed!(0x80000000u);
        Assert.Equal(0x80000000u, binding.LastBar);
        Assert.True(color.AllowAlpha);
        Assert.False(color.Enabled);
        Assert.Null(color.Textures);
    }

    [Fact]
    public void Color_AlphaDefaultsToFalse() => Assert.False(BuildOne(Attrs, new Binding()).AllowAlpha);

    [Theory]
    [InlineData("onchange=\"{SetBar}\"", "color value")]
    [InlineData("value=\"#FFFF0000\" onchange=\"{SetBar}\"", "color value")]
    [InlineData("value=\"{Wrong}\" onchange=\"{SetBar}\"", "color value")]
    [InlineData("value=\"{Bar}\"", "color onchange")]
    [InlineData("value=\"{Bar}\" onchange=\"{SetText}\"", "color onchange")]
    [InlineData("value=\"{Text}\" onchange=\"{SetBar}\"", "color onchange")]
    [InlineData(Attrs + " alpha=\"yes\"", "alpha")]
    public void Color_BadAttributesFailTheBuild(string attributes, string context)
    {
        var ex = Assert.Throws<FormatException>(() => BuildOne(attributes, new Binding()));
        Assert.Contains(context, ex.Message);
    }

    [Fact]
    public void Color_EveryPickerSharesTheTexturesAndRecentColours()
    {
        var textures = new FakeTextures();
        var shared = new FakeState();
        var panel = BuildPanel(
            $"<color x=\"8\" y=\"8\" w=\"40\" h=\"18\" {Attrs}/>"
            + $"<group x=\"0\" y=\"40\" w=\"100\" h=\"40\"><color x=\"8\" y=\"8\" w=\"40\" h=\"18\" {Attrs}/></group>",
            new Binding(), textures, shared);
        var first = (UiMarkupColor)panel.Children[0];
        var second = (UiMarkupColor)panel.Children[1].Children[0];
        Assert.Same(textures, first.Textures);
        Assert.Same(textures, second.Textures);
        Assert.Same(first.Recent, second.Recent);
        first.Recent.Add(0xFF010203u);
        Assert.Equal("#FF010203", shared.Values[("color", "recent")]);
    }

    [Fact]
    public void Color_FollowsThePluginTheme()
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build(
            "<panel theme=\"plugin\" x=\"0\" y=\"0\" w=\"300\" h=\"100\">"
            + $"<color x=\"8\" y=\"8\" w=\"40\" h=\"18\" {Attrs}/></panel>",
            new Binding(), _ => (1u, 16, 16), themes: themes);
        Assert.Same(PluginUiPalette.Moss, panel.Children.OfType<UiMarkupColor>().Single().ThemePalette);
    }

    [Fact]
    public void Color_ThroughUiRootReportsInTheBindingsType()
    {
        var binding = new Binding();
        var panel = BuildPanel("<color x=\"8\" y=\"8\" w=\"40\" h=\"18\" value=\"{Text}\" onchange=\"{SetText}\"/>", binding);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var swatch = (UiMarkupColor)panel.Children[0];
        root.OnMouseDown(UiMouseButton.Left, 20, 15);
        root.OnMouseUp(UiMouseButton.Left, 20, 15);
        Assert.True(swatch.IsOpen);
        var (px, py, _, _) = swatch.Popup.PopupScreenRect;
        root.OnMouseDown(UiMouseButton.Left, (int)(px + UiMarkupColorPopup.HueX + 8), (int)(py + 6 + 64));
        root.OnMouseUp(UiMouseButton.Left, (int)(px + UiMarkupColorPopup.HueX + 8), (int)(py + 6 + 64));
        root.Tick(0.016, 16);
        Assert.Equal("#FF00FFFF", binding.Text);
    }
}
```

  If `UiNineSlicePanel` children include a title element, or the panel's own chrome, ahead of the markup elements, mirror how `MarkupHotkeyTests` picks its element (`Assert.Single(panel.Children)` works there, so it should here). For the `group` child, use `panel.Children[1].Children[0]`, adjusting if `UiPanel` groups wrap their children.

- [ ] **Step 2: Run, expect a compile failure** (`textures`/`sharedControlState` parameters missing).

- [ ] **Step 3: Context** — in `MarkupBuildContext.cs`, extend the record and its summary:

```csharp
/// ... <see cref="DescribeKey"/> is the host's key names for hotkeys (null shows
/// the canonical string). <see cref="Textures"/> uploads short-lived textures
/// (null: controls draw without them) and <see cref="RecentColors"/> is the
/// document's one recent-colours list.
internal sealed record MarkupBuildContext(
    Func<uint, (uint, int, int)> Resolve,
    UiDatFont? DatFont,
    IMarkupIconResolver? Icons,
    MarkupColorBinder BindColor,
    IMarkupControlState? ControlState = null,
    Action<UiElement, XElement>? AddChild = null,
    Func<KeyChord, string>? DescribeKey = null,
    IMarkupTextureService? Textures = null,
    MarkupRecentColors? RecentColors = null);
```

- [ ] **Step 4: Builder** — `src/AcDream.App/UI/Markup/MarkupColor.cs`:

```csharp
using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupColor
{
    public static UiMarkupColor Build(XElement el, object binding, MarkupBuildContext context)
    {
        var (read, write) = MarkupBindings.BindRequiredColor(
            (string?)el.Attribute("value"), (string?)el.Attribute("onchange"), binding, "color");
        var color = new UiMarkupColor
        {
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = MarkupDocument.F(el, "h"),
            ValueSource = read,
            Changed = write,
            AllowAlpha = Alpha(el),
            DatFont = context.DatFont,
            Textures = context.Textures,
        };
        if (context.RecentColors is not null)
            color.Recent = context.RecentColors;
        MarkupDocument.ApplyCommon(color, el, binding);
        return color;
    }

    private static bool Alpha(XElement el) => (string?)el.Attribute("alpha") switch
    {
        null or "false" => false,
        "true" => true,
        var other => throw new FormatException($"<color alpha=\"{other}\"> must be true or false"),
    };
}
```

- [ ] **Step 5: Document** — in `MarkupDocument.cs`:
  - `Build` gains two trailing optional parameters, `IMarkupTextureService? textures = null, IMarkupControlState? sharedControlState = null`. After the root checks, create `var recentColors = new MarkupRecentColors(sharedControlState);` and pass `textures, recentColors` to the `AddElement` call.
  - `AddElement` gains `IMarkupTextureService? textures, MarkupRecentColors recentColors` after `describeKey`. Pass them to every recursive `AddElement` call (the `Context()` `AddChild` closure and the `group` child loop; `grep -n "AddElement(" src/AcDream.App/UI/MarkupDocument.cs` lists them all).
  - `Context()` passes `describeKey, textures, recentColors` as the last three constructor arguments.
  - Add, after `case "hotkey":`,

```csharp
            case "color":
                parent.AddChild(MarkupColor.Build(el, binding, Context()));
                break;
```

  - Check that no existing `case "color"` exists (`grep -n '"color"' src/AcDream.App/UI/MarkupDocument.cs` shows only attribute reads).

- [ ] **Step 6: Theme** — in `PluginMarkupTheme.Register`, add to the font switch after the hotkey line:

```csharp
                case UiMarkupColor color: Bind(() => color.DatFont, f => color.DatFont = f); break;
```

and to the palette switch after the hotkey case:

```csharp
            case UiMarkupColor color:
                panel.AddThemeAction(p => color.ThemePalette = p);
                break;
```

  (Match the exact shape of the neighbouring `UiMarkupHotkey` cases, including any `break`/brace style.)

- [ ] **Step 7: Client** — in `RetailUiRuntime.MountPlugins`, before the `foreach`:

```csharp
        // Colour pickers upload their gradients while open and give them back on close.
        var markupTextures = new RetailMarkupTextureService(_bindings.Assets.TextureCache);
```

and add to the `MarkupDocument.Build` call:

```csharp
                    describeKey: describeKey,
                    textures: markupTextures,
                    sharedControlState: _controlState.Shared);
```

- [ ] **Step 8: Run the tests and the build**

```bash
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~MarkupColorTests|FullyQualifiedName~MarkupHotkeyTests|FullyQualifiedName~UiMarkupColorTests" 2>&1 | tail -5
dotnet build AcDream.slnx -c Release 2>&1 | grep -E ': error|: warning|Warning\(s\)|Error\(s\)' | sort -u
git checkout -- '*.lock.json' 2>/dev/null
```
Expected: all pass; `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 9: Commit**

```bash
git add src/AcDream.App/UI/Markup/MarkupColor.cs src/AcDream.App/UI/Markup/MarkupBuildContext.cs src/AcDream.App/UI/MarkupDocument.cs src/AcDream.App/UI/PluginMarkupTheme.cs src/AcDream.App/UI/RetailUiRuntime.cs tests/AcDream.App.Tests/UI/MarkupColorTests.cs
git commit -m "feat(ui): <color> markup element

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 5: Docs

**Goal:** `docs/plugin-ui-markup.md` documents `<color>`.

**Files:**
- Modify: `docs/plugin-ui-markup.md`

**Acceptance Criteria:**
- [ ] The Elements table gains, after `hotkey`: `| \`color\` | A colour swatch that opens a picker | \`x y w h value onchange alpha\` |`.
- [ ] The Bindings table gains `color value` (`uint`, `int` or `string` `#AARRGGBB`) and `color onchange` (`Action<T>` of the same type), both "throws".
- [ ] The focus paragraph (the one that says a `hotkey` takes focus on click) gains a sentence: a `color` swatch doesn't take focus on click, but its open picker holds the keyboard until it closes.
- [ ] A `## Colour picker` section after `## Hotkey` and before `## Radio group`. It covers: the example; the binding types and how bad text reads; `alpha`; the popup's parts; live reporting (at most once per frame); the hex field rules; the palette and recent rows (recent: 8, per character, shared by every picker, added on close with a change, not on Escape); how it closes (outside click/Enter/Tab/hide keep, Escape restores); looks; build errors; and the older-host line.
- [ ] Older-host line checked against base 391e709a: `git show 391e709a:src/AcDream.App/UI/MarkupDocument.cs | grep -n "unknown element"` shows unknown elements throw.

**Verify:** `grep -c '<color\|`color' docs/plugin-ui-markup.md` → at least 6.

**Steps:**

- [ ] **Step 1: Tables and focus paragraph** — edit as listed, keeping the tables' style.

- [ ] **Step 2: Section** — insert before `## Radio group`:

````markdown
## Colour picker

```xml
<color x="8" y="8" w="40" h="18" value="{BarColor}" onchange="{SetBarColor}" alpha="true"/>
```

A `<color>` shows a colour as a swatch. Clicking it, or Enter or Space while
it has focus, opens a picker below it (above it near the bottom of the
screen).

- `value` binds a `uint`, an `int` or a `string`, and `onchange` an `Action`
  of the same type; both are required. Numbers are `0xAARRGGBB`, the byte
  order of the `#AARRGGBB` literal; a string must be exactly `#AARRGGBB`
  (any case), and anything else shows as white. `onchange` hands strings back
  as `#AARRGGBB` in upper case.
- `alpha="true"` adds an alpha strip. Without it (the default) every colour
  the picker reports is opaque.
- The picker has a saturation/value square, a hue strip, the alpha strip, a
  hex field, a row of palette colours and a row of recently picked colours.
  The palette row is the theme's colours under `theme="plugin"`, and a fixed
  set of basic colours in Classic.
- `onchange` is called live while the player drags, at most once per frame,
  and only when the colour differs from `value`. The swatch always shows
  `value`, so a plugin that ignores the call keeps its colour.
- The hex field takes `#RRGGBB` or `#AARRGGBB` (the `#` is optional). Six
  digits keep the current alpha. It applies on Enter or when the player
  clicks elsewhere in the picker; text it can't read changes nothing.
- Clicking outside the picker, Enter (outside the hex field), Tab, or the
  swatch being hidden or disabled closes it and keeps the colour. Escape
  closes it and puts back the colour it opened with, calling `onchange` with
  that colour if it had already reported another.
- The recent row holds the last 8 colours picked with any `<color>`, newest
  first, remembered for the character. A colour is added when the picker
  closes with a colour different from the one it opened with (not on Escape).
- Clicking the swatch doesn't take keyboard focus from the game. The open
  picker holds the keyboard, and gives it back when it closes.

In Classic the swatch has a thin border that turns gold while it has focus or
is open, with a checkerboard behind translucent colours; under
`theme="plugin"` the swatch and picker take the palette's colours and the
panel's font.

A `<color>` without `value` or `onchange`, with either bound to an
unsupported type, with `onchange` of a different type from `value`, or with
`alpha` other than `true` or `false`, fails the build. On a host older than
colour pickers, a `<color>` fails the build of its window as unknown.
````

  Re-check every sentence against `UiMarkupColor.cs`, `UiMarkupColorPopup.cs` and `MarkupColor.cs`. Where they differ, fix the docs, not the code.

- [ ] **Step 3: Commit**

```bash
git add docs/plugin-ui-markup.md
git commit -m "docs(ui): colour pickers

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 6: Series gate

**Goal:** The Release build is clean, App, Plugin and UI.Abstractions tests have no new failures, and every commit builds.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 errors, 0 warnings.
- [ ] App + Plugin failures ⊆ `mc-base.txt` (162 names); UI.Abstractions failures ⊆ `mc-uiabs-base.txt` (empty).
- [ ] Each of the series' commits builds and passes the markup/colour test filters on its own.
- [ ] Nothing under `docs/superpowers/` on the branch.

**Verify:** both `comm -13` comparisons print nothing.

**Steps:**

- [ ] **Step 1: Release build and tests**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
T=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC
norm() { grep -E '^[[:space:]]+Failed [A-Za-z]' | sed -E 's/^[[:space:]]+Failed //; s/ \[[^]]*\]$//' | sort -u; }
dotnet build AcDream.slnx -c Release 2>&1 | grep -E ': error|: warning|Warning\(s\)|Error\(s\)' | sort -u
for p in tests/AcDream.App.Tests/AcDream.App.Tests.csproj tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj; do
  dotnet test "$p" -c Release 2>&1
done | norm > $T/mc12-after.txt
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 | norm > $T/mc12-uiabs-after.txt
echo "new App/Plugin:"; comm -13 $T/mc-base.txt $T/mc12-after.txt
echo "new UI.Abstractions:"; comm -13 $T/mc-uiabs-base.txt $T/mc12-uiabs-after.txt
git checkout -- '*.lock.json' 2>/dev/null; git status --short
```
Expected: `0 Warning(s)`, `0 Error(s)`, nothing after either "new" line. If `$T/mc-base.txt` is missing, rebuild it from 391e709a the same way (detached worktree, same `norm`) and say so.

- [ ] **Step 2: Each commit builds alone**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
for c in $(git rev-list --reverse 618e80d4..feat/markup-controls); do
  git worktree add -q --detach .worktrees/mc-commit-check $c
  (cd .worktrees/mc-commit-check && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release \
     --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup|FullyQualifiedName~MarkupColor|FullyQualifiedName~UiMarkupColor|FullyQualifiedName~Hotkey|FullyQualifiedName~ContextMenu" 2>&1 \
     | grep -E ': error|: warning|Passed!|Failed!' | sort -u | sed "s/^/$(git rev-parse --short $c) /")
  git worktree remove --force .worktrees/mc-commit-check
done
```
Expected: one `Passed!` per commit, no `error`/`warning`.

- [ ] **Step 3: Branch contents**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
git log --oneline 618e80d4..HEAD
git diff --stat 391e709a..HEAD | grep superpowers || echo "no superpowers docs"
```
Expected: the 5 commits of Tasks 1-5 (plus any review-fix commits); "no superpowers docs".

No commit.
