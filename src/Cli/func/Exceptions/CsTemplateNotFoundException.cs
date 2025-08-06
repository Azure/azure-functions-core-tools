namespace Azure.Functions.Cli.Exceptions;

public class CsTemplateNotFoundException(string templateName)
    : Exception($"Unknown template '{templateName}'. Retry without --template option to see available templates.") {}
