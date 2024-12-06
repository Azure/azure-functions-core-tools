using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Tests.E2E.Helpers
{
    internal static class TestTraits
    {
        /// <summary>
        /// Defines a group of tests to be run together. Useful for test isolation.
        /// </summary>
        public const string Group = "Group";

        // Groups
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
    }
}
