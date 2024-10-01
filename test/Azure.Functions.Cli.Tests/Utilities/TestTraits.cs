namespace Azure.Functions.Cli.Tests
{
    internal class TestTraits
    {
        /// <summary>
        /// Classifies a category of tests. A category may have multiple groups.
        /// </summary>
        public const string Category = "Category";

        /// <summary>
        /// Defines a group of tests to be run together. Useful for test isolation.
        /// </summary>
        public const string Group = "Group";

        // Categories
        public const string EndToEnd = "EndToEnd";
        public const string UnitTest = "UnitTest";

        // Groups
        public const string ActionTests = "ActionTests";
        public const string ExtensionTests = "ExtensionTests";
        public const string DurableTests = "DurableTests";
        public const string FuncCreateTests = "FuncCreateTests";
        public const string FuncDeployTests = "FuncDeployTests";
        public const string FuncInitTests = "FuncInitTests";
        public const string FuncListTests = "FuncListTests";
        public const string FuncPackTests = "FuncPackTests";
        public const string FuncSettingsTests = "FuncSettingsTests";
        public const string FuncStartTests = "FuncStartTests";

    }
}
