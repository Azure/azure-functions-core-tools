# Azure Functions CLI 4.5.0

#### Host Version

- Host Runtime Version: 4.1044.400
- In-Proc CLI:
  - CLI Version: 4.3.0
  - Host Runtime Version: 4.44.100 (includes 4.844.100, 4.644.100)

#### Changes

- Fix .gitignore to allow PowerShell module bin folders (#4574)
- Refactor to use msbuild for determining .NET target framework & add support multiple TFMs (#4715)
  - When using `func init --docker-only` on a .NET project with multiple target frameworks, the CLI will now
   prompt the user to select which target framework to use for the Dockerfile.
