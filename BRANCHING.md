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
2. Merge `upstream-main` into `main`, resolve, build and test; push. Set `<Version>` to the new
   upstream version, `OpenACReleaseNumber` to 1 and `ForkContractVersion` to `<Version>-fork.1`.
3. Merge `main` into `rynth`, resolve, build and test; push. Reset `OpenACReleaseNumber` in
   `Directory.Branch.props` to 1.

## Versions and releases

- `<Version>` in `Directory.Build.props` is upstream's version (e.g. `0.1.26`). It changes only
  when an upstream release is merged, because every lock file records it.
- Each product has its own release number:
  - `main` ships **OpenAC Gateway** as `x.y.z-gateway.N` (`OpenACProduct`, `OpenACReleaseLabel`,
    `OpenACReleaseNumber` in `Directory.Build.props`).
  - `rynth` ships **OpenAC Gateway Rynth** as `x.y.z-rynth.N`, set in `Directory.Branch.props`,
    which exists only on `rynth` and must never be added to `main`.
  - Bump the product's `OpenACReleaseNumber` for each release; it goes back to 1 when an upstream
    release is merged.
- The plugin contract (`ForkContractVersion` in `AcDream.Plugin.Abstractions.csproj`) keeps its own
  `x.y.z-fork.N` counter, bumped only when the plugin API changes.
- Releases are tagged `gateway-v<version>` (from `main`) and `gateway-rynth-v<version>` (from
  `rynth`), never plain `v…`. Each launcher reads the repository's `releases.atom` feed and installs
  only the newest release with its own prefix; see `docs/ci-and-releases.md` (Fork releases).
