# Branches

This fork keeps three lines of work.

| Branch | Product | What it is |
|---|---|---|
| `upstream-main` | none | An exact mirror of `eriknihlen/OpenAC` `main`. Only ever fast-forwarded; never committed to. |
| `main` | OpenAC Gateway | Our client: upstream plus our changes. No Rynth. |
| `rynth` | OpenAC Gateway Rynth | `main` plus the Rynth host, plugins, overlay and navdata. |

## Merge direction

Merges only flow down: `upstream-main` → `main` → `rynth`.
Never merge `rynth` into `main`, or `main` into `upstream-main`.
Use merges, not cherry-picks, so `git log main ^rynth` shows what `rynth` has not taken yet.

## Where a change goes

- **A fix Erik should get:** branch from `upstream-main`, open the upstream PR from it,
  and merge the same branch into `main`. `rynth` gets it on its next merge from `main`.
- **A client change or fix:** on `main`.
- **A Rynth-only change or fix:** on `rynth`.
- **Something Rynth needs from the client:** if it is a general plugin or client API anyone
  could use, put it on `main`; only code that exists solely for Rynth goes on `rynth`.

## Syncing a new upstream release

1. `git fetch upstream` and fast-forward `upstream-main` to `upstream/main`; push it.
2. Merge `upstream-main` into `main`, resolve, build and test; push.
3. Merge `main` into `rynth`, resolve, build and test; push.

## Versions and releases

Version numbers, tags and the two launchers are described in
`docs/superpowers/specs/2026-10-22-gateway-branch-split-design.md` (sections 3 and 4)
and are being put in place; until then releases follow `docs/ci-and-releases.md`.
