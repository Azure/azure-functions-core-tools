# Azure Functions CLI 4.7.0

#### Host Version

- Host Runtime Version: 4.1045.200
- In-Proc CLI:
  - CLI Version: 4.3.0
  - Host Runtime Version: 4.44.100 (includes 4.844.100, 4.644.100)

#### Changes

- Added end-of-life warnings for all runtime versions during `func azure functionapp publish`. (#4760)
- Reduced console output noise by moving informational messages to verbose logging. (#4768)
- Fixed an issue where creating an MCP Tool trigger function would fail with "Unknown template 'McpToolTrigger'" error. (#4768)