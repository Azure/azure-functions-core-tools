# Azure Functions CLI 4.5.0

#### Host Version

- Host Runtime Version: 4.1044.400
- In-Proc CLI:
  - CLI Version: 4.3.0
  - Host Runtime Version: 4.44.100 (includes 4.844.100, 4.644.100)

#### Changes

- Add deprecation warning for extension bundles during function app publish (#4700)
  - Core Tools now displays a warning when publishing function apps that use deprecated extension bundle versions
  - Warning message matches VS Code Azure Functions extension behavior for consistency
  - Supports version range formats: `[X.*, Y.Y.Y)`, `[X.Y.Z, A.B.C)`, and exact versions `[X.Y.Z]`
  - Falls back to default version range `[4.*, 5.0.0)` if CDN endpoint is unreachable
- Fix .gitignore to allow PowerShell module bin folders (#4574)
- Refactor to use msbuild for determining .NET target framework & add support multiple TFMs (#4715)
  - When using `func init --docker-only` on a .NET project with multiple target frameworks, the CLI will now
   prompt the user to select which target framework to use for the Dockerfile.
- Enhanced dotnet installation discovery by adopting the same `Muxer` logic used by the .NET SDK itself (#4732)
- Update .NET templates package version to 4.0.5337 (#4728)
- Fix `func pack --build-native-deps` failure on Windows for Python 3.13+ (#4742)
