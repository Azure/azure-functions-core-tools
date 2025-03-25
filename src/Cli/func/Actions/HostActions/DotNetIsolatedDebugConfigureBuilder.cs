using System;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Diagnostics
{
    internal class DotNetIsolatedDebugConfigureBuilder : IConfigureBuilder<IServiceCollection>
    {
        public void Configure(IServiceCollection services)
        {
            services.PostConfigure<LanguageWorkerOptions>(o =>
            {
                foreach (var workerConfig in o.WorkerConfigs)
                {
                    // We do not want to timeout while debugging.
                    workerConfig.CountOptions.ProcessStartupTimeout = TimeSpan.FromDays(30);
                    workerConfig.CountOptions.InitializationTimeout = TimeSpan.FromDays(30);
                }
            });
        }
    }
}