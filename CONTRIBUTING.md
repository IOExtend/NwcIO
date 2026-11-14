# Contributing to NwcIO

## Reporting Bugs

Open a [GitHub issue](https://github.com/IOExtend/NwcIO/issues) with:
- The Navisworks / CAD software version that saved the file.
- The `NwcIO` / `nwcio` version.
- The command line used, with confidential or personal paths removed.
- A minimal description of how to reproduce the issue.
- Expected and actual results, including relevant error messages or stack traces.

Do not attach proprietary Navisworks files if they contain confidential or customer data.

Report security vulnerabilities privately through the **Security** tab.

## Code Conventions & Clean-Room Policy

- **Clean-Room Development**: All code must be independently authored without decompiled proprietary source code, proprietary headers, or vendor SDKs.
- Do not commit proprietary Navisworks models (`.nwc`, `.nwd`, `.nwf`), customer project data, or third-party binaries to the repository.
- Keep nullable reference types enabled (`<Nullable>enable</Nullable>`).
- Warnings are treated as errors (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`).
- Zero native or third-party library dependencies for core reading and exporting functionality.

## Test Fixtures

Tests requiring real Navisworks files are skipped automatically unless the environment variable `NWCIO_TEST_FILES` is set to a directory containing private test models. The repository ships no proprietary Navisworks models.

## Releases & NuGet Publishing

Push a tag matching the package version, such as `v0.1.0`, to trigger the `release.yml` workflow.
The workflow builds and tests the solution, packs release packages and symbols, generates archives with checksums, publishes the GitHub Release, and publishes packages to NuGet.org via Trusted Publishing (GitHub Actions OIDC).
The NuGet policy on NuGet.org must name this repository (`IOExtend/NwcIO`), workflow `release.yml`, publisher `GitHubActions`, package pattern `NwcIO*`, and owner `mahach`.

## Scene adapter

`src/NwcIO.Scene` converts NwcIO models into `IOScene` documents and uses only the public API of `NwcIO`; the `NwcIO`
package itself keeps no dependencies. `IOScene` is referenced with the range `[0.1.0, 0.2.0-0.0)`, and restore takes the
lowest version in it, 0.1.0 from NuGet.org. To build against an `IOScene` version that is not on NuGet.org yet, start
the range in `Directory.Packages.props` at that version (for a new minor, move the upper bound too, for example
`[0.2.0-preview.1, 0.3.0-0.0)`) and restore from a local folder of packages:
`dotnet build NwcIO.slnx -p:RestoreAdditionalProjectSources=<folder>`. Changing the `SceneModel.Key`, `SourceId` or
`IfcGuid` the adapter writes, its `nwc:` key format, or the value form (`Text`, `Kind`, `Unit`) it writes under a key
fails the golden identity test and is a breaking release.

## Licensing Contributions

All contributions are licensed under the [MIT License](LICENSE). By submitting a pull request, you confirm that you have the right to contribute your code under that license.
