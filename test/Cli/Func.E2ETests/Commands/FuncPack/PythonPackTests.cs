// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Fixtures;
using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Python)]
    public class PythonPackTests : IClassFixture<PythonFunctionAppFixture>
    {
        private readonly PythonFunctionAppFixture _fixture;

        public PythonPackTests(PythonFunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        public void Pack_Python_WorksAsExpected()
        {
            var workingDir = _fixture.WorkingDirectory;
            var testName = nameof(Pack_Python_WorksAsExpected);

            BasePackTests.TestBasicPackFunctionality(
                workingDir,
                testName,
                _fixture,
                new[]
                {
                    "host.json",
                    "requirements.txt",
                    "function_app.py"
                });
        }

        [Fact]
        public void Pack_PythonFromCache_WorksAsExpected()
        {
            var workingDir = _fixture.WorkingDirectory;
            var testName = nameof(Pack_PythonFromCache_WorksAsExpected);
            var syncDirMessage = "Directory .python_packages already in sync with requirements.txt. Skipping restoring dependencies...";

            // Step 2: Run pack for the first time
            var funcPackCommand = new FuncPackCommand(_fixture.FuncPath, testName, _fixture.Log);
            var firstPackResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            firstPackResult.Should().ExitWith(0);
            firstPackResult.Should().HaveStdOutContaining("Creating a new package");
            firstPackResult.Should().NotHaveStdOutContaining(syncDirMessage);

            // Verify .python_packages/requirements.txt.md5 file exists
            var pythonPackagesMd5Path = Path.Combine(workingDir, ".python_packages", "requirements.txt.md5");
            var packFilesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (pythonPackagesMd5Path, new[] { string.Empty }) // Just check file exists, content can be empty
            };
            firstPackResult.Should().FilesExistsWithExpectContent(packFilesToValidate);

            // Step 3: Run pack again without changing requirements.txt (should use cache)
            var secondPackResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            secondPackResult.Should().ExitWith(0);
            secondPackResult.Should().HaveStdOutContaining("Creating a new package");
            secondPackResult.Should().HaveStdOutContaining(syncDirMessage);

            // Step 4: Update requirements.txt and pack again (should restore dependencies)
            var requirementsPath = Path.Combine(workingDir, "requirements.txt");
            _fixture.Log.WriteLine($"Writing to file {requirementsPath}");
            File.WriteAllText(requirementsPath, "requests");

            var thirdPackResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            thirdPackResult.Should().ExitWith(0);
            thirdPackResult.Should().HaveStdOutContaining("Creating a new package");
            thirdPackResult.Should().NotHaveStdOutContaining(syncDirMessage);

            // Verify .python_packages/requirements.txt.md5 file still exists
            thirdPackResult.Should().FilesExistsWithExpectContent(packFilesToValidate);
        }
    }
}
