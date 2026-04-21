// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workload.Sdk;

/// <summary>
/// Stdio JSON-RPC server runtime used by workload executables. A workload's
/// <c>Main</c> method registers handlers via <see cref="WorkloadServerBuilder"/>
/// and then awaits <see cref="WorkloadServer.RunAsync"/>, which:
/// <list type="number">
///   <item>Reads framed JSON-RPC requests from <see cref="Console.OpenStandardInput()"/>.</item>
///   <item>Dispatches each to the registered handler.</item>
///   <item>Writes a framed JSON-RPC response to <see cref="Console.OpenStandardOutput()"/>.</item>
///   <item>Returns when stdin closes or a <c>shutdown</c> request is received.</item>
/// </list>
///
/// Workloads must NOT write anything except framed responses to stdout. All
/// human-readable diagnostics belong on stderr.
/// </summary>
public sealed class WorkloadServer
{
    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<object?>>> _handlers;

    internal WorkloadServer(Dictionary<string, Func<JsonElement?, CancellationToken, Task<object?>>> handlers)
    {
        _handlers = handlers;
    }

    /// <summary>Convenience entry point for workload <c>Main</c> methods.</summary>
    public static Task<int> RunAsync(Action<WorkloadServerBuilder> configure, CancellationToken cancellationToken = default)
    {
        var builder = new WorkloadServerBuilder();
        configure(builder);
        var server = builder.Build();
        return server.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput(), cancellationToken);
    }

    public async Task<int> RunAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await FrameCodec.ReadFrameAsync(input, cancellationToken).ConfigureAwait(false);
                if (frame is null)
                {
                    return 0; // peer closed cleanly
                }

                var (response, isShutdown) = await ProcessFrameAsync(frame, cancellationToken).ConfigureAwait(false);
                await WriteResponseAsync(output, response, cancellationToken).ConfigureAwait(false);

                if (isShutdown)
                {
                    return 0;
                }
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
    }

    private async Task<(JsonRpcResponse Response, bool IsShutdown)> ProcessFrameAsync(byte[] frame, CancellationToken cancellationToken)
    {
        JsonRpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(frame, WorkloadJsonContext.Default.JsonRpcRequest);
        }
        catch (JsonException ex)
        {
            return (ErrorResponse(0, WorkloadProtocol.ErrorCodes.ParseError, ex.Message), false);
        }

        if (request is null || request.JsonRpc != WorkloadProtocol.JsonRpcVersion || string.IsNullOrEmpty(request.Method))
        {
            return (ErrorResponse(request?.Id ?? 0, WorkloadProtocol.ErrorCodes.InvalidRequest, "Malformed JSON-RPC request."), false);
        }

        if (request.Method == WorkloadProtocol.Methods.Shutdown)
        {
            return (SuccessResponse(request.Id, null), true);
        }

        if (!_handlers.TryGetValue(request.Method, out var handler))
        {
            return (ErrorResponse(request.Id, WorkloadProtocol.ErrorCodes.MethodNotFound, $"Method not found: {request.Method}"), false);
        }

        try
        {
            var resultObj = await handler(request.Params, cancellationToken).ConfigureAwait(false);
            return (SuccessResponse(request.Id, resultObj), false);
        }
        catch (WorkloadUserException ex)
        {
            return (ErrorResponse(request.Id, WorkloadProtocol.ErrorCodes.UserError, ex.Message), false);
        }
        catch (Exception ex)
        {
            return (ErrorResponse(request.Id, WorkloadProtocol.ErrorCodes.InternalError, ex.Message), false);
        }
    }

    private static async Task WriteResponseAsync(Stream output, JsonRpcResponse response, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, WorkloadJsonContext.Default.JsonRpcResponse);
        await FrameCodec.WriteFrameAsync(output, bytes, cancellationToken).ConfigureAwait(false);
    }

    private static JsonRpcResponse SuccessResponse(int id, object? result)
    {
        JsonElement? element = null;
        if (result is not null)
        {
            // Re-serialize via runtime serialization. Workloads pass strongly-typed
            // result records; we round-trip them through the shared JSON context
            // when it has a converter, otherwise via untyped serialization.
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result, result.GetType(), WorkloadJsonContext.Default);
            using var doc = JsonDocument.Parse(bytes);
            element = doc.RootElement.Clone();
        }

        return new JsonRpcResponse { Id = id, Result = element };
    }

    private static JsonRpcResponse ErrorResponse(int id, int code, string message) =>
        new() { Id = id, Error = new JsonRpcError { Code = code, Message = message } };
}
