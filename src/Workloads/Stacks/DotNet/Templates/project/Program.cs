#if (!NoTelemetry)
using Azure.Monitor.OpenTelemetry.Exporter;
#endif
using Microsoft.Azure.Functions.Worker.Builder;
#if (!NoTelemetry)
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
#endif
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

#if (!NoHttp)
builder.ConfigureFunctionsWebApplication();

#endif
#if (!NoTelemetry)
var telemetry = builder.Services.AddOpenTelemetry()
    .UseFunctionsWorkerDefaults();

if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    telemetry.UseAzureMonitorExporter();
}

#endif
await builder.Build().RunAsync();
