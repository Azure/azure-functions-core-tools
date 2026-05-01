// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;
using Azure.Functions.Cli.Commands;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Internal adapter that wraps a workload-contributed <see cref="FuncCommand"/>
/// in a <see cref="FuncCliCommand"/> the parser can execute, while carrying the
/// owning <see cref="WorkloadInfo"/> for diagnostics and tracing.
///
/// Translates the parser-independent descriptors on <see cref="FuncCommand"/>
/// (<see cref="FuncCommand.Options"/>, <see cref="FuncCommand.Arguments"/>,
/// <see cref="FuncCommand.Subcommands"/>) into <see cref="System.CommandLine"/>
/// equivalents at construction. Subcommands are translated recursively as
/// nested <see cref="ExternalCommand"/> instances under this parent — they are
/// not registered as separate top-level services, so they cannot float to the
/// root.
///
/// The descriptors on the source command are read once at construction;
/// subsequent changes are not reflected.
/// </summary>
internal sealed class ExternalCommand : FuncCliCommand
{
    private static readonly MethodInfo _createOptionMethod =
        typeof(ExternalCommand).GetMethod(nameof(CreateTypedOption), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo _createArgumentMethod =
        typeof(ExternalCommand).GetMethod(nameof(CreateTypedArgument), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly Dictionary<FuncCommandOption, Option> _optionLookup;
    private readonly Dictionary<FuncCommandArgument, Argument> _argumentLookup;

    public ExternalCommand(WorkloadInfo workload, FuncCommand source)
        : base(ValidateName(source), ValidateDescription(source))
    {
        ArgumentNullException.ThrowIfNull(workload);

        Workload = workload;
        Source = source;

        _optionLookup = new Dictionary<FuncCommandOption, Option>(ReferenceEqualityComparer.Instance);
        _argumentLookup = new Dictionary<FuncCommandArgument, Argument>(ReferenceEqualityComparer.Instance);

        AddOptions(source.Options);
        AddArguments(source.Arguments);
        AddSubcommands(source.Subcommands);
    }

    /// <summary>The workload that registered the underlying <see cref="FuncCommand"/>.</summary>
    public WorkloadInfo Workload { get; }

    /// <summary>The workload-supplied command this adapter is wrapping.</summary>
    public FuncCommand Source { get; }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var context = new ParseResultInvocationContext(parseResult, _optionLookup, _argumentLookup);
        return Source.ExecuteAsync(context, cancellationToken);
    }

    private static string ValidateName(FuncCommand source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source.Name))
        {
            throw new InvalidOperationException(
                $"FuncCommand '{source.GetType().FullName}' returned a null or empty Name.");
        }
        return source.Name;
    }

    private static string ValidateDescription(FuncCommand source)
    {
        // Source already null-checked in ValidateName via base call ordering.
        return source.Description ?? string.Empty;
    }

    private void AddOptions(IReadOnlyList<FuncCommandOption> options)
    {
        if (options is null)
        {
            return;
        }

        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in options)
        {
            if (descriptor is null)
            {
                throw BuildSourceError($"command '{Source.Name}' declared a null option.");
            }

            if (!seenTokens.Add(descriptor.Name))
            {
                throw BuildSourceError(
                    $"command '{Source.Name}' declared option '{descriptor.Name}' more than once.");
            }

            if (descriptor.ShortName is not null && !seenTokens.Add(descriptor.ShortName))
            {
                throw BuildSourceError(
                    $"command '{Source.Name}' declared a duplicate option token '{descriptor.ShortName}'.");
            }

            var sclOption = (Option)_createOptionMethod
                .MakeGenericMethod(descriptor.ValueType)
                .Invoke(null, [descriptor])!;

            Options.Add(sclOption);
            _optionLookup.Add(descriptor, sclOption);
        }
    }

    private void AddArguments(IReadOnlyList<FuncCommandArgument> arguments)
    {
        if (arguments is null)
        {
            return;
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in arguments)
        {
            if (descriptor is null)
            {
                throw BuildSourceError($"command '{Source.Name}' declared a null argument.");
            }

            if (!seenNames.Add(descriptor.Name))
            {
                throw BuildSourceError(
                    $"command '{Source.Name}' declared argument '{descriptor.Name}' more than once.");
            }

            var sclArgument = (Argument)_createArgumentMethod
                .MakeGenericMethod(descriptor.ValueType)
                .Invoke(null, [descriptor])!;

            Arguments.Add(sclArgument);
            _argumentLookup.Add(descriptor, sclArgument);
        }
    }

    private void AddSubcommands(IReadOnlyList<FuncCommand> subcommands)
    {
        if (subcommands is null)
        {
            return;
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subcommand in subcommands)
        {
            if (subcommand is null)
            {
                throw BuildSourceError($"command '{Source.Name}' declared a null subcommand.");
            }

            if (string.IsNullOrWhiteSpace(subcommand.Name))
            {
                throw BuildSourceError(
                    $"command '{Source.Name}' declared a subcommand with a null or empty Name.");
            }

            if (!seenNames.Add(subcommand.Name))
            {
                throw BuildSourceError(
                    $"command '{Source.Name}' declared subcommand '{subcommand.Name}' more than once.");
            }

            Subcommands.Add(new ExternalCommand(Workload, subcommand));
        }
    }

    private InvalidOperationException BuildSourceError(string message)
        => new($"Workload '{Workload.PackageId}' {message}");

    private static Option<T> CreateTypedOption<T>(FuncCommandOption descriptor)
    {
        var typed = (FuncCommandOption<T>)descriptor;
        var aliases = typed.ShortName is null ? [] : new[] { typed.ShortName };
        var option = new Option<T>(typed.Name, aliases)
        {
            Description = typed.Description,
        };

        if (typed.HasDefaultValue)
        {
            var defaultValue = typed.DefaultValue;
            option.DefaultValueFactory = _ => defaultValue;
        }

        return option;
    }

    private static Argument<T> CreateTypedArgument<T>(FuncCommandArgument descriptor)
    {
        var typed = (FuncCommandArgument<T>)descriptor;
        return new Argument<T>(typed.Name)
        {
            Description = typed.Description,
            Arity = typed.IsRequired ? ArgumentArity.ExactlyOne : ArgumentArity.ZeroOrOne,
        };
    }

    /// <summary>
    /// <see cref="FuncCommandInvocationContext"/> implementation backed by the
    /// parser's <see cref="ParseResult"/>. Lookups go through reference-keyed
    /// dictionaries built at <see cref="ExternalCommand"/> construction, so
    /// passing a different descriptor instance with the same name is reported
    /// as an unknown descriptor (matching the abstractions contract).
    /// </summary>
    private sealed class ParseResultInvocationContext : FuncCommandInvocationContext
    {
        private readonly ParseResult _parseResult;
        private readonly IReadOnlyDictionary<FuncCommandOption, Option> _options;
        private readonly IReadOnlyDictionary<FuncCommandArgument, Argument> _arguments;

        public ParseResultInvocationContext(
            ParseResult parseResult,
            IReadOnlyDictionary<FuncCommandOption, Option> options,
            IReadOnlyDictionary<FuncCommandArgument, Argument> arguments)
        {
            _parseResult = parseResult;
            _options = options;
            _arguments = arguments;
        }

        public override T? GetValue<T>(FuncCommandOption<T> option)
            where T : default
        {
            ArgumentNullException.ThrowIfNull(option);
            if (!_options.TryGetValue(option, out var sclOption))
            {
                throw new ArgumentException(
                    $"Option '{option.Name}' is not declared on the command being executed. " +
                    "Pass the same FuncCommandOption instance the command exposed through Options.",
                    nameof(option));
            }

            return _parseResult.GetValue((Option<T>)sclOption);
        }

        public override T? GetValue<T>(FuncCommandArgument<T> argument)
            where T : default
        {
            ArgumentNullException.ThrowIfNull(argument);
            if (!_arguments.TryGetValue(argument, out var sclArgument))
            {
                throw new ArgumentException(
                    $"Argument '{argument.Name}' is not declared on the command being executed. " +
                    "Pass the same FuncCommandArgument instance the command exposed through Arguments.",
                    nameof(argument));
            }

            return _parseResult.GetValue((Argument<T>)sclArgument);
        }
    }
}
