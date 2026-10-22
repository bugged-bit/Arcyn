# Contributing to ARCYN

Small, focused contributions are welcome: a reproducible bug report, a clearer instruction, an accessibility fix, or a well-tested change to a workspace workflow.

## Run the project

Use Windows x64 and the .NET SDK selected by [global.json](https://github.com/bugged-bit/Arcyn/blob/codex/arcyn-lightweight/global.json). The current candidate is developed on `codex/arcyn-lightweight`; `master` still contains the previous version.

From a clone of the repository:

```powershell
git switch codex/arcyn-lightweight
dotnet build ARCYN/ARCYN.UI/ARCYN.UI.csproj -c Release
```

Use a disposable configuration when running the application. `--config` must point to an existing file; copy the bundled example and replace its targets before launching anything. Don't test against personal workspaces or rearrange someone else's desktop.

## Check a change

Run these from the repository root:

```powershell
./ARCYN/tools/Test-Arcyn.ps1
./ARCYN/tools/Test-Scripts.ps1
```

The regression runner renders interface states offscreen and opens no application windows or targets. Visible tests require an available desktop and safe test applications. Preserve the cinematic timings.

To build the complete standalone ZIP, install the Visual Studio C++ build tools for the optional native hotkey helper, then use a new output directory:

```powershell
./ARCYN/tools/Publish-Arcyn.ps1 -OutputDirectory .arcyn-local/package-check
./ARCYN/tools/Test-ArtifactMetadata.ps1 -Directory .arcyn-local/package-check-release
```

Publishing here builds a local package; it does not create a GitHub release. Distributing only `ARCYN.exe` is insufficient. For runtime servicing, update the bundled runtime and its notices together.

## Send a contribution

Fork the repository and create a branch from the version you're changing. For the 1.6 candidate, target `codex/arcyn-lightweight`. Keep the pull request focused, describe the user-visible result, and include the checks you ran and anything you couldn't verify.

Follow the surrounding C#/XAML style and reuse existing components. Preserve configuration compatibility, validation, command review, cancellation, and user data. Use comments to explain a decision rather than restating the code. Don't commit personal configuration, machine-specific paths, build outputs, or session transcripts.

Report the Windows version, ARCYN version, display scale, and steps to reproduce a bug. Logs are at `%LOCALAPPDATA%\ARCYN\arcyn.log`. Redact personal paths and command arguments before sharing logs or configuration. The [issue templates](https://github.com/bugged-bit/Arcyn/issues/new/choose) provide a starting point.

## Project references

- [User guide](docs/guide.md): configuration, shortcuts, recovery, and troubleshooting.
- [Live validation](docs/live-validation.md) and [implementation evidence](docs/implementation-validation.md): completed checks and their limits.
- [Redistribution notices](https://github.com/bugged-bit/Arcyn/tree/codex/arcyn-lightweight/ARCYN/Notices): fonts, Fluent contributions, and bundled runtime licenses.

Earlier design handoffs and session notes remain in Git history. The validated .NET 8 checkpoint is tagged `arcyn-net8-validated`; build outputs are disposable, user configurations are not.
