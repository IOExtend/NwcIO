# Third-party notices

NwcIO is a 100% pure managed .NET implementation developed clean-room for open BIM interoperability.

## Dependencies

The core library `NwcIO` has zero third-party dependencies and relies solely on the standard .NET runtime (`net8.0` and `net10.0`).

| Component | Use | License |
|---|---|---|
| .NET Runtime Libraries | Base class library, I/O, Deflate decompression | MIT, copyright (c) .NET Foundation and Contributors |

Development and build-time packages (xunit, Microsoft.NET.Test.Sdk) are not part of the published packages.

## BCF 2.1 test schemas

`tests/NwcIO.Tests/Schemas/Bcf21/markup.xsd` and `visinfo.xsd` are unmodified,
byte-identical copies from [buildingSMART/BCF-XML](https://github.com/buildingSMART/BCF-XML),
branch `release_2_1`, commit `92fdcf4bfaa8f0576e7562730f341fc7781e2065`:

- https://github.com/buildingSMART/BCF-XML/blob/92fdcf4bfaa8f0576e7562730f341fc7781e2065/Schemas/markup.xsd
- https://github.com/buildingSMART/BCF-XML/blob/92fdcf4bfaa8f0576e7562730f341fc7781e2065/Schemas/visinfo.xsd

(c) buildingSMART International Ltd.,
Creative Commons Attribution-NoDerivatives 4.0 International (CC BY-ND 4.0).
License: https://creativecommons.org/licenses/by-nd/4.0/
These test schemas retain their upstream license separately from NwcIO's MIT license.

## Trademarks & Clean-Room Statement

- Autodesk, Navisworks, and Revit are trademarks or registered trademarks of Autodesk, Inc. in the USA and other countries.
- NwcIO is an independent project and is not affiliated with, endorsed by, or sponsored by Autodesk, Inc.
- The Autodesk Navisworks binary file formats (`.nwc`, `.nwd`, `.nwf`) are parsed by analyzing file structures solely for the purpose of software interoperability.
- No Autodesk code, headers, SDKs, or binaries are included or required.
