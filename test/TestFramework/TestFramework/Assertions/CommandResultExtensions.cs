


namespace TestFramework.Assertions
{
    public static class CommandResultExtensions
    {
        public static CommandResultAssertions Should(this CommandResult commandResult) => new CommandResultAssertions(commandResult);
    }
}
