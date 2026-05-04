// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using System.Text;
using Azure.Functions.Cli.Common;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.CommonTests
{
    public class ExecutableTests
    {
        [Fact]
        public async Task RunAsync_CapturesFullStdout_ForShortLivedProcess()
        {
            (string exe, string args) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ("cmd.exe", "/c echo hello-from-test")
                : ("/bin/sh", "-c \"echo hello-from-test\"");

            var sb = new StringBuilder();
            var executable = new Executable(exe, args);

            int exitCode = await executable.RunAsync(o => sb.AppendLine(o), e => sb.AppendLine(e));

            exitCode.Should().Be(0);
            sb.ToString().Should().Contain("hello-from-test");
        }
    }
}
