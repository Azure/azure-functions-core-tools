using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Tests.E2E.Helpers
{
    internal static class TestingTraits
    {
        internal class TestCategory
        {
            // Tests with RequiresNestedInProcArtifacts label will not be run in the default scenario and only in the artifact consolidation pipeline
            // These tests will break if run in the PR pipeline since they don't have the required nested inproc folders
            public const string RequiresNestedInProcArtifacts = "RequiresNestedInProcArtifacts";

            // Tests with UseInConsolidatedArtifactGeneration label will be used in the default scenario and in the artifact consolidation pipeline
            // We still want the PR pipelines to run these tests AND we want the consolidated artifact to check that this scenario still works as a sanity check
            public const string UseInConsolidatedArtifactGeneration = "UseInConsolidatedArtifactGeneration";
        }

        internal class TraitName
        {
            public const string Category = "Category";
        }
    }
}
