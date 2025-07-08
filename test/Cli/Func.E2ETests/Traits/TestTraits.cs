// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.E2ETests.Traits
{
    internal static class TestTraits
    {
        /// <summary>
        /// Defines a group of tests to be run together. Useful for test isolation.
        /// </summary>
        public const string Group = "Group";

        /// <summary>
        /// Tests with RequiresNestedInProcArtifacts label will not be run in the default scenario and only in the artifact consolidation pipeline
        /// Otherwise tests with this label will fail in the PR/ official core tools pipelines since the nested inproc artifacts are not present.
        /// </summary>
        public const string RequiresNestedInProcArtifacts = "RequiresNestedInProcArtifacts";

        /// <summary>
        /// Tests with UseInConsolidatedArtifactGeneration label will be used in the default scenario and in the artifact consolidation pipeline
        /// We still want to run these tests in the PR/ official core tools pipelines and in the artifact consolidation pipeline for a sanity check before publishing the artifacts.
        /// </summary>
        public const string UseInConsolidatedArtifactGeneration = "UseInConsolidatedArtifactGeneration";

        /// <summary>
        /// Tests with UseInVisualStudioConsolidatedArtifactGeneration label will not be run in the default scenario and only in the artifact consolidation pipeline
        /// Otherwise tests with this label will fail in the PR/ official core tools pipelines since the nested inproc artifacts are not present.
        /// </summary>
        public const string UseInVisualStudioConsolidatedArtifactGeneration = "UseInVisualStudioConsolidatedArtifactGeneration";

        /// <summary>
        /// Tests with InProc label are used to distinguish dotnet isolated tests from dotnet inproc tests that are not involved in the artifact consolidation pipeline and do not require nested inproc artifacts.
        /// This is done since when the dotnet isolated tests are run with dotnet inproc, we run into templating conflict errors.
        /// </summary>
        public const string InProc = "InProc";

        public const string WorkerRuntime = "WorkerRuntime";
        public const string Dotnet = "Dotnet";
        public const string DotnetIsolated = "DotnetIsolated";
        public const string Node = "Node";
        public const string Powershell = "Powershell";
    }
}
