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
            public const string RequiresNestedInProcArtifacts = "RequiresNestedInProcArtifacts";

            // Tests with UseInConsolidatedArtifactGeneration label will be used in the default scenario and in the artifact consolidation pipeline
            public const string UseInConsolidatedArtifactGeneration = "UseInConsolidatedArtifactGeneration";
        }

        internal class TraitName
        {
            public const string Category = "Category";
        }
    }
}
