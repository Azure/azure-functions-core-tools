// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Extensions;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ExtensionsTests
{
    public class TaskExtensionsTests
    {
        [Fact]
        public async Task IgnoreFailureTest()
        {
            var task = Task.FromException(new Exception());
            await task.IgnoreFailure();
        }

        [Fact]
        public async Task IgnoreFailuteOfTTest()
        {
            var strTask = Task.FromException<string>(new Exception());
            var result = await strTask.IgnoreFailure();
            result.Should().BeNull();
        }

        [Fact]
        public async Task IgnoreFailureAndFilterListOfTasks()
        {
            var list = new List<Task<string>>
            {
                Task.FromResult("test"),
                Task.FromException<string>(new Exception())
            };

            var result = await list.IgnoreAndFilterFailures();

            result.Should()
                .ContainSingle()
                .And
                .Contain(new[] { "test" });
        }
    }
}
