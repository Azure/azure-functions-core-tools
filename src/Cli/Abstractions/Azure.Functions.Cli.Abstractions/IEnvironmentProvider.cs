
namespace Azure.Functions.Cli.Abstractions
{
    public interface IEnvironmentProvider
    {
        IEnumerable<string> ExecutableExtensions { get; }

        string? GetCommandPath(string commandName, params string[] extensions);

        string? GetCommandPathFromRootPath(string rootPath, string commandName, params string[] extensions);

        string? GetCommandPathFromRootPath(string rootPath, string commandName, IEnumerable<string> extensions);

        bool GetEnvironmentVariableAsBool(string name, bool defaultValue);

        int? GetEnvironmentVariableAsNullableInt(string name);

        string? GetEnvironmentVariable(string name);

        string? GetEnvironmentVariable(string variable, EnvironmentVariableTarget target);

        void SetEnvironmentVariable(string variable, string value, EnvironmentVariableTarget target);
    }

}
