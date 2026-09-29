# Azure Functions CLI 4.15.2

#### Host Version

- Host Runtime Version: 4.1053.200
- In-Proc CLI:
  - CLI Version: 4.8.0
  - Host Runtime Version: 4.52.100 (includes 4.852.100, 4.652.100)

#### Changes

- Added fix for an MSRC and  core-tools docker support for .NET 11.
- Add Dockerfile generation (`func init --docker` / `--docker-only`) for .NET 11 isolated projects using the chiseled `mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated11.0-chiseled` base image. (#5599)
- Require explicit confirmation before `func bundles download --force` recursively deletes an existing extension bundle directory. (#5666)
