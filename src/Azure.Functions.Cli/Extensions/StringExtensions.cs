using Azure.Functions.Cli.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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

        public static string SanitizeImageName(this string imageName)
        {
            if (string.IsNullOrWhiteSpace(imageName))
            {
                throw new System.ArgumentException($"{nameof(imageName)} cannot be null or empty", nameof(imageName));
            }

            string cleanImageName = new Regex(@"[^\d\w_]").Replace(imageName, "");
            if (string.IsNullOrWhiteSpace(cleanImageName))
            {
                throw new ImageNameFormatException(imageName);
            }

            return cleanImageName.ToLowerInvariant().Substring(0, Math.Min(cleanImageName.Length, 128)).Trim();
        }

        public static async Task<string> AppendContent(this string hostJsonContent, string contentPropertyName, Task<string> contentSource)
        {
            var hostJsonObj = JsonConvert.DeserializeObject<JObject>(hostJsonContent);
            var additionalContent = await contentSource;
            var additionalConfig = JsonConvert.DeserializeObject<JToken>(additionalContent);
            hostJsonObj.Add(contentPropertyName, additionalConfig);
            return JsonConvert.SerializeObject(hostJsonObj, Formatting.Indented);
        }
    }
}
