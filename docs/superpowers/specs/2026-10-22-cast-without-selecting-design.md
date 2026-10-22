# Cast at a Target Without Selecting It — Design

Date: 2026-10-22. Follow-up named in
`2026-10-22-gateway-branch-split-design.md` ("Follow-up: cast at a target
without selecting it"). Gateway split plan 4.

## Problem

Aethrix casts every spell through `MagicSession.Send`
(`OpenAC-Plugins-Aethrix/src/Aethrix/Core/Magic/MagicSession.cs`), which calls
`IMagicCommands.EvaluateGate(spellId, target)` and then
`IMagicCommands.RequestCast(spellId, target)`. In `RuntimeAutomationSurface`
both overloads go through `SelectExplicitTarget`, so every combat or buff cast
moves the player's selection to the target, twice.

The runtime already casts without the selection:
`RuntimeSpellCastState.Cast(spellId, targetId)` neither reads nor changes it
(as the original client's FreeHandsAndCastSpell does for a plugin). Only the
Rynth host uses it. The gate check `RuntimeSpellCastState.EvaluateCastGate`
has no target form: it reads the selection.

## Decision

Additive API (approach 1). The two existing target overloads keep selecting,
as their documentation says, so upstream behaviour and every other plugin are
unchanged and a later upstream PR is purely additive.

### Runtime (`RuntimeSpellCastState`)

- New `SpellCastGate EvaluateCastGate(uint spellId, uint targetId)`: the same
  checks as today with `targetId` in place of the selection; `0` means no
  target (`NoTargetSelected` for a targeted spell). Self, untargeted and
  mask-0 spells answer `NoTargetNeeded` whatever the target.
- `EvaluateCastGate(uint spellId)` becomes
  `EvaluateCastGate(spellId, _selection.SelectedObjectId ?? 0u)`.

### Plugin contract (`IMagicCommands`)

Two new default interface members:

```csharp
PluginCastGate EvaluateGateWithoutSelecting(uint spellId, uint targetObjectId)
    => PluginCastGate.Refused;
PluginCastRequestResult RequestCastWithoutSelecting(uint spellId, uint targetObjectId)
    => PluginCastRequestResult.Unavailable;
```

A host without them refuses; it never falls back to selecting. The inert
surface (`NoOpAutomationSurface`) answers `Unavailable` for both. No `bool`
`Cast…` variant (YAGNI: `RequestCast…` says more).

### Host (`RuntimeAutomationSurface`)

- A private `IsCastTarget(targetObjectId)`: the checks `SelectExplicitTarget`
  makes (bound runtime, available, non-zero, active entity) without the
  `Select` call. `SelectExplicitTarget` reuses it.
- `EvaluateGateWithoutSelecting`: `Unavailable` / `NotKnown` exactly as
  `EvaluateGate(spellId)`; `Refused` when `IsCastTarget` fails; otherwise maps
  `EvaluateCastGate(spellId, target)` the same way `EvaluateGate(spellId)`
  maps its result.
- `RequestCastWithoutSelecting`: `IncompatibleTarget` when `IsCastTarget`
  fails; otherwise `cast.Cast(spellId, target)` mapped the same way
  `RequestCast(spellId)` maps its result.
- Neither touches `ActionOwner.Selection`.

Note: the target must be an active entity even for a self-targeted spell,
matching the selecting overloads. Aethrix passes the creature or the player.

### Contract version

`ForkContractVersion` `0.1.26-fork.5` → `0.1.26-fork.6`. `docs/plugin-api.md`
gets a short "Casting without selecting" note with the contract requirement,
in the style of the other fork members.

### Branches

Work on `feat/cast-without-selecting` from `main`, merged to `main`
(`--no-ff`), then `main` → `rynth` as usual. Upstream-portable (no fork-only
types), but no upstream PR in this plan.

### Aethrix

- `PluginApiPackageVersion` → `0.1.26-fork.6`.
- `MagicSession.Send` calls `EvaluateGateWithoutSelecting` and
  `RequestCastWithoutSelecting`.
- `FakeAutomation`'s magic fake implements both (same `RefusedTargets`,
  `Gate`, `NextResult`, `Casts` behaviour) and counts calls to the selecting
  overloads, so a test can show the session no longer uses them.
- `docs/api-gaps.md` gets a resolved entry for this gap.

## Testing

- Runtime: `EvaluateCastGate(spell, target)` answers from the given target
  with a different object selected; `0` is `NoTargetSelected`; self spells
  need no target; the one-argument form still reads the selection.
- Host: with object A selected, `EvaluateGateWithoutSelecting` /
  `RequestCastWithoutSelecting` at active object B answer `Ready` / `Sent`
  and leave A selected; an inactive target is `Refused` /
  `IncompatibleTarget` with nothing sent; the inert surface answers
  `Unavailable`; the existing `RequestCast(spell, target)` still selects.
- Aethrix: the full test suite passes; a magic-session test asserts no
  selecting overload was called.
- Live check (user, when available): Aethrix combat casts at a creature
  while a different object stays selected.

## Out of scope

Upstream PR; a `bool` cast variant; changing what the existing overloads do;
Rynth (it already uses the runtime path).
