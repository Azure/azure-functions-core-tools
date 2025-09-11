# Azure Functions CLI 4.3.0


#### Host Version

- Host Version: 4.1041.200
- In-Proc Host Version: 4.41.100 (4.841.100, 4.641.100)

#### Changes
- Add `func pack` basic functionality (#4600) 
- Add comprehensive pack validations for all Azure Functions runtimes (#4625)
  - Enhanced user experience with real-time validation status (PASSED/FAILED/WARNING)
  - Runtime-specific validations for .NET, Python, Node.js, PowerShell, and Custom Handlers
  - Actionable error messages to help developers resolve issues during packaging
  - Validates folder structure, required files, programming models, and runtime-specific configurations 
