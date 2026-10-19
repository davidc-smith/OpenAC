# tools/

| Path | Purpose |
|---|---|
| `ShaderCompiler/` | .NET shader compiler over Silk.NET.Shaderc used by `compile-shaders.ps1`. Part of `AcDream.slnx`. |
| `RenderPackValidator/` | Validates a render pack against the SDK contract. Part of `AcDream.slnx`; `tests/AcDream.RenderPackValidator.Tests` covers it. |
| `compile-shaders.ps1` | Recompiles every GLSL source to the committed SPIR-V and rewrites the freshness manifest. |
| `build-linux.sh` | Linux-friendly Release build. Keeps .NET, NuGet, MSBuild, and generated lock state outside the checkout; pass `--test` for the portable test filter. |
| `run-release-gate.ps1` | The complete bounded local test gate: locked restore, Release build, every test assembly in its own timed process, TRX and hash evidence under `artifacts/release-gate/`. Its default `-TestFilter` is the portable filter CI copies. |
| `publish-bin.ps1` | Builds the self-contained client and launcher payloads, `manifest.json` and `launcher-fingerprint.json`. CI runs it in the release job; locally it is for inspection. |
| `launcher-fingerprint.ps1` | The launcher fingerprint `publish-bin.ps1` embeds and publishes; `-ListInputs` prints what it covers. |
| `run-launcher-trial.ps1` | Builds this branch's client and launcher and runs them together, isolated in `artifacts/launcher-trial/` from the tester's real OpenAC install. |
| `RynthNavData/` | RynthNav's navmesh tiles: `pin` records one archive of theirs in `external/rynth-navdata.json` (our own SHA-256), `install` installs exactly that one into `<RYNTHCORE_DIR>/NavData` with RynthCore's launcher code, `check-feed` shows what their signed feed offers. 1.6 GB download, about 4.7 GB on disk (needs about 6.4 GB free). Run with `dotnet run --project tools/RynthNavData -c Release -- <verb>`; run `install` with the same RYNTHCORE_DIR / RYNTHNAV_NAVDATA as the client, or pass --dir. |
| `plugin-extraction-dryrun.ps1` | Rehearses moving a plugin to its own repository, in scratch copies: builds and tests the client with the plugin deleted, and builds and tests the plugin on its own against the packed contract. See [extracting a plugin](../docs/plugins/extracting-a-plugin.md). |
| `update-package-locks.ps1` | Regenerates every neutral and RID NuGet lock file after a dependency change. |
