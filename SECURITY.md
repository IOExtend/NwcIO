# Security policy

## Supported versions

NwcIO is in preview (0.x). Security fixes are made on the latest release only.

## Reporting a vulnerability

Please do not open a public issue for a security problem. Use GitHub private vulnerability reporting:
open the Security tab of the repository and choose "Report a vulnerability". Include the version of NwcIO, the
command or API call, and a description of the problem. A file that triggers it helps, but do not send files with
confidential content; a crafted or cut-down file is usually enough.

You can expect an acknowledgement within a few days. Fixes are released as soon as they are verified, and the report
is credited unless you prefer otherwise.

## Scope

NwcIO parses files that can come from untrusted sources. Problems that matter most:

- crashes, hangs or unbounded memory use caused by a crafted `.nwc`, `.nwd` or `.nwf` file;
- writing files outside the folder or path a command was given (path traversal in output paths);
- any behaviour that executes code or contacts the network (the library does neither).
