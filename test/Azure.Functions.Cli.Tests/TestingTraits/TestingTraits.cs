using System;
using Xunit.Sdk;

namespace Azure.Functions.Cli.Tests.TestingTraits
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    [TraitDiscoverer("Namespace.TestingTraitDiscoverer", "TestingTrait")]
    public class TestingTraitAttribute : Attribute, ITraitAttribute
    {
        public string Name { get; }
        public string Value { get; }

        public TestingTraitAttribute(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }
}
