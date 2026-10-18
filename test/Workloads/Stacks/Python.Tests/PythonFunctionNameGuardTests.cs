// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Python.Tests;

public sealed class PythonFunctionNameGuardTests
{
    private readonly PythonFunctionNameGuard _guard = new();
    private const string Snippet = "@app.route(route='new')\ndef Greeting(req):\n    return 'hello'\n";

    // Pinned from Python 3.13 str.isidentifier() and ast.parse(), using Unicode 15.1.
    [Theory]
    [InlineData("\u2160reeting", "Ireeting")]
    [InlineData("\u217fodule", "module")]
    [InlineData("\u2118Greeting", "\u2118Greeting")]
    [InlineData("\u212eGreeting", "\u212eGreeting")]
    [InlineData("\u1885Greeting", "\u1885Greeting")]
    [InlineData("\u1886Greeting", "\u1886Greeting")]
    [InlineData("a\u00b7b", "a\u00b7b")]
    [InlineData("a\u0387b", "a\u00b7b")]
    [InlineData("a\u1369b", "a\u1369b")]
    [InlineData("a\u1371b", "a\u1371b")]
    [InlineData("a\u19dab", "a\u19dab")]
    [InlineData("a\u0301b", "\u00e1b")]
    [InlineData("a\u093eb", "a\u093eb")]
    [InlineData("a\u203fb", "a\u203fb")]
    [InlineData("\U0001d4a2reeting", "Greeting")]
    [InlineData("\uff27reeting", "Greeting")]
    [InlineData("a\uff3fb", "a_b")]
    public void Analyze_Python313IdentifierAndAstOracle_ReservesWholeNormalizedSymbol(string identifier, string astName)
    {
        string original = $"@app.route(route='old')\ndef {identifier}(req):\n    pass\n";
        string generated = $"@app.route(route='new')\ndef {astName}(req):\n    pass\n";

        PythonFunctionNameValidation validation = _guard.Analyze(original, generated, CancellationToken.None);

        validation.Diagnostic.Should().Contain($"'{astName}' already exists");
        validation.Complete.Should().BeTrue();
    }

    [Theory]
    [InlineData("a\u200cb")]
    [InlineData("a\u20ddb")]
    [InlineData("\u309bGreeting")]
    [InlineData("\u309cGreeting")]
    [InlineData("\u037aGreeting")]
    [InlineData("\ufdfaGreeting")]
    public void Analyze_UnrecognizedScalarOrInvalidNfkcClosure_DoesNotClaimCompleteValidation(string identifier)
    {
        string original = $"def {identifier}(req):\n    pass\n";

        PythonFunctionNameValidation validation = _guard.Analyze(original, Snippet, CancellationToken.None);

        validation.Diagnostic.Should().BeNull();
        validation.Complete.Should().BeFalse();
    }

    [Fact]
    public void Analyze_UnrecognizedValidIdentifier_DoesNotReserveTruncatedPrefix()
    {
        const string original = "@app.route(route='old')\ndef Alpha\u200cOther(req):\n    pass\n";
        const string generated = "@app.route(route='new')\ndef Alpha(req):\n    pass\n";

        PythonFunctionNameValidation validation = _guard.Analyze(original, generated, CancellationToken.None);

        validation.Diagnostic.Should().BeNull();
        validation.Complete.Should().BeFalse();
    }

    [Fact]
    public void Analyze_ProgrammaticLiteralNameApplication_RetainsProvableNameDespitePartialAnalysis()
    {
        const string original = "def Different(req):\n    pass\napp.function_name('Greeting')(app.route(route='old')(Different))\n";

        PythonFunctionNameValidation validation = _guard.Analyze(original, Snippet, CancellationToken.None);

        validation.Diagnostic.Should().Contain("'Greeting' already exists");
        validation.Complete.Should().BeFalse();
    }

    [Fact]
    public void Analyze_UnappliedFunctionNameDecorator_DoesNotAssumeRegistration()
    {
        PythonFunctionNameValidation validation = _guard.Analyze("named = app.function_name('Greeting')\n", Snippet, CancellationToken.None);

        validation.Diagnostic.Should().BeNull();
        validation.Complete.Should().BeFalse();
    }

    [Theory]
    [InlineData("@app.function_name('Inner')\n@app.route(route='old')\ndef Different(req):\n    pass\napp.function_name('Greeting')(Different)\n")]
    [InlineData("def Different(req):\n    pass\napp.function_name('Greeting')(app.function_name('Inner')(app.route(route='old')(Different)))\n")]
    [InlineData("def Different(req):\n    pass\nDifferent = app.function_name('Inner')(app.route(route='old')(Different))\napp.function_name('Greeting')(Different)\n")]
    public void Analyze_ExistingBuilderOrMultipleNameSettings_DoesNotAssumeOuterNameOverridesSdkSetting(string original)
    {
        PythonFunctionNameValidation validation = _guard.Analyze(original, Snippet, CancellationToken.None);

        validation.Diagnostic.Should().BeNull();
        validation.Complete.Should().BeFalse();
    }

    [Theory]
    [InlineData("def Different(req):\n    pass\nif False: app.function_name('Greeting')(app.route(route='old')(Different))\n")]
    [InlineData("def Different(req):\n    pass\ndef factory(): return app.function_name('Greeting')(app.route(route='old')(Different))\n")]
    [InlineData("def Different(req):\n    pass\ncreated = app.function_name('Greeting')(app.route(route='old')(Different)) if False else None\n")]
    [InlineData("def Different(req):\n    pass\n@app.function_name('Inner')\n@app.route(route='old')\ndef Different(req):\n    pass\napp.function_name('Greeting')(Different)\n")]
    public void Analyze_ConditionalCallOrDecoratedRebinding_DoesNotReserveUnprovenOuterName(string original)
    {
        PythonFunctionNameValidation validation = _guard.Analyze(original, Snippet, CancellationToken.None);

        validation.Diagnostic.Should().BeNull();
        validation.Complete.Should().BeFalse();
    }

    [Fact]
    public void Analyze_AppReplacedByKnownConstructor_DropsOldRegistryNames()
    {
        const string original = "@app.function_name('Greeting')\n@app.route(route='old')\ndef Different(req):\n    pass\napp = func.FunctionApp()\n";

        PythonFunctionNameValidation validation = _guard.Analyze(original, Snippet, CancellationToken.None);

        validation.Diagnostic.Should().BeNull();
        validation.Complete.Should().BeTrue();
    }

    [Theory]
    [InlineData("alias = app\nalias = other\n@alias.function_name('Greeting')\n@alias.route(route='old')\ndef Different(req):\n    pass\n")]
    [InlineData("app = build_app()\n@app.function_name('Greeting')\n@app.route(route='old')\ndef Different(req):\n    pass\n")]
    public void Analyze_ReceiverReboundToUnknownValue_DoesNotReusePriorAppIdentity(string original)
    {
        PythonFunctionNameValidation validation = _guard.Analyze(original, Snippet, CancellationToken.None);

        validation.Diagnostic.Should().BeNull();
        validation.Complete.Should().BeFalse();
    }

    [Fact]
    public void Analyze_AliasReboundAfterRegistration_RetainsNamesAlreadyRegisteredOnApp()
    {
        const string original = "alias = app\n@alias.function_name('Greeting')\n@alias.route(route='old')\ndef Different(req):\n    pass\nalias = other\n";

        _guard.GetDiagnostic(original, Snippet, CancellationToken.None).Should().Contain("'Greeting' already exists");
    }

    [Fact]
    public void Analyze_AliasedTimerAfterUnrecognizedRebinding_PropagatesValidationLimit()
    {
        const string original = "alias = app\nalias = (app)\ndef Different(timer):\n    pass\n"
            + "alias.timer_trigger(arg_name='timer', schedule='0 * * * * *')(Different)\n";

        PythonFunctionNameValidation validation = _guard.Analyze(original, Snippet, CancellationToken.None);

        validation.Diagnostic.Should().BeNull();
        validation.Complete.Should().BeFalse();
    }

    [Theory]
    [InlineData("register_user_handler(app)\n")]
    [InlineData("register_user_handler(target=app)\n")]
    [InlineData("register_user_handler(other, app)\n")]
    [InlineData("alias = app\nregister_user_handler(alias)\n")]
    public void Analyze_AppPassedToOpaqueRegistrationHelper_AllowsWithValidationLimit(string invocation)
    {
        const string helper = "def register_user_handler(target):\n"
            + "    def DynamicExisting(req):\n        pass\n"
            + "    target.route(route='existing')(DynamicExisting)\n";

        PythonFunctionNameValidation validation = _guard.Analyze(helper + invocation, Snippet, CancellationToken.None);

        validation.Diagnostic.Should().BeNull();
        validation.Complete.Should().BeFalse();
    }

    [Theory]
    [InlineData("def register_user_handler(target):\n    def DynamicExisting(req):\n        pass\n    target.route(route='old')(DynamicExisting)\n")]
    [InlineData("def helper(app):\n    pass\n")]
    [InlineData("def helper(target=app):\n    pass\n")]
    [InlineData("helper(other)\n")]
    [InlineData("# register_user_handler(app)\ntext = 'register_user_handler(app)'\n")]
    public void Analyze_NoOpaqueInvocationOfKnownApp_DoesNotInventRegistrationEvidence(string original)
    {
        PythonFunctionNameValidation validation = _guard.Analyze(original, Snippet, CancellationToken.None);

        validation.Diagnostic.Should().BeNull();
        validation.Complete.Should().BeTrue();
    }

    [Fact]
    public void Analyze_OpaqueHelperInvocation_DoesNotHideKnownLiteralCollision()
    {
        PythonFunctionNameValidation validation = _guard.Analyze("register_user_handler(app)\n" + Snippet, Snippet, CancellationToken.None);

        validation.Diagnostic.Should().Contain("'Greeting' already exists");
        validation.Complete.Should().BeFalse();
    }

    [Fact]
    public void GetDiagnostic_LetterNumberIdentifier_NormalizesWholePythonIdentifier()
    {
        const string original = "@app.route(route='old')\ndef \u2160reeting(req):\n    pass\n";
        const string generated = "@app.route(route='new')\ndef Ireeting(req):\n    pass\n";

        _guard.GetDiagnostic(original, generated, CancellationToken.None).Should().Contain("'Ireeting' already exists");
    }

    [Fact]
    public void Analyze_ProgrammaticTimerRegistration_AllowsWithExplicitValidationLimit()
    {
        const string original = "def Different(timer):\n    pass\napp.timer_trigger(arg_name='timer', schedule='0 * * * * *')(Different)\n";

        PythonFunctionNameValidation validation = _guard.Analyze(original, Snippet, CancellationToken.None);

        validation.Diagnostic.Should().BeNull();
        validation.Complete.Should().BeFalse();
    }

    [Theory]
    [InlineData("def Greeting(req):\n    pass\n")]
    [InlineData("async def Greeting(req):\n    pass\n")]
    [InlineData("class Greeting:\n    pass\n")]
    [InlineData("@app.function_name(name='Greeting')\n@app.route(route='old')\nasync def different(req):\n    pass\n")]
    [InlineData("@app.route(route='old')\n@app.function_name('Greeting')\ndef different(req):\n    pass\n")]
    [InlineData("@app.function_name(\n    name = (\n        'Greet' 'ing'\n    ),\n)\n@app.route(route='old')\ndef different(req):\n    pass\n")]
    [InlineData("@app.function_name(r'Greeting')\n@app.route(route='old')\ndef different(req):\n    pass\n")]
    [InlineData("@app.function_name('Greet\\u0069ng')\n@app.route(route='old')\ndef different(req):\n    pass\n")]
    [InlineData("@app.function_name('Greet\\x69ng')\n@app.route(route='old')\ndef different(req):\n    pass\n")]
    [InlineData("@app.function_name('Greet\\151ng')\n@app.route(route='old')\ndef different(req):\n    pass\n")]
    [InlineData("@app.\uff46\uff55\uff4e\uff43\uff54\uff49\uff4f\uff4e_\uff4e\uff41\uff4d\uff45('Greeting')\n@app.route(route='old')\ndef different(req):\n    pass\n")]
    [InlineData("@app.route(route='old')\ndef \U0001d4a2reeting(req):\n    pass\n")]
    [InlineData("application = app\n@application.function_name('Greeting')\n@application.route(route='old')\ndef different(req):\n    pass\n")]
    public void GetDiagnostic_ExistingSymbolOrEffectiveNameCollision_Refuses(string original)
    {
        _guard.GetDiagnostic(original, Snippet, CancellationToken.None).Should().Contain("already exists");
    }

    [Theory]
    [InlineData("# def Greeting(req):\n# @app.function_name('Greeting')\n")]
    [InlineData("s = \"def Greeting(req):\"\nt = '@app.function_name(\"Greeting\")'\n")]
    [InlineData("s = '''\n@app.route(route='old')\ndef Greeting(req):\n    pass\n'''\n")]
    [InlineData("s = r'\\n def Greeting(req):'\n")]
    [InlineData("s = f'Greeting {42}'\n")]
    [InlineData("def helper():\n    def Greeting(req):\n        pass\n")]
    [InlineData("class Container:\n    def Greeting(self):\n        pass\n")]
    [InlineData("@app.route(route='old')\ndef greeting(req):\n    pass\n")]
    [InlineData("@app.function_name('greeting')\n@app.route(route='old')\ndef different(req):\n    pass\n")]
    [InlineData("function_name = 'Greeting'\n")]
    [InlineData("logger.info('app.register_functions(bp)')\nregistry.register_functions(handlers)\n")]
    [InlineData("@registry.function_name('Greeting')\ndef other():\n    pass\n")]
    [InlineData("# app.register_functions(bp)\ntext = 'app.register_functions(bp)'\n")]
    [InlineData("@app.route(route='old')\ndef \U0001d4a2oodbye(req):\n    pass\n")]
    public void GetDiagnostic_CommentsStringsLocalSymbolsAndCaseDistinctNames_AllowsAppend(string original)
    {
        _guard.GetDiagnostic(original, Snippet, CancellationToken.None).Should().BeNull();
    }

    [Theory]
    [InlineData("@app.function_name(name=NAME)\n@app.route(route='old')\ndef different(req):\n    pass\n")]
    [InlineData("@app.function_name(f'Greeting{suffix}')\n@app.route(route='old')\ndef different(req):\n    pass\n")]
    [InlineData("@app.function_name('\\N{LATIN CAPITAL LETTER G}reeting')\n@app.route(route='old')\ndef different(req):\n    pass\n")]
    [InlineData("app.register_functions(imported_blueprint)\n")]
    [InlineData("app.register_blueprint(imported_blueprint)\n")]
    [InlineData("app.function_name('Greeting')(existing)\n")]
    [InlineData("application = func.FunctionApp()\napplication.register_functions(imported_blueprint)\n")]
    [InlineData("app.\uff52\uff45\uff47\uff49\uff53\uff54\uff45\uff52_functions(imported_blueprint)\n")]
    [InlineData("app.route(route='Greeting')(existing)\n")]
    [InlineData("def factory():\n    @app.route(route='old')\n    def Greeting(req):\n        pass\nfactory()\n")]
    [InlineData("if False:\n    @app.route(route='old')\n    def Greeting(req):\n        pass\n")]
    [InlineData("if enabled:\n    app.function_name('Greeting')(existing)\n")]
    [InlineData("s = f\"{data[\"key\"]}\"\n")]
    [InlineData("s = '''unclosed\n")]
    [InlineData("value = (\n")]
    public void Analyze_UnsupportedRegistrationOrLexicalShape_AllowsWithExplicitValidationLimit(string original)
    {
        PythonFunctionNameValidation validation = _guard.Analyze(original, Snippet, CancellationToken.None);
        validation.Diagnostic.Should().BeNull();
        validation.Complete.Should().BeFalse();
    }

    [Fact]
    public void Analyze_UnpairedSurrogate_ReportsLimitWithoutNormalizingInvalidUnicode()
    {
        PythonFunctionNameValidation validation = _guard.Analyze("def \ud835(req):\n    pass\n", Snippet, CancellationToken.None);
        validation.Diagnostic.Should().BeNull();
        validation.Complete.Should().BeFalse();
    }

    [Fact]
    public void Analyze_UnknownRegistration_DoesNotHideProvableCollisionLaterInSource()
    {
        PythonFunctionNameValidation validation = _guard.Analyze("app.register_functions(imported_blueprint)\n" + Snippet, Snippet, CancellationToken.None);

        validation.Diagnostic.Should().Contain("'Greeting' already exists");
        validation.Complete.Should().BeFalse();
    }

    [Fact]
    public void Analyze_UnregisteredBlueprintEffectiveName_DoesNotClaimItBelongsToApp()
    {
        const string original = "bp = func.Blueprint()\n@bp.function_name('Greeting')\n@bp.route(route='unused')\ndef Different(req):\n    pass\n";

        PythonFunctionNameValidation validation = _guard.Analyze(original, Snippet, CancellationToken.None);

        validation.Diagnostic.Should().BeNull();
        validation.Complete.Should().BeFalse();
    }

    [Fact]
    public void GetDiagnostic_GeneratedMultiFunctionPayload_ChecksEveryEffectiveName()
    {
        const string original = "@app.function_name('Reserved')\n@app.route(route='old')\ndef old(req):\n    pass\n";
        const string generated = "@app.route(route='new')\ndef Fresh(req):\n    pass\n"
            + "@app.function_name('Reserved')\n@app.route(route='other')\ndef Different(req):\n    pass\n";

        _guard.GetDiagnostic(original, generated, CancellationToken.None).Should().Contain("'Reserved' already exists");
    }

    [Fact]
    public void GetDiagnostic_PythonIdentifierNormalization_DoesNotNormalizeExplicitSdkName()
    {
        const string original = "@app.route(route='old')\ndef \u212a(req):\n    pass\n";
        const string generated = "@app.route(route='new')\ndef K(req):\n    pass\n";
        _guard.GetDiagnostic(original, generated, CancellationToken.None).Should().Contain("'K' already exists");

        const string explicitName = "@app.function_name('\u212a')\n@app.route(route='old')\ndef old(req):\n    pass\n";
        _guard.GetDiagnostic(explicitName, generated, CancellationToken.None).Should().BeNull();
    }

    [Theory]
    [InlineData(null, "snippet")]
    [InlineData("source", null)]
    public void GetDiagnostic_NullArgument_Throws(string? source, string? snippet)
    {
        Action act = () => _guard.GetDiagnostic(source!, snippet!, CancellationToken.None);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetDiagnostic_PreCanceled_Throws()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Action act = () => _guard.GetDiagnostic(string.Empty, Snippet, cancellation.Token);
        act.Should().Throw<OperationCanceledException>();
    }
}
