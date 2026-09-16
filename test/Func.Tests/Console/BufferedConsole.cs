// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Tests.Console;

internal sealed class BufferedConsole : IAnsiConsole, IAnsiConsoleInput, IDisposable
{
    private readonly StringWriter _writer = new();
    private readonly IAnsiConsole _console;
    private readonly Queue<ConsoleKeyInfo> _keys = new();

    public BufferedConsole(bool interactive, bool ansi, bool noColor = false)
    {
        _console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(_writer),
            Interactive = interactive ? InteractionSupport.Yes : InteractionSupport.No,
            Ansi = ansi ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = noColor ? ColorSystemSupport.NoColors : ColorSystemSupport.Standard,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
        });
        Profile.Width = 100;
        Profile.Height = 30;
    }

    public Profile Profile => _console.Profile;
    public IAnsiConsoleCursor Cursor => _console.Cursor;
    public IAnsiConsoleInput Input => this;
    public IExclusivityMode ExclusivityMode => _console.ExclusivityMode;
    public RenderPipeline Pipeline => _console.Pipeline;
    public string Output => _writer.ToString();
    public int Reads { get; private set; }

    public void Enqueue(ConsoleKey key, char character = '\0', bool control = false)
        => _keys.Enqueue(new ConsoleKeyInfo(character, key, false, false, control));

    public bool IsKeyAvailable() => _keys.Count > 0;

    public ConsoleKeyInfo? ReadKey(bool intercept)
    {
        Reads++;
        if (!Profile.Capabilities.Interactive)
        {
            throw new InvalidOperationException("Attempted input on a non-interactive test console.");
        }

        if (!_keys.TryDequeue(out ConsoleKeyInfo key))
        {
            throw new InvalidOperationException("Prompt exhausted its scripted input.");
        }

        if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            throw new OperationCanceledException();
        }

        return key;
    }

    public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadKey(intercept));
    }

    public void Clear(bool home) => _console.Clear(home);
    public void Write(IRenderable renderable) => _console.Write(renderable);
    public void Dispose() => _writer.Dispose();
}