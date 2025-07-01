// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Copied from: https://github.com/dotnet/sdk/blob/4a81a96a9f1bd661592975c8269e078f6e3f18c9/src/Cli/Microsoft.DotNet.Cli.Utils/Reporter.cs
namespace Azure.Functions.Cli.Abstractions
{
    // Simple console manager
    public class Reporter : IReporter
    {
        // cannot use auto properties, as those are static
#pragma warning disable IDE0032 // Use auto property
        private static readonly Reporter _consoleOutReporter = new(AnsiConsole.GetOutput());
        private static readonly Reporter _consoleErrReporter = new(AnsiConsole.GetError());
#pragma warning restore IDE0032 // Use auto property

        private static SpinLock _spinlock = default(SpinLock);
        private static IReporter _errorReporter = _consoleErrReporter;
        private static IReporter _outputReporter = _consoleOutReporter;
        private static IReporter _verboseReporter = _consoleOutReporter;

        private readonly AnsiConsole? _console;

        static Reporter()
        {
            Reset();
        }

        private Reporter(AnsiConsole? console)
        {
            _console = console;
        }

        public static Reporter NullReporter { get; } = new(console: null);

        public static Reporter ConsoleOutReporter => _consoleOutReporter;

        public static Reporter ConsoleErrReporter => _consoleErrReporter;

        public static IReporter Output { get; private set; } = NullReporter;

        public static IReporter Error { get; private set; } = NullReporter;

        public static IReporter Verbose { get; private set; } = NullReporter;

        /// <summary>
        /// Resets the reporters to write to the current reporters based on <see cref="CommandLoggingContext"/> settings.
        /// </summary>
        public static void Reset()
        {
            UseSpinLock(() =>
            {
                ResetOutput();
                ResetError();
                ResetVerbose();
            });
        }

        /// <summary>
        /// Sets the output reporter to <paramref name="reporter"/>.
        /// The reporter won't be applied if disabled in <see cref="CommandLoggingContext"/>.
        /// </summary>
        /// <param name="reporter"> IReporter instance. </param>
        public static void SetOutput(IReporter reporter)
        {
            UseSpinLock(() =>
            {
                _outputReporter = reporter;
                ResetOutput();
            });
        }

        /// <summary>
        /// Sets the error reporter to <paramref name="reporter"/>.
        /// The reporter won't be applied if disabled in <see cref="CommandLoggingContext"/>.
        /// </summary>
        public static void SetError(IReporter reporter)
        {
            UseSpinLock(() =>
            {
                _errorReporter = reporter;
                ResetError();
            });
        }

        /// <summary>
        /// Sets the verbose reporter to <paramref name="reporter"/>.
        /// The reporter won't be applied if disabled in <see cref="CommandLoggingContext"/>.
        /// </summary>
        public static void SetVerbose(IReporter reporter)
        {
            UseSpinLock(() =>
            {
                _verboseReporter = reporter;
                ResetVerbose();
            });
        }

        private static void ResetOutput()
        {
            Output = CommandLoggingContext.OutputEnabled ? _outputReporter : NullReporter;
        }

        private static void ResetError()
        {
            Error = CommandLoggingContext.ErrorEnabled ? _errorReporter : NullReporter;
        }

        private static void ResetVerbose()
        {
            Verbose = CommandLoggingContext.IsVerbose ? _verboseReporter : NullReporter;
        }

        public void WriteLine(string message)
        {
            UseSpinLock(() =>
            {
                if (CommandLoggingContext.ShouldPassAnsiCodesThrough)
                {
                    _console?.Writer?.WriteLine(message);
                }
                else
                {
                    _console?.WriteLine(message);
                }
            });
        }

        public void WriteLine()
        {
            UseSpinLock(() => _console?.Writer?.WriteLine());
        }

        public void Write(string message)
        {
            UseSpinLock(() =>
            {
                if (CommandLoggingContext.ShouldPassAnsiCodesThrough)
                {
                    _console?.Writer?.Write(message);
                }
                else
                {
                    _console?.Write(message);
                }
            });
        }

        public void WriteLine(string format, params object?[] args) => WriteLine(string.Format(format, args));

        public static void UseSpinLock(Action action)
        {
            bool lockTaken = false;
            try
            {
                _spinlock.Enter(ref lockTaken);
                action();
            }
            finally
            {
                if (lockTaken)
                {
                    _spinlock.Exit(false);
                }
            }
        }
    }
}
