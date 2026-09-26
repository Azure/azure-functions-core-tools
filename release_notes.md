# Azure Functions CLI 4.15.1

#### Host Version

- Host Runtime Version: 4.1053.200
- In-Proc CLI:
  - CLI Version: 4.8.0
  - Host Runtime Version: 4.52.100 (includes 4.852.100, 4.652.100)

#### Changes

- Add Dockerfile generation (`func init --docker` / `--docker-only`) for .NET 11 isolated projects using the chiseled `mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated11.0-chiseled` base image. (#5599)
