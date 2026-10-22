# Plugin layout upgrade: flex upstream and `<repeat>`

Date: 2026-10-22. Status: agreed with the user (part 1 and 2a on 2026-10-22; 2b and 2c
confirmed after the flex port: copies straight into the container,
remembered state per item by key, docs-only plugin test support).

## Why

Aethrix asked for a `<repeat>` element (api-gaps #78, request page
https://claude.ai/artifact/3aKQAURCfwswFEDoFKWw3K) so one row template draws
once per item of a bound list. The request places copies as flex items
(its rule R6), and flex layout is fork-only. Rather than invent a non-flex
repeat for upstream, we take flex upstream too: it fits the controls work
already in upstream PR 236 and it is the last item of the upstream proposal
sent to Erik.

## Branches

Each branch is cut from the one before it, and each becomes one upstream PR.

| Branch | Base | Upstream PR |
|---|---|---|
| `feat/plugin-controls-upgrade` | `upstream-main` 391e709a | 236 (open) |
| `feat/plugin-flex-layout` | `feat/plugin-controls-upgrade` | B (new) |
| `feat/markup-repeat` | `feat/plugin-flex-layout` | C (new) |

Worktrees: `.worktrees/plugin-flex-layout`, later `.worktrees/markup-repeat`.
This spec and the plans live on the fork-only branch `docs/layout-upgrade-spec`,
never on the upstream branches.

Into fork `main`: `feat/markup-repeat` is merged into `main` (which brings
`feat/plugin-flex-layout` with it). Every flex conflict resolves to `main`'s
version, because `main` already has flex plus its modern-theme integration.
The check: `git diff` of `main` before and after the merge shows only the
repeat work and anything the port fixed that `main` lacked. `rynth` takes it
from `main` as usual. The fork contract moves to the next free version
(today `0.1.26-fork.5`, so `fork.6`, or the gateway scheme's next number if
plan 2 of the gateway split has landed by then).

---

## Part 1: the flex port (`feat/plugin-flex-layout`)

### Approach

Regroup, don't replay. Take the finished flex code from fork `main`
(2e0894ce or later), take out what is fork-only, and build it into a clean
commit series on PR 236's tip, the same way PR 236 itself was made. Every
commit builds with 0 warnings, and its failing tests match upstream's Mac
baseline (335 failure names across App/Core/Plugin/UI.Abstractions on
391e709a; re-measure on 1a777f1a before starting).

### What stays fork-only

- `PluginUiStyle` (modern rounded shapes). Where flex code calls it, which is the
  themed close button in `PluginTitleBar`, the scrollbar thumbs and slider in
  `UiScrollbar`, `PluginWindowChrome` and `MarkupContentSize`, a themed
  (`theme="plugin"`) window on the port draws with upstream's flat palette,
  like the rest of upstream's plugin theme.
- Markup canvas (`UiMarkupCanvas`, `InMarkup`) references in `MarkupDocument`,
  `MarkupContentSize` and `MarkupWindow`.
- The fork dock redesign in `PluginSidePanel`; only the title-bar pieces
  (the bar's X replacing the dock's "–") come across.
- Modern-shape tests (`PluginThemeControlTests`) and modern-shape assertions
  in other tests.
- The post-merge modern-theme fixes (SemiBold list header, ghost-X Confirm close).

### Commit series

1. **Engine**: `UI/Layout/Flex/*` (FlexLayout, FlexNode, FlexTypes, FlexFit)
   and its tests.
2. **Window chrome and title bar**: `PluginWindowChrome`, `PluginTitleBar`,
   `UiPluginContentHost`, `MarkupWindow`, `titlebar="true"`, the X replacing
   the dock "–", the frame minimum that counts the bar.
3. **Markup flex**: `layout`/flex container and item attributes
   (`MarkupFlexAttributes`), `UiFlexGroup`/`UiFlexBox`, `MarkupContentSize`
   for every control including the 13 from PR 236, the fitted window minimum,
   the flex menu popup width.
4. **Scrolling groups**: `scroll="y|x|both"` (`UiScrollArea`, `UiScrollPanel`),
   wheel routing in `UiRoot`, and the follow-ups (a short list/log leaves the
   wheel, a wheel step closes an open popup, hover re-checked after a step,
   the allocation test takes the least of five rounds). Shift+sideways
   scrolls sideways first, else steps vertically; this replaces PR 236's
   `RetainedWheel.Vertical` stopgap.
5. **New controls in flex**: expander reflow inside flex, split panes as
   flex containers, flex expander/split without w/h seeded before anchor
   baselines, the number inner field with no anchors (22640176), content
   sizes for the new controls.
6. **Bound visible**: a flex window's first size follows bound `visible`.
7. **Sample and docs**: FlexDemo (finder, settings, gallery, Flex Controls
   window) and the flex sections of `docs/plugin-ui-markup.md`
   (Flex layout, Scrolling groups, Title bar).

### Testing

The flex tests come with their code. A live check of FlexDemo and
ControlsDemo on the local ACE server, only after the user says they are
available.

---

## Part 2: `<repeat>` (`feat/markup-repeat`)

Markup:

```xml
<repeat items="{Rows}" key="{Id}">
  <group layout="row" gap="6" h="22">
    <checkbox checked="{On}" onchange="{SetOn}" w="20"/>
    <label text="{Label}" grow="1"/>
    <number value="{Value}" onchange="{SetValue}" w="80"/>
  </group>
</repeat>
```

The rules R1–R11 of the request apply as written, except where this part
says otherwise.

### 2a. Bindings resolve through a binding source (agreed)

Today every binding helper (about 30 lookups across `MarkupDocument` and
`Markup/*.cs`, about 60 `object binding` parameters) does
`binding.GetType().GetProperty(name)` and captures that object.
That cannot check names when a list is empty (R3), and a kept row could
not follow its key to a new item object (R5).

Each helper takes a `MarkupBindingSource` instead:

- `Type`: where names are looked up.
- `Current`: the object values are read from and callbacks invoked on.
- `Read(property)` / invoke helpers: give the type's default value, or do
  nothing, while `Current` is null.

A window's source is `Type = binding.GetType()` with a fixed `Current`, so
existing windows behave exactly as before. A repeat copy's source is
`Type = T` (subtype-only members are invisible) with a `Current` the repeat
updates. Inside a template nothing sees the outer item or the window
binding (R2). This change is its own commit with no behaviour change.

R3 with an empty list: at build time the repeat builds its template once
against a source whose `Current` is null, then throws that copy away
(see 2b, build scopes). Misspelt bool/int/list/required bindings throw the
same `FormatException` as today; silent-fallback attributes stay silent;
reads during this dry build return defaults, so no plugin code runs.

`items` must bind to a property declared as `IReadOnlyList<T>`, `IList<T>`
or `IEnumerable<T>`; any other type (including non-generic `IEnumerable`)
fails the build. `T` comes from that declared type. `key` binds to any
property on `T`.

### 2b. Copies, reconcile and cleanup (agreed)

**Placement (R6).** The builder adds a zero-size, non-flex marker element
(`UiMarkupRepeat`) to the flex container, in the position the `<repeat>`
was written. Copies' top-level elements are inserted into the container
directly after the marker, in item order, so flex layout, tab order and
drawing all treat them as if they had been written there. The marker is
excluded from flex items the same way the zero-size context menu is.
A `<repeat>` whose parent is not a flex container (window root or group
with `layout`) fails the build. It takes no `x y w h`.
`UiElement` gains an internal `InsertChild(index, child)` (and a move);
`AddChild` stays as is.

**Each tick (R4).** The marker reads `items`, then each item's key (or
uses the index when there is no `key`). If the count, the key sequence
and every item reference all match the last tick, it does nothing, even
when the list is a new instance (a computed `Where`/`ToList` list).
Otherwise it reconciles:

- keyed: a copy whose key is still present is kept and its source's
  `Current` set to the new item; copies for missing keys are removed; new
  keys get new copies; children are moved into the new order;
- no `key`: copies are matched by index, extras built or removed at the end;
- duplicate keys: logged once per repeat, then matched by index.

Rows edited in place cause no rebuild. 150 rows means 150 key reads a tick,
which is cheap next to the bindings those rows already read.

**Build scopes.** A build of one copy records everything it registers on
the window: theme actions (`AddThemeAction` gains a removable form),
remembered-state handles, tooltip/context-menu ownership. Removing a copy
disposes its scope and calls `UiRoot.ClearSubtreeOwnership` on its elements
(focus, capture, hover, an open popup). The dry build of 2a uses a scope
that is disposed at once. Nothing a removed copy registered survives.

**Nesting (R7).** A `<repeat>` inside a template is built as part of that
copy; its marker lives inside the copy, and its copies are removed with it.

**Remembered names (R8).** A `name` inside a template is stored as
`name` + `#` + the key path, the keys of every enclosing repeat
(invariant `ToString()`, joined with `/`). A named element under any
enclosing repeat without `key` fails the build.

**Inside templates (R9).** All existing attributes work. `canvas` does not
exist upstream; the fork merge adds "a canvas inside a template fails the
build" on `main`.

### 2c. Limits, tests, docs, sample (agreed)

**Performance (R10).** 150 copies of a five-control row in a scrolling
group. A test measures reconcile plus arrange for that shape and bounds it
(set from a measured baseline, the same style as the flex allocation
test). The live check scrolls it by eye.

**Older hosts (R11).** Unchanged: an unknown element fails the build.

**Plugin-side checks.** The host has no plugin-facing markup test helper;
Aethrix walks its own XML (`Aethrix.Tests/Ui/MarkupTests.cs`). The docs
state the scope rule precisely (inside `<repeat items="{X}">` names
resolve on the element type of property `X`) so Aethrix's walker can
follow it. No new public API.

**Tests** (markup tests, fake resolvers, as the request lists): empty,
one-item and many-item lists; misspelt template binding throws with an
empty list; in-place change does not rebuild; a new list with the same
keys keeps a focused field's typed text; a removed key drops its copy and
its theme actions; a reorder; nested repeats; a named expander remembered
per key; named element without key fails; a non-flex parent fails;
duplicate keys fall back to index; the R10 bound.

**Docs.** A Repeat section in `docs/plugin-ui-markup.md`, a row in the
element table, the scope rule, and (on `main` only) the contract note.

**Sample.** A ControlsDemo "Repeat" window: rows of checkbox, label, menu
and number in a scrolling group, with Add, Remove and Shuffle buttons, so
keyed state (a half-typed number, an open menu) visibly survives.

**Done when** `<repeat>` meets R1–R10 on the upstream branch, the tests
pass, docs and sample are in, it is merged into fork `main` with the next
contract version, and Aethrix is told the version and commit.
