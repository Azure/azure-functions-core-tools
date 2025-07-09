// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Extensions;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ExtensionsTests
{
    public class ProcessExtensionsTests
    {
        private volatile bool _calledContinueWith = false;

        [SkippableFact]
        public async Task WaitForExitTest()
        {
            Skip.IfNot(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                reason: "Unreliable on linux CI");

            Process process = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Process.Start("cmd")
                : Process.Start("sh");

            process.CreateWaitForExitTask().ContinueWith(_ =>
            {
                _calledContinueWith = true;
            }).Ignore();

            process.Kill();
            for (var i = 0; !_calledContinueWith && i < 10; i++)
            {
                await Task.Delay(200);
            }

            _calledContinueWith.Should().BeTrue(because: "the process should have exited and called the continuation");
        }
    }
}
