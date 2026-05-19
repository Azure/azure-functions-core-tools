// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    // Tests that exercise WorkerRuntimeLanguageHelper.ResolveNativeWorkerRuntime mutate
    // process-global state (FUNCTIONS_WORKER_RUNTIME, FUNCTIONS_CLI_GO_PREVIEW env vars,
    // FileSystemHelpers.Override, GlobalCoreToolsSettings.CurrentWorkerRuntime). Disable
    // cross-class parallelization so they don't race against each other.
    [CollectionDefinition("NativeWorkerRuntimeTests", DisableParallelization = true)]
    public class NativeWorkerRuntimeTestCollection
    {
    }
}
