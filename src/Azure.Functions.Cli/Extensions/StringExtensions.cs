using Newtonsoft.Json;

namespace Azure.Functions.Cli.Extensions
{
    internal static class StringExtensions
    {
        // http://stackoverflow.com/a/11838215
        public static bool IsJson(this string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            try
            {
                JsonConvert.DeserializeObject(input);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string TrimQuotes(this string input)
        {
            return input.Trim(new char[] { '\'', '\"' });
        }
    }
}
