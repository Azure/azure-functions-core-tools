// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workload.Sdk;
using Azure.Functions.Cli.Workloads;

// Reference implementation of an out-of-process workload. Demonstrates the
// minimum required to participate in the protocol: handle initialize, then
// implement whichever capability methods this workload claims to support.
//
// Not AOT-published in this project (see csproj note) but written so it can be:
// no reflection, no dynamic loading, source-generated JSON via WorkloadJsonContext.

return await WorkloadServer.RunAsync(builder => builder
    .OnInitialize((p, _) => Task.FromResult(new InitializeResult(
        WorkloadId: "sample",
        WorkloadVersion: "1.0.0",
        ProtocolVersion: WorkloadProtocol.Version,
        Capabilities: [
            WorkloadProtocol.Capabilities.ProjectDetect,
            WorkloadProtocol.Capabilities.ProjectInit,
            WorkloadProtocol.Capabilities.Templates,
            WorkloadProtocol.Capabilities.Pack
        ],
        SupportedRuntimes: ["sample"])))
    .OnProjectDetect((p, _) =>
    {
        var hasMarker = Directory.EnumerateFiles(p.Directory, "*.sampleproj", SearchOption.TopDirectoryOnly).Any();
        return Task.FromResult(new ProjectDetectResult(
            Matched: hasMarker,
            Runtime: hasMarker ? "sample" : null,
            Language: hasMarker ? "Demo" : null,
            Confidence: hasMarker ? 1.0 : 0.0));
    })
    .OnProjectInit(async (p, ct) =>
    {
        Directory.CreateDirectory(p.ProjectPath);
        var hostJsonPath = Path.Combine(p.ProjectPath, "host.json");
        var projPath = Path.Combine(p.ProjectPath, $"{p.ProjectName ?? "App"}.sampleproj");

        if (!p.Force && (File.Exists(hostJsonPath) || File.Exists(projPath)))
        {
            throw new WorkloadUserException(
                $"Project files already exist in '{p.ProjectPath}'. Use --force to overwrite.");
        }

        await File.WriteAllTextAsync(hostJsonPath, "{ \"version\": \"2.0\" }\n", ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(projPath, "<SampleProject />\n", ct).ConfigureAwait(false);
        return new ProjectInitResult([hostJsonPath, projPath]);
    })
    .OnTemplatesList((_, _) => Task.FromResult(new TemplatesListResult([
        new FunctionTemplateInfo("HttpTrigger", "HTTP-triggered function", "sample", "Demo"),
        new FunctionTemplateInfo("TimerTrigger", "Timer-triggered function", "sample", "Demo"),
    ])))
    .OnTemplatesCreate(async (p, ct) =>
    {
        Directory.CreateDirectory(p.OutputPath);
        var path = Path.Combine(p.OutputPath, $"{p.FunctionName}.sample.json");
        if (File.Exists(path) && !p.Force)
        {
            throw new WorkloadUserException(
                $"Function '{p.FunctionName}' already exists in '{p.OutputPath}'. Use --force to overwrite.");
        }

        await File.WriteAllTextAsync(
            path,
            $"{{ \"name\": \"{p.FunctionName}\", \"template\": \"{p.TemplateName}\" }}\n",
            ct).ConfigureAwait(false);
        return new TemplatesCreateResult([path]);
    })
    .OnPackRun((p, _) =>
    {
        var output = p.OutputPath ?? Path.Combine(p.ProjectPath, "bin", "sample.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, "stub-sample-package");
        return Task.FromResult(new PackRunResult(output));
    }));
