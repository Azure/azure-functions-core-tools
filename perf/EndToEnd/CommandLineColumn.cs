// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Azure.Functions.Cli.Performance.EndToEnd;

internal sealed class CommandLineColumn : IColumn
{
    public string Id => nameof(CommandLineColumn);

    public string ColumnName => "Invocation";

    public bool AlwaysShow => true;

    public ColumnCategory Category => ColumnCategory.Params;

    public int PriorityInCategory => 1;

    public bool IsNumeric => false;

    public UnitType UnitType => UnitType.Dimensionless;

    public string Legend => "CLI invocation, with arguments quoted where needed";

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        string name = benchmarkCase.Parameters[nameof(CommandBenchmarks.Command)] as string
            ?? throw new InvalidOperationException("The benchmark command parameter is missing.");
        return CommandCase.All.Single(command => command.Name == name).DisplayCommand;
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public bool IsAvailable(Summary summary) => true;
}
