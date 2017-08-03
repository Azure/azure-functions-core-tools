using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.AzureActions;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Tests.ActionsTests
{
    public class SettingsActionsTests : ActionTestsBase
    {
        public SettingsActionsTests(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData("Name1", "Value1")]
        public void AddSettingsActionTest(string name, string value)
        {
            // Setup
            Program.Main(new[] { "init" });

            // Test
            Program.Main(new[] { "settings", "add", name, value });

            var content = File.ReadAllText(Path.Combine(WorkingDirectory, "local.settings.json"));

            // Assert
            content.Should().Contain(name);
            content.Should().Contain("IsEncrypted");
            content.Should().Contain("false");
        }

        [Theory]
        [InlineData("Name1", "Value1")]
        public void DeleteSettingsActionTest(string name, string value)
        {
            // Setup
            Program.Main(new[] { "init" });
            Program.Main(new[] { "settings", "add", name, value });

            // Test
            Program.Main(new[] { "settings", "remove", name });
            var content = File.ReadAllText(Path.Combine(WorkingDirectory, "local.settings.json"));

            // Assert
            content.Should().NotContain(name);
        }

        [Theory]
        [InlineData("Name1", "Value1")]
        public void DecryptAndEncryptSettingsActionTest(string name, string value)
        {
            // Setup
            Program.Main(new[] { "init" });
            Program.Main(new[] { "settings", "add", name, value });
            var settingsPath = Path.Combine(WorkingDirectory, "local.settings.json");

            var content = File.ReadAllText(settingsPath);
            content.Should().Contain(name);
            content.Should().Contain(value);
            content.Should().Contain("false");

            Program.Main(new[] { "settings", "encrypt" });

            content = File.ReadAllText(settingsPath);
            content.Should().Contain(name);
            content.Should().NotContain(value);
            content.Should().Contain("true");

            Program.Main(new[] { "settings", "decrypt" });

            content = File.ReadAllText(settingsPath);
            content.Should().Contain(name);
            content.Should().Contain(value);
            content.Should().Contain("false");
        }

        [Theory]
        [InlineData("Name1", "Value1")]
        public void ListSettingsActionTest(string name, string value)
        {
            var console = Substitute.For<IConsoleWriter>();
            var stringBuilder = new StringBuilder();
            console.WriteLine(Arg.Do<object>(o => stringBuilder.AppendLine(o?.ToString()))).Returns(console);
            console.Write(Arg.Do<object>(o => stringBuilder.Append(o.ToString()))).Returns(console);
            ColoredConsole.Out = console;

            Program.Main(new[] { "init" });
            Program.Main(new[] { "settings", "add", name, value });

            stringBuilder.Clear();
            Program.Main(new[] { "settings", "list" });

            var output = stringBuilder.ToString();
            output.Should().Contain(name);
            output.Should().NotContain(value);

            stringBuilder.Clear();

            Program.Main(new[] { "settings", "list", "-a" });

            output = stringBuilder.ToString();
            output.Should().Contain(name);
            output.Should().Contain(value);
        }

        [Theory]
        [InlineData("Name1", "Value1")]
        public void AddConnectionString(string name, string value)
        {
            // Setup
            Program.Main(new[] { "init" });

            // Test
            Program.Main(new[] { "settings", "add", name, value, "--connectionString" });

            var content = File.ReadAllText(Path.Combine(WorkingDirectory, "local.settings.json"));

            // Assert
            content.Should().Contain(name);
            content.Should().Contain("IsEncrypted");
            content.Should().Contain("false");
            content.Should().Contain(value);
            content.Should().Contain(Constants.DefaultSqlProviderName);
        }

        [Fact]
        public async Task AddStorageAccountActionTest()
        {
            Program.Main(new[] { "init" });

            const string storageAccountName = "StorageAccount1";
            var output = new StringBuilder();
            var action = SetupForStorageAccount(storageAccountName, output);
            // Test
            await action.RunAsync();

            // Assert
            output.ToString().Should().Contain($"{storageAccountName}_STORAGE");
        }

        [Fact]
        public async Task AddStorageAccountActionErrorTest()
        {
            var output = new StringBuilder();
            var action = SetupForStorageAccount("StorageName", output);
            action.StorageAccountName = "NotThere";

            await action.RunAsync();

            output.ToString().Should().Contain("Can't find storage account with name");
        }

        private AddStorageAccountSettingAction SetupForStorageAccount(string storageAccountName, StringBuilder output)
        {
            const string storageAccountKey = "Key1";
            const string currentSubscription = "sub1";
            var console = Substitute.For<IConsoleWriter>();
            console.WriteLine(Arg.Do<object>(o => output.AppendLine(o?.ToString()))).Returns(console);
            console.Write(Arg.Do<object>(o => output.Append(o.ToString()))).Returns(console);
            ColoredConsole.Out = console;
            ColoredConsole.Error = console;


            var armManager = Substitute.For<IArmManager>();
            var storageAccount = new StorageAccount(currentSubscription, "resource", storageAccountName, "location") { StorageAccountKey = storageAccountKey };
            armManager.GetStorageAccountsAsync().Returns(new[] {storageAccount});
            armManager.LoadAsync(Arg.Any<StorageAccount>()).Returns(storageAccount);

            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.SetSecret(Arg.Is(storageAccountName), Arg.Any<string>());

            var settings = Substitute.For<ISettings>();
            settings.CurrentSubscription.Returns(currentSubscription);

            var action = new AddStorageAccountSettingAction(armManager, settings, secretsManager);
            action.ParseArgs(new[] { storageAccountName });

            output.Clear();
            return action;
        }
    }
}
