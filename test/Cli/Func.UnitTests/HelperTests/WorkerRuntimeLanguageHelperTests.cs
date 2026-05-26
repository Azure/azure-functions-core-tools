// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class WorkerRuntimeLanguageHelperTests
    {
        [Fact]
        public void NormalizeLanguage_Golang_ThrowsArgumentException()
        {
            // "golang" is not a valid language string; only "go" is accepted.
            // This test ensures the resolver doesn't silently accept "golang"
            // and map it to the wrong runtime.
            Assert.Throws<ArgumentException>(() =>
                WorkerRuntimeLanguageHelper.NormalizeLanguage("golang"));
        }
    }
}
