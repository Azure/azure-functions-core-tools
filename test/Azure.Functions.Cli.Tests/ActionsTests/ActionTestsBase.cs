using System;
using System.IO;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Threading;
using Azure.Functions.Cli.Common;
using Colors.Net;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.ActionsTests
{
    public abstract class ActionTestsBase : IDisposable
    {
        protected ITestOutputHelper Output { get; private set; }

        protected string WorkingDirectory { get; private set; }

        private readonly IConsoleWriter _out;
        private readonly IConsoleWriter _error;

        protected ActionTestsBase(ITestOutputHelper output)
        {
            Output = output;
            _out = ColoredConsole.Out;
            _error = ColoredConsole.Error;
            CleanUp();
        }

        public void Dispose()
        {
            CleanUp(WorkingDirectory);
        }

        protected void CleanUp([CallerMemberName]string name = null)
        {
            WorkingDirectory = Path.GetTempFileName();
            InternalCleanUp(WorkingDirectory);
            Directory.CreateDirectory(WorkingDirectory);
            Environment.CurrentDirectory = WorkingDirectory;
            FileSystemHelpers.Instance = new FileSystem();
            ColoredConsole.Out = _out;
            ColoredConsole.Error = _error;
        }

        private void InternalCleanUp(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}