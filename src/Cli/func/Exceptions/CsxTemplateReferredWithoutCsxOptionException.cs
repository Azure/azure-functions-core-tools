namespace Azure.Functions.Cli.Exceptions;

public class CsxTemplateReferredWithoutCsxOptionException(string templateName)
    : Exception($"Template '{templateName}' is for C# script in in-process model. Retry with --csx option to use the template. Or, retry without --template option to see available templates.") {}
