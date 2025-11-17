# Azure Functions CLI 4.5.0

#### Host Version

- Host Version: 4.1044.400
- In-Proc Host Version: 4.44.100 (4.844.100, 4.644.100)

#### Changes

- Add updated Durable .NET templates (#4692)
- Adding the MCP Tool Trigger Templates for the Node/Typescript (#4651)
- Set `AzureWebJobsStorage` to use the storage emulator by default on all platforms (#4685)
- Set `FUNCTIONS_WORKER_RUNTIME` to custom if the  `EnableMcpCustomHandlerPreview` feature flag is set (#4703)
- Enhanced dotnet installation discovery by adopting the same `Muxer` logic used by the .NET SDK itself (#4732)