# Private Test Fixtures & Clean-Room Policy

Some integration tests require sample Navisworks files (`.nwc`, `.nwd`, `.nwf`) that cannot be published due to intellectual property and confidentiality considerations. Those tests locate their input through an abstracted fixture resolver in the test suite and are reported as skipped when private fixtures are not present.

The repository contains **no proprietary models, customer files, or binary Navisworks caches**.

## Synthetic container measurements

The #27 workload can be reproduced with 24,000,000 bytes from `new Random(27).NextBytes(payload)`
and each byte reduced modulo 192. Write one stream with `NwcContainerWriter.Write([payload])`
using its default compression. This produces 22,548,544 compressed bytes on .NET 10.0.12 on Windows.
Compare `NwcContainerReader.Read(bytes)` with `ZLibStream.CopyTo(Stream.Null)` on the same compressed
bytes after the section header. Release build, three warm-up runs and a median of nine samples:

| Input | Reader returning decoded bytes | `ZLibStream.CopyTo(Stream.Null)` | Native inflate into an exact-size array |
| --- | ---: | ---: | ---: |
| #27 workload, 24 MB | 50.44 ms | 41.59 ms | 44.80 ms |
| Pseudorandom, 24 MiB | 3.92 ms | 0.64 ms | 3.47 ms |
| Pseudorandom first 22.5 MB, zero-filled last 1.5 MB | 9.88 ms | 1.35 ms | 6.63 ms |

The reader is 1.21× the native discard time on the #27 workload. The native discard baseline
on mostly stored DEFLATE data is much shorter, while returning the full payload requires an array.
A second inflate pass is reserved for streams whose validated trailer cannot be located uniquely.

For the memory check, open an in-memory container with eight copies of the #27 payload using
the default reader options, varying `KeepStreamCatalog`. Measure above a baseline taken after
allocating the input, with `GC.GetTotalMemory(false)` sampled at roughly 1 ms intervals during the open
and a full collection for the retained-memory figure. In the same Windows run, the sampled heap
peak was 366.4 MiB with the catalog disabled and 183.4 MiB with it enabled. These samples include
collectible allocations. At return, the corresponding live heap deltas were 0.0 MiB and 183.2 MiB.
A separate test scans a synthetic seekable input with a valid stream beyond 2 GiB.

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
