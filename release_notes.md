# Azure Functions CLI 4.6.0

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
- Enhanced dotnet installation discovery by adopting the same `Muxer` logic used by the .NET SDK itself (#4732)
- Update .NET templates package version to 4.0.5337 (#4728)
- Fix `func pack --build-native-deps` failure on Windows for Python 3.13+ (#4742)
- Update the TypeScript project template to improve interoperability (#4739)
  - Upgrade `typescript` from `^4.0.0` to `^5.0.0`
  - Add `"esModuleInterop": true` option to `tsconfig.json`
- Cleaned up `func --help` output and fixed validation errors when using the `--help` flag for specific commands (#4748)
- Improved `func init --help` output to better display options for each worker runtime (#4748)
- Fix F# project & template initialization via `func init | new` (#4749)
- Log a warning if remote build is used for Python 3.14 flex app (#4755)
