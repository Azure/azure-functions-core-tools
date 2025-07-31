// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
using Autofac;
using Azure.Functions.Cli.Actions;
using Azure.Functions.Cli.Actions.AzureActions;
using Azure.Functions.Cli.Actions.DurableActions;
using Azure.Functions.Cli.Actions.HostActions;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    public class ResolveActionTests : IDisposable
    {
        [Theory]
        [InlineData("azure functionapp enable-git-repo appName", typeof(DeprecatedAzureActions))]
        [InlineData("azure functionapp fetch-app-settings appName", typeof(FetchAppSettingsAction))]
        [InlineData("azure functionapp fetch appName", typeof(FetchAppSettingsAction))]
        [InlineData("azure functionapp publish app-name -g resource-group", typeof(PublishFunctionAppAction))]
        [InlineData("azure get-publish-username", typeof(DeprecatedAzureActions))]
        [InlineData("azure account list", typeof(DeprecatedAzureActions))]
        [InlineData("azure subscriptions list", typeof(DeprecatedAzureActions))]
        [InlineData("azure functionapp list", typeof(DeprecatedAzureActions))]
        [InlineData("azure storage list", typeof(DeprecatedAzureActions))]
        [InlineData("azure login", typeof(DeprecatedAzureActions))]
        [InlineData("azure logout", typeof(DeprecatedAzureActions))]
        [InlineData("azure functionapp logstream appName", typeof(LogStreamAction))]
        [InlineData("azure account set accountName", typeof(DeprecatedAzureActions))]
        [InlineData("azure set-publish-password userName", typeof(DeprecatedAzureActions))]
        [InlineData("azure set-publish-username userName", typeof(DeprecatedAzureActions))]
        [InlineData("host start", typeof(StartHostAction))]
        [InlineData("settings add settingName", typeof(AddSettingAction))]
        [InlineData("azure storage fetch-connection-string storageName", typeof(AddStorageAccountSettingAction))]
        [InlineData("new", typeof(CreateFunctionAction))]
        [InlineData("function new", typeof(CreateFunctionAction))]
        [InlineData("function create", typeof(CreateFunctionAction))]
        [InlineData("settings decrypt", typeof(DecryptSettingAction))]
        [InlineData("settings encrypt", typeof(EncryptSettingsAction))]
        [InlineData("settings delete settingName", typeof(DeleteSettingAction))]
        [InlineData("settings list", typeof(ListSettingsAction))]
        [InlineData("init", typeof(InitAction))]
        [InlineData("", typeof(HelpAction))]
        [InlineData("help", typeof(HelpAction))]
        [InlineData("WrongName", typeof(HelpAction))]
        [InlineData("azure functionapp --help", typeof(HelpAction))]
        [InlineData("azure --help", typeof(HelpAction))]
        [InlineData("--help", typeof(HelpAction))]
        [InlineData("durable delete-task-hub --connection-string-setting connectionSettingName", typeof(DurableDeleteTaskHub))]
        [InlineData("durable get-history --id ab1b5de4c5a9450eafa5a4cf249b3169 --show-input true --connection-string-setting connectionSettingName", typeof(DurableGetHistory))]
        [InlineData("durable get-instances --created-before 11/10/2018 --connection-string-setting connectionSettingName --task-hub-name taskHub1", typeof(DurableGetInstances))]
        [InlineData("durable get-runtime-status --id ab1b5de4c5a9450eafa5a4cf249b3169 --connection-string-setting connectionSettingName", typeof(DurableGetRuntimeStatus))]
        [InlineData("durable purge-history --created-after 11/5/2018 --connection-string-setting connectionSettingName", typeof(DurablePurgeHistory))]
        [InlineData("durable raise-event --id ab1b5de4c5a9450eafa5a4cf249b3169 --connection-string-setting connectionSettingName --task-hub-name taskHub2", typeof(DurableRaiseEvent))]
        [InlineData("durable rewind --id ab1b5de4c5a9450eafa5a4cf249b3169 --connection-string-setting connectionSettingName", typeof(DurableRewind))]
        [InlineData("durable start-new --function-name functostart --connection-string-setting connectionSettingName", typeof(DurableStartNew))]
        [InlineData("durable terminate --id ab1b5de4c5a9450eafa5a4cf249b3169 --connection-string-setting connectionSettingName --task-hub-name taskHub3", typeof(DurableTerminate))]
        public void ResolveCommandLineCorrectly(string args, Type returnType)
        {
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Any<string>()).Returns(true);
            FileSystemHelpers.Instance = fileSystem;

            var container = InitializeContainerForTests();
            var app = new ConsoleApp(args.Split(' ').ToArray(), typeof(Program).Assembly, container);
            var result = app.Parse();
            if (returnType == null)
            {
                result.Should().BeNull();
            }
            else
            {
                result.Should().BeOfType(returnType);
            }
        }

        [Theory]
        [InlineData("azure functionapp publish -g resource-group app-name-not-the-first-arg")]
        public void ThrowErrorOnIncorrectCommandLine(string args)
        {
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Any<string>()).Returns(true);
            FileSystemHelpers.Instance = fileSystem;

            var container = InitializeContainerForTests();
            var app = new ConsoleApp(args.Split(' ').ToArray(), typeof(Program).Assembly, container);

            Assert.Throws<CliArgumentsException>(app.Parse);
        }

        private IContainer InitializeContainerForTests()
        {
            var builder = new ContainerBuilder();

            builder.RegisterType<FunctionsLocalServer>()
                .As<IFunctionsLocalServer>();

            builder.Register(_ => new PersistentSettings())
                .As<ISettings>()
                .SingleInstance();

            builder.RegisterType<ProcessManager>()
                .As<IProcessManager>()
                .SingleInstance();

            var mockedSecretsManager = Substitute.For<ISecretsManager>();
            builder.RegisterInstance(mockedSecretsManager)
                .As<ISecretsManager>();
            mockedSecretsManager.GetHostStartSettings().Returns(new HostStartSettings());

            builder.RegisterType<TemplatesManager>()
                .As<ITemplatesManager>();

            builder.RegisterType<ContextHelpManager>()
                .As<IContextHelpManager>();

            builder.RegisterType<DurableManager>()
                .As<IDurableManager>();

            return builder.Build();
        }

        public void Dispose()
        {
            FileSystemHelpers.Instance = null;
        }
    }
}
