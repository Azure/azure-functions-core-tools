// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.AzureActions;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.StacksApi;
using Moq;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    public class PublishFunctionAppActionTests
    {
        [Fact]
        public async Task UpdateRuntimeConfigForFlex_SkipsUpdate_WhenNoRuntimeVersionDetected()
        {
            // Arrange
            var site = new Site("test-site")
            {
                Location = "eastus",
                FunctionAppConfig = new FunctionAppConfig
                {
                    Runtime = new Runtime
                    {
                        Name = "dotnet-isolated",
                        Version = "9.0"
                    }
                }
            };

            var helperServiceMock = new Mock<PublishFunctionAppAction.AzureHelperService>(null, null);

            // Act - passing null for runtimeVersion should skip the update
            await PublishFunctionAppAction.UpdateRuntimeConfigForFlex(
                site,
                "dotnet-isolated",
                null, // No version detected
                helperServiceMock.Object,
                force: false,
                overwriteSettings: false);

            // Assert - UpdateFlexRuntime should not be called
            helperServiceMock.Verify(
                x => x.UpdateFlexRuntime(It.IsAny<Site>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task UpdateRuntimeConfigForFlex_SkipsUpdate_WhenVersionsMatch()
        {
            // Arrange
            var site = new Site("test-site")
            {
                Location = "eastus",
                FunctionAppConfig = new FunctionAppConfig
                {
                    Runtime = new Runtime
                    {
                        Name = "dotnet-isolated",
                        Version = "9.0"
                    }
                }
            };

            var helperServiceMock = new Mock<PublishFunctionAppAction.AzureHelperService>(null, null);
            helperServiceMock
                .Setup(x => x.GetFlexFunctionsStacks(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(CreateMockFlexStacks());

            // Act - passing same version should skip the update
            await PublishFunctionAppAction.UpdateRuntimeConfigForFlex(
                site,
                "dotnet-isolated",
                "9.0", // Same version as Azure
                helperServiceMock.Object,
                force: false,
                overwriteSettings: false);

            // Assert - UpdateFlexRuntime should not be called
            helperServiceMock.Verify(
                x => x.UpdateFlexRuntime(It.IsAny<Site>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task UpdateRuntimeConfigForFlex_UpdatesRuntime_WhenVersionsDifferAndForceIsTrue()
        {
            // Arrange
            var site = new Site("test-site")
            {
                Location = "eastus",
                FunctionAppConfig = new FunctionAppConfig
                {
                    Runtime = new Runtime
                    {
                        Name = "dotnet-isolated",
                        Version = "8.0"
                    }
                }
            };

            var helperServiceMock = new Mock<PublishFunctionAppAction.AzureHelperService>(null, null);
            helperServiceMock
                .Setup(x => x.GetFlexFunctionsStacks(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(CreateMockFlexStacks());

            // Act - different version with force should update
            await PublishFunctionAppAction.UpdateRuntimeConfigForFlex(
                site,
                "dotnet-isolated",
                "9.0", // Different version from Azure (8.0)
                helperServiceMock.Object,
                force: true, // Force the update
                overwriteSettings: false);

            // Assert - UpdateFlexRuntime should be called
            helperServiceMock.Verify(
                x => x.UpdateFlexRuntime(site, "dotnet-isolated", "9.0"),
                Times.Once);
        }

        [Theory]
        [InlineData(WorkerRuntime.Powershell)]
        [InlineData(WorkerRuntime.Node)]
        [InlineData(WorkerRuntime.Python)]
        [InlineData(WorkerRuntime.Java)]
        public async Task UpdateFrameworkVersions_NonDotnetWindowsApp_DoesNotThrow(WorkerRuntime workerRuntime)
        {
            // Arrange - simulate a Windows function app with a non-.NET runtime and no dotnet version specified.
            // This is the exact scenario that caused "Value cannot be null. (Parameter 'input')" in v4.7.0.
            var site = new Site("test-site")
            {
                Kind = "functionapp", // Windows (no "linux" in Kind)
                Sku = "dynamic",
                NetFrameworkVersion = "v6.0"
            };

            var helperServiceMock = new Mock<PublishFunctionAppAction.AzureHelperService>(null, null);

            // Act - calling with null dotnetVersion (no --dotnet-version specified) should not throw.
            // In v4.7.0 this threw ArgumentNullException: Value cannot be null. (Parameter 'input').
            var exception = await Record.ExceptionAsync(() =>
                PublishFunctionAppAction.UpdateFrameworkVersions(site, workerRuntime, null, false, helperServiceMock.Object));

            // Assert
            Assert.Null(exception);

            // Should not try to update the web settings since there is no version to apply
            helperServiceMock.Verify(
                x => x.UpdateWebSettings(It.IsAny<Site>(), It.IsAny<Dictionary<string, string>>()),
                Times.Never);
        }

        private static FlexFunctionsStacks CreateMockFlexStacks()
        {
            return new FlexFunctionsStacks
            {
                Languages = new List<FlexLanguage>
                {
                    new FlexLanguage
                    {
                        LanguageProperties = new FlexLanguageProperties
                        {
                            MajorVersions = new List<FlexMajorVersion>
                            {
                                new FlexMajorVersion
                                {
                                    MinorVersions = new List<FlexMinorVersion>
                                    {
                                        new FlexMinorVersion
                                        {
                                            StackSettings = new FlexStackSettings
                                            {
                                                LinuxRuntimeSettings = new FlexLinuxRuntimeSettings
                                                {
                                                    Sku = new List<FlexSku>
                                                    {
                                                        new FlexSku
                                                        {
                                                            SkuCode = "FC1",
                                                            FunctionAppConfigProperties = new FunctionAppConfigProperties
                                                            {
                                                                Runtime = new FlexRuntime
                                                                {
                                                                    Name = "dotnet-isolated",
                                                                    Version = "9.0"
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}
