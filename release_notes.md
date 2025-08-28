# Azure Functions CLI 4.3.0

# Azure Functions CLI 4.2.2
# Azure Functions CLI 4.3.0-preview1

This release provides a preview build of Core Tools targeting the Linux-ARM64 architecture.

> [!Note]
> Unless you're targeting Linux-ARM64, we recommend continuing to use the standard release for broader compatibility.

- This build is intended to enable support for the Linux-ARM64 platform.
- It does not include .NET applications using the in-process model at this time.
- This release will only be available on NPM and APT

#### Host Version

- Host Version: 4.1041.200
- In-Proc Host Version: 4.41.100 (4.841.100, 4.641.100)

#### Changes

- Log the resolved worker runtime and `local.settings.json` location, if found.
- Add `func pack` basic functionality (#4600) 
- Clean up HelpAction and add `func pack` to help menu (#4639)
- Add comprehensive pack validations for all Azure Functions runtimes (#4625)
  - Enhanced user experience with real-time validation status (PASSED/FAILED/WARNING)
  - Runtime-specific validations for .NET, Python, Node.js, PowerShell, and Custom Handlers
  - Actionable error messages to help developers resolve issues during packaging
  - Validates folder structure, required files, programming models, and runtime-specific configurations 

- Linux ARM64 support
