// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Performance.EndToEnd;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using System.Text.Json;

if (args is ["--list-commands"])
{
    Console.WriteLine(JsonSerializer.Serialize(CommandCase.All.Select(command => command.Name)));
    return 0;
}

ManualConfig config = ManualConfig.Create(DefaultConfig.Instance)
    .AddColumn(new CommandLineColumn())
    .HideColumns(TargetMethodColumn.Method);
Summary[] summaries = [.. BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config)];
return summaries.Length == 0 || summaries.Any(summary =>
    summary.HasCriticalValidationErrors ||
    summary.Reports.Any(report => !report.Success)) ? 1 : 0;
