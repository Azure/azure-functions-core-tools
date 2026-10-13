// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Projects;

public class InstalledProjectStackCatalogTests
{
    [Fact]
    public void Constructor_EmptyInventory_IsSupported()
    {
        InstalledProjectStackCatalog catalog = new([]);

        catalog.Stacks.Should().BeEmpty();
        catalog.FindStack("dotnet").Should().BeNull();
        catalog.GetCanonicalLanguage("C#").Should().BeNull();
        catalog.GetStacksForLanguage("C#").Should().BeEmpty();
    }

    [Fact]
    public void Constructor_NullInventory_Throws()
    {
        FluentActions.Invoking(() => new InstalledProjectStackCatalog(null!))
            .Should().ThrowExactly<ArgumentNullException>().WithParameterName("stacks");
    }

    [Fact]
    public void Constructor_NullEntry_Throws()
    {
        FluentActions.Invoking(() => new InstalledProjectStackCatalog([null!]))
            .Should().ThrowExactly<ArgumentNullException>().WithParameterName("stacks");
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("DOTNET")]
    public void Constructor_DuplicateStack_Throws(string duplicate)
    {
        IProjectStack first = CreateStack("dotnet", "C#", "csharp");
        IProjectStack second = CreateStack(duplicate, "F#", "fsharp");

        FluentActions.Invoking(() => new InstalledProjectStackCatalog([first, second]))
            .Should().ThrowExactly<InvalidOperationException>().WithMessage("*Multiple*stack*");
    }

    [Theory]
    [InlineData("go")]
    [InlineData("GO")]
    [InlineData(" native ")]
    [InlineData("NATIVE")]
    public void FindStack_CanonicalOrRuntimeAlias_ReturnsOwner(string name)
    {
        IProjectStack stack = CreateStack("go", "Go", "golang");
        stack.WorkerRuntimeAliases.Returns(["native"]);
        InstalledProjectStackCatalog catalog = new([stack]);

        catalog.FindStack(name).Should().BeSameAs(stack);
    }

    [Theory]
    [InlineData("JavaScript")]
    [InlineData("JAVASCRIPT")]
    [InlineData(" JS ")]
    public void GetCanonicalLanguage_NameOrAlias_ReturnsCanonicalSpelling(string language)
    {
        InstalledProjectStackCatalog catalog = new([CreateStack("node", "JavaScript", "js")]);

        catalog.GetCanonicalLanguage(language).Should().Be("JavaScript");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetStacksForLanguage_SharedLanguage_ReturnsEveryOwner(bool reverse)
    {
        IProjectStack first = CreateStack("node", "JavaScript", "js");
        IProjectStack second = CreateStack("another-js-stack", "JavaScript", "javascript-alt");
        IProjectStack unrelated = CreateStack("python", "Python", "py");
        IProjectStack[] owners = reverse ? [second, first] : [first, second];
        InstalledProjectStackCatalog catalog = new([.. owners, unrelated]);

        catalog.GetStacksForLanguage("JS").Should().Equal(owners);
        catalog.GetStacksForLanguage("javascript-alt").Should().Equal(owners);
        catalog.GetStacksForLanguage("JavaScript").Should().Equal(owners);
        catalog.GetStacksForLanguage("ruby").Should().BeEmpty();
    }

    [Fact]
    public void GetStacksForLanguage_SharedAlias_IsNotADuplicateStack()
    {
        IProjectStack first = CreateStack("first", "JavaScript", "js");
        IProjectStack second = CreateStack("second", "JavaScript", "JS");
        InstalledProjectStackCatalog catalog = new([first, second]);

        catalog.GetStacksForLanguage("js").Should().Equal(first, second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Constructor_AmbiguousRuntimeAlias_Throws(bool reverse)
    {
        IProjectStack first = CreateStack("first", "Go", "golang");
        first.WorkerRuntimeAliases.Returns(["native"]);
        IProjectStack second = CreateStack("native", "Python", "py");
        IProjectStack[] stacks = reverse ? [second, first] : [first, second];

        FluentActions.Invoking(() => new InstalledProjectStackCatalog(stacks))
            .Should().ThrowExactly<InvalidOperationException>().WithMessage("*native*first*");
    }

    [Fact]
    public void Constructor_AmbiguousLanguageAlias_Throws()
    {
        IProjectStack first = CreateStack("first", "JavaScript", "script");
        IProjectStack second = CreateStack("second", "Python", "SCRIPT");

        FluentActions.Invoking(() => new InstalledProjectStackCatalog([first, second]))
            .Should().ThrowExactly<InvalidOperationException>().WithMessage("*language*SCRIPT*JavaScript*Python*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Constructor_LanguageAliasCollidesWithCanonicalName_Throws(bool reverse)
    {
        IProjectStack first = CreateStack("first", "JavaScript", "python");
        IProjectStack second = CreateStack("second", "Python", "py");
        IProjectStack[] stacks = reverse ? [second, first] : [first, second];

        FluentActions.Invoking(() => new InstalledProjectStackCatalog(stacks))
            .Should().ThrowExactly<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" padded ")]
    public void Constructor_InvalidStackName_Throws(string? name)
    {
        IProjectStack stack = CreateStack(name!, "Go", "golang");

        FluentActions.Invoking(() => new InstalledProjectStackCatalog([stack])).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" padded ")]
    public void Constructor_InvalidLanguageName_Throws(string? language)
    {
        IProjectStack stack = CreateStack("go", "Go", "golang");
        stack.SupportedLanguages.Returns([language!]);

        FluentActions.Invoking(() => new InstalledProjectStackCatalog([stack])).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" padded ")]
    public void Constructor_InvalidAlias_Throws(string? alias)
    {
        IProjectStack stack = CreateStack("go", "Go", alias!);

        FluentActions.Invoking(() => new InstalledProjectStackCatalog([stack])).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_UnsupportedAliasLanguage_Throws()
    {
        IProjectStack stack = CreateStack("go", "Go", "golang");
        stack.SupportedLanguages.Returns(["Python"]);

        FluentActions.Invoking(() => new InstalledProjectStackCatalog([stack]))
            .Should().ThrowExactly<ArgumentException>().WithMessage("*unsupported language*");
    }

    [Fact]
    public void Constructor_NoLanguages_Throws()
    {
        IProjectStack stack = CreateStack("go", "Go", "golang");
        stack.SupportedLanguages.Returns([]);

        FluentActions.Invoking(() => new InstalledProjectStackCatalog([stack]))
            .Should().ThrowExactly<ArgumentException>().WithMessage("*at least one language*");
    }

    [Fact]
    public void Constructor_DuplicateLanguagesWithinStack_Throws()
    {
        IProjectStack stack = CreateStack("go", "Go", "golang");
        stack.SupportedLanguages.Returns(["Go", "GO"]);

        FluentActions.Invoking(() => new InstalledProjectStackCatalog([stack]))
            .Should().ThrowExactly<ArgumentException>().WithMessage("*more than once*");
    }

    [Theory]
    [InlineData("display")]
    [InlineData("runtimes")]
    [InlineData("languages")]
    [InlineData("aliases")]
    [InlineData("alias-values")]
    public void Constructor_NullMetadata_Throws(string property)
    {
        IProjectStack stack = CreateStack("go", "Go", "golang");
        switch (property)
        {
            case "display":
                stack.DisplayName.Returns((string)null!);
                break;
            case "runtimes":
                stack.WorkerRuntimeAliases.Returns((IReadOnlyList<string>)null!);
                break;
            case "languages":
                stack.SupportedLanguages.Returns((IReadOnlyList<string>)null!);
                break;
            case "aliases":
                stack.SupportedLanguageAliases.Returns((IReadOnlyDictionary<string, IReadOnlyList<string>>)null!);
                break;
            case "alias-values":
                stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["Go"] = null! });
                break;
        }

        FluentActions.Invoking(() => new InstalledProjectStackCatalog([stack])).Should().ThrowExactly<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Lookups_InvalidInput_Throw(string? value)
    {
        InstalledProjectStackCatalog catalog = new([]);

        FluentActions.Invoking(() => catalog.FindStack(value!)).Should().Throw<ArgumentException>().WithParameterName("stack");
        FluentActions.Invoking(() => catalog.GetCanonicalLanguage(value!)).Should().Throw<ArgumentException>().WithParameterName("language");
        FluentActions.Invoking(() => catalog.GetStacksForLanguage(value!)).Should().Throw<ArgumentException>().WithParameterName("language");
    }

    [Fact]
    public void Constructor_CopiesInventory()
    {
        IProjectStack stack = CreateStack("go", "Go", "golang");
        List<IProjectStack> registrations = [stack];
        InstalledProjectStackCatalog catalog = new(registrations);

        registrations.Clear();

        catalog.Stacks.Should().ContainSingle().Which.Should().BeSameAs(stack);
    }

    private static IProjectStack CreateStack(string id, string language, string alias)
    {
        IProjectStack stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns(id);
        stack.DisplayName.Returns(id);
        stack.WorkerRuntimeAliases.Returns([]);
        stack.SupportedLanguages.Returns([language]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { [language] = [alias] });
        return stack;
    }
}
