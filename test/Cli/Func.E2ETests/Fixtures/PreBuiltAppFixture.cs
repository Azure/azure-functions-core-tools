// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.TestFramework.Helpers;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Fixtures
{
    /// <summary>
    /// Base fixture that copies a pre-built test app from TestFunctionApps directory
    /// instead of running func init/new. This significantly speeds up test setup
    /// by avoiding the 5-10 second scaffolding overhead per test.
    /// </summary>
    public abstract class PreBuiltAppFixture : IAsyncLifetime
    {
        protected PreBuiltAppFixture(string testAppName, bool requiresBuild = false)
        {
            TestAppName = testAppName;
            RequiresBuild = requiresBuild;

            Log = new Mock<ITestOutputHelper>().Object;

            FuncPath = Environment.GetEnvironmentVariable(Constants.FuncPath) ?? string.Empty;

            if (string.IsNullOrEmpty(FuncPath))
            {
                // Fallback for local testing in Visual Studio, etc.
                FuncPath = Path.Combine(Environment.CurrentDirectory, "func");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    FuncPath += ".exe";
                }

                if (!File.Exists(FuncPath))
                {
                    throw new ApplicationException("Could not locate the 'func' executable to use for testing. Make sure the FUNC_PATH environment variable is set to the full path of the func executable.");
                }
            }

            // Get the source test app directory
            string testProjectPath = Environment.GetEnvironmentVariable(Constants.TestProjectPath)
                ?? Path.GetFullPath(Path.Combine("..", "..", "..", "..", "test", "TestFunctionApps"));

            SourceAppPath = Path.Combine(testProjectPath, TestAppName);

            if (!Directory.Exists(SourceAppPath))
            {
                throw new ApplicationException($"Test app not found at '{SourceAppPath}'. Make sure the TestFunctionApps directory contains '{TestAppName}'.");
            }

            Directory.CreateDirectory(WorkingDirectory);
        }

        public ITestOutputHelper Log { get; set; }

        public string FuncPath { get; set; }

        public string WorkingDirectory { get; set; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        public string SourceAppPath { get; set; }

        public string TestAppName { get; set; }

        public bool RequiresBuild { get; set; }

        public bool CleanupWorkingDirectory { get; set; } = true;

        public Task DisposeAsync()
        {
            if (CleanupWorkingDirectory)
            {
                try
                {
                    Directory.Delete(WorkingDirectory, true);
                }
                catch
                {
                    // Ignore any errors when cleaning up
                }
            }

            return Task.CompletedTask;
        }

        public virtual Task InitializeAsync()
        {
            // Set up custom hive for dotnet templates (avoid polluting global state)
            var hiveRoot = Path.Combine(Path.GetTempPath(), "func-e2e-hives");
            Environment.SetEnvironmentVariable(DotnetHelpers.CustomHiveFlag, "1");
            Environment.SetEnvironmentVariable(DotnetHelpers.CustomHiveRoot, hiveRoot);
            Directory.CreateDirectory(hiveRoot);

            // Verify source app path exists before copying
            if (!Directory.Exists(SourceAppPath))
            {
                throw new DirectoryNotFoundException($"Source test app not found at '{SourceAppPath}'");
            }

            // Copy the pre-built app to the working directory
            CopyDirectoryHelpers.CopyDirectory(SourceAppPath, WorkingDirectory);

            // Verify copy was successful
            if (!File.Exists(Path.Combine(WorkingDirectory, "host.json")))
            {
                throw new InvalidOperationException($"Failed to copy test app - host.json not found in '{WorkingDirectory}'");
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Fixture for .NET 8 isolated function app.
    /// Uses TestDotnet8IsolatedProject from TestFunctionApps.
    /// </summary>
    public class PreBuiltDotnetIsolatedFixture : PreBuiltAppFixture
    {
        public PreBuiltDotnetIsolatedFixture()
            : base("TestDotnet8IsolatedProject", requiresBuild: true)
        {
        }
    }

    /// <summary>
    /// Fixture for .NET 6 in-process function app.
    /// Uses TestNet6InProcProject from TestFunctionApps.
    /// </summary>
    public class PreBuiltDotnet6InProcFixture : PreBuiltAppFixture
    {
        public PreBuiltDotnet6InProcFixture()
            : base("TestNet6InProcProject", requiresBuild: true)
        {
        }
    }

    /// <summary>
    /// Fixture for .NET 8 in-process function app.
    /// Uses TestNet8InProcProject from TestFunctionApps.
    /// </summary>
    public class PreBuiltDotnet8InProcFixture : PreBuiltAppFixture
    {
        public PreBuiltDotnet8InProcFixture()
            : base("TestNet8InProcProject", requiresBuild: true)
        {
        }
    }

    /// <summary>
    /// Fixture for Node.js v4 function app.
    /// Uses TestNodeProject from TestFunctionApps.
    /// </summary>
    public class PreBuiltNodeFixture : PreBuiltAppFixture
    {
        public PreBuiltNodeFixture()
            : base("TestNodeProject", requiresBuild: false)
        {
        }
    }

    /// <summary>
    /// Fixture for Python function app.
    /// Uses TestPythonProject from TestFunctionApps.
    /// </summary>
    public class PreBuiltPythonFixture : PreBuiltAppFixture
    {
        public PreBuiltPythonFixture()
            : base("TestPythonProject", requiresBuild: false)
        {
        }
    }

    /// <summary>
    /// Fixture for PowerShell function app.
    /// Uses TestPowershellProject from TestFunctionApps.
    /// </summary>
    public class PreBuiltPowershellFixture : PreBuiltAppFixture
    {
        public PreBuiltPowershellFixture()
            : base("TestPowershellProject", requiresBuild: false)
        {
        }
    }

    /// <summary>
    /// Fixture for Custom Handler function app.
    /// Uses TestCustomHandlerProject from TestFunctionApps.
    /// </summary>
    public class PreBuiltCustomHandlerFixture : PreBuiltAppFixture
    {
        public PreBuiltCustomHandlerFixture()
            : base("TestCustomHandlerProject", requiresBuild: false)
        {
        }
    }
}
