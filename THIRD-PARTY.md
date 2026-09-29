# Third-Party Components

## Runtime Dependencies

- NAudio.Core and NAudio.WinMM 2.2.1, MIT. [Upstream](https://github.com/naudio/NAudio). Official NuGet packages are restored with pinned hashes. License: `experiments/tencent-streaming-probe/NAudio-LICENSE.txt`.
- Microsoft.Diagnostics.Tracing.TraceEvent 3.2.6 and its FastSerialization library, MIT. [Upstream license](https://github.com/microsoft/perfview/blob/v3.2.6/LICENSE.TXT). Official NuGet package is restored with a pinned hash. License: `docs/TraceEvent-LICENSE.txt`.
- Microsoft .NET Desktop Runtime and Windows .NET Framework are prerequisites installed separately; they are not redistributed in the release ZIP.

The release includes only the managed ETW file-event receiver dependencies, not native symbol or relogging tools. Dependencies retain their own license terms.

Paths above refer to this source repository. In the binary release ZIP, both license texts are included beside the libraries in `Assistant/`.

## Development Dependencies

Fengari Node CLI, Fengari, luaparse and their transitive dependencies are development-only npm packages. Exact versions are recorded in `package-lock.json`; npm installs retain their license files. They are not needed by players and are not included in the release ZIP.

## Excluded Material

This repository does not redistribute QuestEcho NPC/quest lookup tables, extracted game identity databases, game recordings, cloned-voice reference samples or account-specific generated voice catalogs. Empty lookup tables preserve the existing addon interfaces without importing the original data.

The project-provided desktop and addon icons are retained from the application's existing assets. No additional third-party art is introduced by this release.

Game names and service names identify interoperability targets and remain the property of their respective owners. This is an independent project, not an official game or cloud-provider product.
