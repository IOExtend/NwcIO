These are unmodified buildingSMART BCF-XML 2.1 schemas from the
`release_2_1` branch, commit `92fdcf4bfaa8f0576e7562730f341fc7781e2065`:

- https://github.com/buildingSMART/BCF-XML/blob/92fdcf4bfaa8f0576e7562730f341fc7781e2065/Schemas/markup.xsd
- https://github.com/buildingSMART/BCF-XML/blob/92fdcf4bfaa8f0576e7562730f341fc7781e2065/Schemas/visinfo.xsd

(c) buildingSMART International Ltd.,
Creative Commons Attribution-NoDerivatives 4.0 International (CC BY-ND 4.0).
License: https://creativecommons.org/licenses/by-nd/4.0/
The `markup.xsd` and `visinfo.xsd` files are unmodified, byte-identical copies.

They are embedded in the test assembly so validation needs neither network
access nor private Navisworks fixtures. Each schema is loaded separately
because both define a global `Guid` type in the empty namespace.
