# Azure Functions CLI 4.15.1

#### Host Version

- Host Runtime Version: 4.1053.200
- In-Proc CLI:
  - CLI Version: 4.8.0
  - Host Runtime Version: 4.52.100 (includes 4.852.100, 4.652.100)

#### Changes
- The Go runtime is now generally available on Flex Consumption, with preview support on Elastic Premium and Dedicated Linux plans. Windows and Linux Consumption plans are not supported.
- Add explicit .NET 11 C# isolated initialization with updated templates. The default remains .NET 10; .NET 11 F# scaffolding and Docker support are not yet available.
- Support worker-indexed .NET isolated build and publish outputs in `func pack`, including `--no-build`, without requiring a separate `functions.metadata` file.
- Fixed `func kubernetes deploy` failing with `Invalid property identifier character: {` for dotnet-isolated projects with more than one function. The `print-functions.sh` script previously used a `sed` command that only captured the last function name and produced malformed JSON; replaced with `awk` to correctly key each function object by its `name` field. (#3585)
