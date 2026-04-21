// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workload.Sdk;

/// <summary>
/// Fluent registration surface used by workload executables to declare which
/// JSON-RPC methods they handle.
/// </summary>
public sealed class WorkloadServerBuilder
{
    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<object?>>> _handlers = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a handler for an arbitrary method name. The params element is
    /// passed through raw so the handler can deserialize using the appropriate
    /// source-generated context.
    /// </summary>
    public WorkloadServerBuilder OnMethod(string method, Func<JsonElement?, CancellationToken, Task<object?>> handler)
    {
        _handlers[method] = handler;
        return this;
    }

    public WorkloadServerBuilder OnInitialize(Func<InitializeParams, CancellationToken, Task<InitializeResult>> handler) =>
        Bind(WorkloadProtocol.Methods.Initialize, WorkloadJsonContext.Default.InitializeParams, handler);

    public WorkloadServerBuilder OnProjectDetect(Func<ProjectDetectParams, CancellationToken, Task<ProjectDetectResult>> handler) =>
        Bind(WorkloadProtocol.Methods.ProjectDetect, WorkloadJsonContext.Default.ProjectDetectParams, handler);

    public WorkloadServerBuilder OnProjectInit(Func<ProjectInitParams, CancellationToken, Task<ProjectInitResult>> handler) =>
        Bind(WorkloadProtocol.Methods.ProjectInit, WorkloadJsonContext.Default.ProjectInitParams, handler);

    public WorkloadServerBuilder OnTemplatesList(Func<TemplatesListParams, CancellationToken, Task<TemplatesListResult>> handler) =>
        Bind(WorkloadProtocol.Methods.TemplatesList, WorkloadJsonContext.Default.TemplatesListParams, handler);

    public WorkloadServerBuilder OnTemplatesCreate(Func<TemplatesCreateParams, CancellationToken, Task<TemplatesCreateResult>> handler) =>
        Bind(WorkloadProtocol.Methods.TemplatesCreate, WorkloadJsonContext.Default.TemplatesCreateParams, handler);

    public WorkloadServerBuilder OnPackRun(Func<PackRunParams, CancellationToken, Task<PackRunResult>> handler) =>
        Bind(WorkloadProtocol.Methods.PackRun, WorkloadJsonContext.Default.PackRunParams, handler);

    public WorkloadServer Build() => new(_handlers);

    private WorkloadServerBuilder Bind<TParams, TResult>(
        string method,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TParams> paramsTypeInfo,
        Func<TParams, CancellationToken, Task<TResult>> handler)
        where TResult : class
    {
        _handlers[method] = async (paramsElement, ct) =>
        {
            if (paramsElement is null)
            {
                throw new WorkloadUserException($"Missing params for method '{method}'.");
            }

            var typed = paramsElement.Value.Deserialize(paramsTypeInfo)
                ?? throw new WorkloadUserException($"Invalid params for method '{method}'.");
            return await handler(typed, ct).ConfigureAwait(false);
        };
        return this;
    }
}
