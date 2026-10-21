// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using TestConsole = System.Console;

namespace Azure.Functions.Cli.Templates.Tests.StoreProcess;

/// <summary>
/// Runs only against a test-owned home supplied by the integration test.
/// </summary>
internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        string mode = args[0];
        string home = args[1];
        using CancellationTokenSource cancellation = new();
        CancellationToken token = cancellation.Token;
        FileTemplateStoreLock storeLock = new();
        TemplateStore store = new(new(home), new PhysicalFileSystem(), storeLock, NullLoggerFactory.Instance);
        TemplateEngineContext context = new(WorkingDirectory.FromExplicit(home));
        try
        {
            if (mode == "mutate")
            {
                using TemplateStoreWriteSession transaction = await store.OpenWriteAsync(context, token);
                await transaction.ExecuteAsync(async (engine, ct) =>
                {
                    await File.WriteAllTextAsync(Path.Combine(store.Hive, "interrupted.txt"), "uncommitted", ct);
                    TestConsole.WriteLine("MUTATING");
                    await TestConsole.In.ReadLineAsync(ct);
                    throw new IOException("The test mutation was interrupted.");
                }, (_, _) => Task.CompletedTask, token);
                return;
            }

            if (mode == "store-write")
            {
                using TemplateStoreWriteSession transaction = await store.OpenWriteAsync(context, token,
                    reason => TestConsole.WriteLine($"WAITING:{reason}"));
                await transaction.ExecuteAsync(async (_, ct) =>
                {
                    TestConsole.WriteLine("READY");
                    await TestConsole.In.ReadLineAsync(ct);
                }, (_, _) => Task.CompletedTask, token);
                return;
            }

            if (mode == "store-read")
            {
                using TemplateEngineSession engine = await store.OpenReadAsync(context, token,
                    waiting: reason => TestConsole.WriteLine($"WAITING:{reason}"));
                using Templater templater = new(engine, new(engine, new PhysicalFileSystem()));
                IReadOnlyList<TemplateCatalogEntry> entries = await templater.ListAsync(TemplateType.Item, token);
                TestConsole.WriteLine($"READY:{entries.Count}");
                await TestConsole.In.ReadLineAsync(token);
                return;
            }

            if (mode == "churn")
            {
                for (int iteration = 0; iteration < 20; iteration++)
                {
                    using (await storeLock.AcquireAsync(store.ControlDirectory, TemplateStoreAccess.Read, token,
                        reason => TestConsole.WriteLine($"WAITING:{reason}")))
                    {
                        TestConsole.WriteLine("READ");
                        await Task.Delay(10, token);
                    }
                }

                return;
            }

            if (mode == "cancel-churn")
            {
                TestConsole.WriteLine("READY");
                await TestConsole.In.ReadLineAsync(token);
                for (int iteration = 0; iteration < 128; iteration++)
                {
                    using var turn = CancellationTokenSource.CreateLinkedTokenSource(token);
                    try
                    {
                        using IDisposable unexpected = await storeLock.AcquireAsync(store.ControlDirectory,
                            TemplateStoreAccess.Write, turn.Token, _ => turn.Cancel());
                        throw new InvalidOperationException("The incumbent reader must prevent writer admission.");
                    }
                    catch (OperationCanceledException) when (turn.IsCancellationRequested && !token.IsCancellationRequested)
                    {
                        TestConsole.WriteLine("CANCELED");
                    }
                }

                return;
            }

            if (mode == "cancelable-write")
            {
                Task<IDisposable> acquisition = storeLock.AcquireAsync(store.ControlDirectory, TemplateStoreAccess.Write,
                    token, reason => TestConsole.WriteLine($"WAITING:{reason}"));
                string? signal = await TestConsole.In.ReadLineAsync(token);
                if (signal != "cancel")
                {
                    throw new InvalidOperationException("The parent must explicitly cancel the queued writer.");
                }

                await cancellation.CancelAsync();
                using IDisposable unexpected = await acquisition;
                throw new InvalidOperationException("The incumbent reader must prevent writer admission.");
            }

            using IDisposable lease = await storeLock.AcquireAsync(store.ControlDirectory,
                mode == "write" ? TemplateStoreAccess.Write : TemplateStoreAccess.Read, token,
                reason => TestConsole.WriteLine($"WAITING:{reason}"));
            TestConsole.WriteLine("READY");
            await TestConsole.In.ReadLineAsync(token);
        }
        catch (OperationCanceledException)
        {
            TestConsole.WriteLine("CANCELED");
        }
    }
}
