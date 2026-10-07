# Azure Functions CLI 4.16.0

#### Host Version

- Host Runtime Version: 4.1054.250
- In-Proc CLI:
  - CLI Version: 4.9.0
  - Host Runtime Version: 4.52.200 (includes 4.852.200, 4.652.200)

#### Breaking Changes

- **Python 3.9 is no longer supported.** This version has reached end-of-life. Please upgrade to Python 3.10 or later to continue using Azure Functions Core Tools.

#### Changes
- The Go runtime is now generally available on Flex Consumption, with preview support on Elastic Premium and Dedicated Linux plans. Windows and Linux Consumption plans are not supported.
- Add explicit .NET 11 C# isolated initialization with updated templates. The default remains .NET 10; .NET 11 F# scaffolding and Docker support are not yet available.
- Support worker-indexed .NET isolated build and publish outputs in `func pack`, including `--no-build`, without requiring a separate `functions.metadata` file.
- Fixed `func kubernetes deploy` failing with `Invalid property identifier character: {` for dotnet-isolated projects with more than one function. The `print-functions.sh` script previously used a `sed` command that only captured the last function name and produced malformed JSON; replaced with `awk` to correctly key each function object by its `name` field. (#3585)
