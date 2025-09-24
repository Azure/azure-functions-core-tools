# Azure Functions CLI 4.3.0

#### Host Version

- Host Version: 4.1042.100


#### Changes

- Log the resolved worker runtime and `local.settings.json` location, if found.
- Add `func pack` basic functionality (#4600) 
- Add `func pack` basic functionality (#4600)
- Clean up HelpAction and add `func pack` to help menu (#4639)
- Add comprehensive pack validations for all Azure Functions runtimes (#4625)
  - Enhanced user experience with real-time validation status (PASSED/FAILED/WARNING)
  - Runtime-specific validations for .NET, Python, Node.js, PowerShell, and Custom Handlers
  - Actionable error messages to help developers resolve issues during packaging
  - Validates folder structure, required files, programming models, and runtime-specific configurations
- Add support for linux-arm64 (#4655)
- Updated Host version with 4.1042.100