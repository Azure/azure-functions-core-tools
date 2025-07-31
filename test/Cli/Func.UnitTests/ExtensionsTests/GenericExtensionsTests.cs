// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Extensions;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ExtensionsTests
{
    public class GenericExtensionsTests
    {
        public enum Test
        {
            North,
            South
        }

        [Fact]
        public void MergeWithTest()
        {
            var source = new Source { Name = "Original", Value = 10, Timestamp = DateTime.UtcNow, Direction = Test.South, From = Test.South };
            var target = new Target();

            target = target.MergeWith(source, t => t);

            target.Name.Should().Be(source.Name);
            target.Value.Should().Be(source.Value);
            target.Timestamp.Should().Be(source.Timestamp);
            target.Direction.Should().Be(source.Direction);
            target.From.Should().Be(source.From);
        }

        public class Source
        {
            public string Name { get; set; }

            public int Value { get; set; }

            public DateTime Timestamp { get; set; }

            public Test Direction { get; set; }

            public Test From { get; set; }
        }

        public class Target
        {
            public string Name { get; set; }

            public int Value { get; set; }

            public DateTime? Timestamp { get; set; }

            public Test? Direction { get; set; }

            public Test From { get; set; }
        }
    }
}
