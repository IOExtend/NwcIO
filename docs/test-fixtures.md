# Private Test Fixtures & Clean-Room Policy

Some integration tests require sample Navisworks files (`.nwc`, `.nwd`, `.nwf`) that cannot be published due to intellectual property and confidentiality considerations. Those tests locate their input through an abstracted fixture resolver in the test suite and are reported as skipped when private fixtures are not present.

The repository contains **no proprietary models, customer files, or binary Navisworks caches**.

## Synthetic container measurements

On Windows, with a .NET 10 Release build and one 24 MiB pseudorandom payload (`Random(27)`),
the median of seven runs was 8.0 ms for the container reader and 0.91 ms for
`ZLibStream.CopyTo(Stream.Null)`. The container reader measures the exact end of the zlib stream
and returns its decoded bytes. The second inflate pass still adds cost.

With eight such streams in one in-memory container, the managed heap sample immediately after
scanning was about 192 MiB above baseline with the catalog retained and 0.13 MiB above baseline
with `KeepStreamCatalog = false`. The input buffer was already allocated at baseline. Samples
used `GC.GetTotalMemory(true)` at the `read-container` progress callback, so they measure live
objects at that phase. A separate test scans a synthetic seekable input with a valid stream
beyond 2 GiB.

## What Is Discovered

The per-model tests (`RealFixtureTests`) discover every `*.nwc`, `*.nwd` and `*.nwf` file under the roots below and run each as its own theory case, so one bad fixture does not stop the others. Cases are identified by position in the fixture list, never by path, so no private name reaches the test output. `.nwc` and `.nwd` files are opened with the scene tree; `.nwf` files are opened with `BuildSceneTree = false` and only the checks that apply to the data present run. Without a sidecar file the tests assert only model-independent invariants (valid transforms, mesh and index sanity, export structure and counts, BVH queries, a `.nwc` save/open round trip); no source application, loader plugin, element count or output size is assumed. The multi-model test uses the first two `.nwc`/`.nwd` fixtures and is skipped when fewer than two exist. The other real-file suites (`RealFileInvariantTests`, `RealCorpusTests`) keep reading `.nwc` and `.nwd` only.

## Optional Per-Fixture Expectations

A fixture may carry `<name>.expect.json` next to it. Only present values are asserted:

```json
{
  "sourceContains": ".rvt",
  "loaderPlugin": "LcNwcLoaderPlugin:lcldrevit",
  "minGuids": 1000,
  "minPropertyRecords": 500,
  "minTransforms": 1000,
  "minMeshes": 500,
  "minViewpoints": 10,
  "minGlbBytes": 100000,
  "minIfc2x3Bytes": 100000,
  "minIfc4Bytes": 50000
}
```

`sourceContains` is matched case-insensitively against `FileInfo.SourcePath`; the other fields are minimum counts/sizes. A sidecar for `Sample1.nwc` is named `Sample1.expect.json`; it applies to every fixture with that file name stem, so keep one stem per model. An unknown key is an error, so a typo cannot silently disable an expectation.

## Where Fixtures Are Looked For

1. The path or folder specified by the `NWCIO_TEST_FILES` environment variable.
2. An optional `test_files` folder in the repository root (or in any parent directory of the test binary).

```
NwcIO/
├── src/
├── tests/
└── test_files/               <-- Uncommitted, ignored by git
    ├── Sample1.nwc
    └── Sample2.nwc
```

If neither is set, tests that rely on external binary models skip cleanly with zero failure. All core unit tests run on synthetic streams and procedurally generated geometry.

## Running Tests with Private Fixtures

PowerShell:
```powershell
$env:NWCIO_TEST_FILES = "C:\path\to\fixtures"
dotnet test -c Release
```

Bash / Linux:
```bash
NWCIO_TEST_FILES=/path/to/fixtures dotnet test -c Release
```

## Temporary Diagnostic Dumps

Tests that generate output (such as IFC, glTF, OBJ, or BCF files) write exclusively to the system temporary directory (`Path.GetTempPath()`) with unique GUID suffixes, and clean up their files in `finally` blocks. No fixed absolute paths or local directory paths are hardcoded.
