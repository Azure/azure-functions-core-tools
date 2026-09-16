# Azure Functions CLI 4.14.0

#### Host Version

- Host Runtime Version: 4.1052.200
- In-Proc CLI:
  - CLI Version: 4.7.0
  - Host Runtime Version: 4.51.100 (includes 4.851.100, 4.651.100)

#### Changes
- Add explicit .NET 11 C# isolated initialization with updated templates. The default remains .NET 10; .NET 11 F# scaffolding and Docker support are not yet available.
- Support worker-indexed .NET isolated build and publish outputs in `func pack`, including `--no-build`, without requiring a separate `functions.metadata` file.
- Fixed Python native dependency builds to inherit authenticated pip indexes in Docker containers (#5574)
- Make host.json optional across func publish, and pack (#5488)
