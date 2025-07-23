// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.UnitTests.Helpers;

internal class TestProcessManager : IProcessManager
{
    public bool KillChildProcessesCalled { get; private set; }

    public bool KillMainProcessCalled { get; private set; }

    public void KillChildProcesses() => KillChildProcessesCalled = true;

    public void KillMainProcess() => KillMainProcessCalled = true;

    // Other interface members omitted for this test
    public IEnumerable<IProcessInfo> GetProcessesByName(string processName) => null;

    public IProcessInfo GetCurrentProcess() => null;

    public IProcessInfo GetProcessById(int processId) => null;

    public bool RegisterChildProcess(System.Diagnostics.Process childProcess) => false;
}
