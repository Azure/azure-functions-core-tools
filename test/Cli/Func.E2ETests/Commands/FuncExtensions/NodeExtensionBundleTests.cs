// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncExtensions
{
    public class NodeExtensionBundleTests(ITestOutputHelper log) : ExtensionBundleTestsBase(log)
    {
        private const string WorkerRuntime = "node";

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
        public async Task ExtensionsInstall_BundlesConfiguredByDefault_NoActionPerformed()
        {
            await ExtensionsInstallBundlesConfiguredByDefault(WorkerRuntime, "\"Azure Blob Storage trigger\"", nameof(ExtensionsInstall_BundlesConfiguredByDefault_NoActionPerformed));
        }

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
        public async Task ExtensionsInstall_NoBundleArg_WithExtVersion_InstallsSpecificExtVersion()
        {
            await ExtensionsInstallNoBundleArgWithExtVersion(WorkerRuntime, "\"Azure Blob Storage trigger\"", nameof(ExtensionsInstall_NoBundleArg_WithExtVersion_InstallsSpecificExtVersion));
        }

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
        public async Task ExtensionsInstall_NoBundleArg_WithNotValidTrigger_NoActionPerformed()
        {
            await ExtensionsInstallNoBundleArgWithNotValidTrigger(WorkerRuntime, "HttpTrigger", nameof(ExtensionsInstall_NoBundleArg_WithNotValidTrigger_NoActionPerformed));
        }

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
        public async Task GetExtensionBundlePath_ReturnsBundlePath()
        {
            await GetExtensionBundlePathReturnsBundlePath(WorkerRuntime, nameof(GetExtensionBundlePath_ReturnsBundlePath));
        }

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
        public async Task GetExtensionBundlePath_NoBundleArg_BundlesNotConfiigured()
        {
            await GetExtensionBundlePathNoBundleArgBundlesNotConfiigured(WorkerRuntime, nameof(GetExtensionBundlePath_NoBundleArg_BundlesNotConfiigured));
        }
    }
}
