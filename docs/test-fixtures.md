# Private Test Fixtures & Clean-Room Policy

Some integration tests require sample Navisworks files (`.nwc`, `.nwd`, `.nwf`) that cannot be published due to intellectual property and confidentiality considerations. Those tests locate their input through an abstracted fixture resolver in the test suite and are reported as skipped when private fixtures are not present.

The repository contains **no proprietary models, customer files, or binary Navisworks caches**.

## What Is Discovered

Every `*.nwc`, `*.nwd` and `*.nwf` file under the roots below is discovered in deterministic (ordinal path) order. Each file runs as its own theory case, so one bad fixture does not stop the others. Without a sidecar file the tests assert only model-independent invariants (valid transforms, mesh vertex/face minimums, export headers and counts); no source application, loader plugin, element count or output size is assumed. The multi-model test is skipped when fewer than two fixtures exist.

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

`sourceContains` is matched case-insensitively against `FileInfo.SourcePath`; the other fields are minimum counts/sizes. A sidecar for `Sample1.nwc` is named `Sample1.expect.json`.

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
