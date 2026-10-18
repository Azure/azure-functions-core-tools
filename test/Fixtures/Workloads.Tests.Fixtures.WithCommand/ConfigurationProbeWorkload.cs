// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand;

/// <summary>
/// Exercises supported DI construction before invocation. Public for workload activation across the load context.
/// </summary>
public sealed class ConfigurationProbeWorkload : Workload
{
    public override string DisplayName => "Configuration probe";

    public override string Description => "Reports constructor-captured configuration through a distinct test-only exit code.";

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<ProbeOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            options.Value = configuration["review:value"];
            if (configuration["review:configureMarker"] is { Length: > 0 } marker)
            {
                File.AppendAllText(marker, options.Value + "\n");
            }
        });
        builder.Services.AddOptions<ProbeOptions>("named").Configure<IConfiguration>((options, configuration) => options.Value = configuration["review:named"]);
        builder.RegisterCommand<ProbeCommand>();
    }

    private sealed class ProbeOptions
    {
        public ProbeOptions()
        {
        }

        public string? Value { get; set; }
    }

    private sealed class ProbeCommand(IOptions<ProbeOptions> options, IOptionsMonitor<ProbeOptions> monitor, IConfiguration configuration) : FuncCommand
    {
        private readonly string? _captured = (options ?? throw new ArgumentNullException(nameof(options))).Value.Value;
        private readonly string? _named = (monitor ?? throw new ArgumentNullException(nameof(monitor))).Get("named").Value;
        private readonly string? _direct = (configuration ?? throw new ArgumentNullException(nameof(configuration)))["review:direct"];
        private readonly FuncCommandArgument<string> _target = new("target", "A literal value used to distinguish command ownership.", isRequired: true);
        private readonly FuncCommandOption<string> _value = new("--value", "-v", "Another literal value.");

        public override string Name => "configuration-probe";

        public override string Description => "Verifies effective configuration was loaded before DI command construction.";

        public override IReadOnlyList<FuncCommandArgument> Arguments => [_target];

        public override IReadOnlyList<FuncCommandOption> Options => [_value];

        public override Task<int> ExecuteAsync(FuncCommandInvocationContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            bool values = context.GetValue(_target) is "new" or "init" or "ordinary" or "workload"
                && context.GetValue(_value) is "new" or "init" or "ordinary" or "workload";
            // A distinct nonzero witness proves the handler ran rather than first-run/help returning success.
            return Task.FromResult(values && _captured == "project" && _named == "project" && _direct == "project" ? 73 : 43);
        }
    }
}
