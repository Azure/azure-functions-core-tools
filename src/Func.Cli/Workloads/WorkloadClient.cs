// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Text.Json;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// JSON-RPC client over a single workload child process. Owns the process
/// lifetime and the stdio pipes; not thread-safe — one outstanding request
/// at a time. (The host serializes calls per workload; v1 has no cancellation
/// or notifications mid-request.)
/// </summary>
public interface IWorkloadClient : IAsyncDisposable
{
    public InitializeResult? InitializeResult { get; }
    public Task<InitializeResult> InitializeAsync(string cwd, CancellationToken cancellationToken = default);
    public Task<TResult> InvokeAsync<TParams, TResult>(string method, TParams parameters, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TParams> paramsTypeInfo, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> resultTypeInfo, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IWorkloadClient"/>. Spawns the workload executable and
/// communicates via JSON-RPC over its stdin/stdout. Stderr is forwarded to
/// the host's stderr so workload diagnostics surface naturally.
/// </summary>
public sealed class WorkloadClient : IWorkloadClient
{
    private readonly Process _process;
    private readonly Stream _stdin;
    private readonly Stream _stdout;
    private int _nextRequestId;
    private bool _disposed;

    public InitializeResult? InitializeResult { get; private set; }

    private WorkloadClient(Process process)
    {
        _process = process;
        _stdin = process.StandardInput.BaseStream;
        _stdout = process.StandardOutput.BaseStream;
    }

    /// <summary>
    /// Spawns the workload described by the given manifest. The working
    /// directory is set to the host's cwd; environment is inherited.
    /// </summary>
    public static WorkloadClient Spawn(WorkloadManifestFile manifest, string installDirectory)
    {
        // The manifest's `executable` may be:
        //   * an absolute path (used as-is),
        //   * a relative path / bare filename present in installDirectory
        //     (resolved against the install dir — e.g., a native AOT binary),
        //   * a bare command name like `dotnet` not present in installDirectory
        //     (resolved from PATH — useful while we ship workloads as .dll
        //     during development before NativeAOT publish is wired up).
        var inInstallDir = Path.Combine(installDirectory, manifest.Executable);
        string fileName;
        if (Path.IsPathRooted(manifest.Executable))
        {
            fileName = manifest.Executable;
        }
        else if (File.Exists(inInstallDir))
        {
            fileName = inInstallDir;
        }
        else
        {
            // Treat as command-on-PATH (e.g., `dotnet`).
            fileName = manifest.Executable;
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Directory.GetCurrentDirectory(),
        };

        foreach (var arg in manifest.ExecutableArgs)
        {
            // Resolve relative args against the install directory so the
            // workload payload (its .dll, scripts, assets) is locatable
            // regardless of host cwd.
            var resolved = Path.IsPathRooted(arg) || !File.Exists(Path.Combine(installDirectory, arg))
                ? arg
                : Path.Combine(installDirectory, arg);
            psi.ArgumentList.Add(resolved);
        }

        var process = Process.Start(psi)
            ?? throw new GracefulException($"Failed to start workload '{manifest.Id}'.", isUserError: false);

        // Forward stderr to host stderr so workload diagnostics surface.
        _ = Task.Run(async () =>
        {
            try
            {
                while (!process.HasExited)
                {
                    var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                    if (line is null) break;
                    System.Console.Error.WriteLine($"[workload:{manifest.Id}] {line}");
                }
            }
            catch
            {
                // Best effort.
            }
        });

        return new WorkloadClient(process);
    }

    public async Task<InitializeResult> InitializeAsync(string cwd, CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync(
            WorkloadProtocol.Methods.Initialize,
            new InitializeParams("vnext", WorkloadProtocol.Version, cwd),
            WorkloadJsonContext.Default.InitializeParams,
            WorkloadJsonContext.Default.InitializeResult,
            cancellationToken).ConfigureAwait(false);

        if (result.ProtocolVersion != WorkloadProtocol.Version)
        {
            throw new GracefulException(
                $"Workload '{result.WorkloadId}' speaks protocol {result.ProtocolVersion}, host expects {WorkloadProtocol.Version}.",
                isUserError: true);
        }

        InitializeResult = result;
        return result;
    }

    public async Task<TResult> InvokeAsync<TParams, TResult>(
        string method,
        TParams parameters,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TParams> paramsTypeInfo,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> resultTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = Interlocked.Increment(ref _nextRequestId);

        var paramsBytes = JsonSerializer.SerializeToUtf8Bytes(parameters, paramsTypeInfo);
        using var paramsDoc = JsonDocument.Parse(paramsBytes);

        var request = new JsonRpcRequest
        {
            Id = id,
            Method = method,
            Params = paramsDoc.RootElement.Clone(),
        };

        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, WorkloadJsonContext.Default.JsonRpcRequest);
        await FrameCodec.WriteFrameAsync(_stdin, requestBytes, cancellationToken).ConfigureAwait(false);

        var responseBytes = await FrameCodec.ReadFrameAsync(_stdout, cancellationToken).ConfigureAwait(false)
            ?? throw new GracefulException($"Workload exited unexpectedly while handling '{method}'.", isUserError: false);

        var response = JsonSerializer.Deserialize(responseBytes, WorkloadJsonContext.Default.JsonRpcResponse)
            ?? throw new GracefulException($"Workload returned an unparseable response for '{method}'.", isUserError: false);

        if (response.Error is not null)
        {
            var isUser = response.Error.Code == WorkloadProtocol.ErrorCodes.UserError
                || response.Error.Code == WorkloadProtocol.ErrorCodes.InvalidParams;
            throw new GracefulException(response.Error.Message, isUserError: isUser);
        }

        if (response.Result is null)
        {
            throw new GracefulException($"Workload returned an empty result for '{method}'.", isUserError: false);
        }

        return response.Result.Value.Deserialize(resultTypeInfo)
            ?? throw new GracefulException($"Workload result for '{method}' deserialized to null.", isUserError: false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Best-effort graceful shutdown: send shutdown then close stdin.
        try
        {
            var shutdown = new JsonRpcRequest { Id = -1, Method = WorkloadProtocol.Methods.Shutdown };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(shutdown, WorkloadJsonContext.Default.JsonRpcRequest);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await FrameCodec.WriteFrameAsync(_stdin, bytes, cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // ignore — we're shutting down
        }

        try { _stdin.Close(); } catch { /* ignore */ }

        try
        {
            if (!_process.WaitForExit(1000))
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            _process.Dispose();
        }
    }
}
