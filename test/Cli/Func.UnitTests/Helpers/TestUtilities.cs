// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Azure.Functions.Cli.UnitTests
{
    public static class TestUtilities
    {
        public static IConfigurationRoot CreateSetupWithConfiguration(Dictionary<string, string> settings = null)
        {
            var builder = new ConfigurationBuilder();
            if (settings != null)
            {
                builder.AddInMemoryCollection(settings);
            }

            return builder.Build();
        }

        public static async Task<bool> WaitForConditionAsync(Func<bool> condition, TimeSpan timeout, int pollIntervalMs = 50)
        {
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(pollIntervalMs);
            }

            return condition();
        }

        /// <summary>
        /// Stubs all common write paths for a file ending with <paramref name="fileSuffix"/>.
        /// Returns a capture you can use to read back what was written.
        /// </summary>
        public static WriteCapture SetupWriteFor(IFileSystem fs, string fileSuffix)
        {
            var cap = new WriteCapture();

            // Any Open that includes Write access => new stream
            fs.File.Open(
                Arg.Is<string>(p => p.EndsWith(fileSuffix, StringComparison.Ordinal)),
                Arg.Any<FileMode>(),
                Arg.Is<FileAccess>(fa => (fa & FileAccess.Write) != 0),
                Arg.Any<FileShare>())
              .Returns(_ => cap.New());

            // File.Create(path) => new stream
            fs.File.Create(
                Arg.Is<string>(p => p.EndsWith(fileSuffix, StringComparison.Ordinal)))
              .Returns(_ => cap.New());

            // OpenWrite(path) => new stream
            fs.File.OpenWrite(
                Arg.Is<string>(p => p.EndsWith(fileSuffix, StringComparison.Ordinal)))
              .Returns(_ => cap.New());

            // Some implementations may create directories
            fs.Directory.CreateDirectory(Arg.Any<string>())
              .Returns(Substitute.For<DirectoryInfoBase>());

            return cap;
        }

        public sealed class WriteCapture
        {
            public List<MemoryStream> Streams { get; } = new();

            public MemoryStream New()
            {
                var ms = new MemoryStream();
                Streams.Add(ms);
                return ms;
            }

            public string LastText()
            {
                Streams.Should().NotBeEmpty("expected a write to have occurred");
                return Encoding.UTF8.GetString(Streams.Last().ToArray());
            }
        }
    }
}
