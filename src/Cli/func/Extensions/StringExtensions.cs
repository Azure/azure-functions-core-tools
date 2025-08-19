// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using Azure.Functions.Cli.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

            string cleanImageName = new Regex(@"[^\d\w_]").Replace(imageName, string.Empty);
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

        public static bool EqualsIgnoreCase(this string str1, string str2)
        {
            return string.Equals(str1, str2, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Retrieves the value associated with a specified key from a delimited key-value string.
        /// </summary>
        /// <param name="input">Retrieves the value associated with a specified key from a delimited key-value pair.</param>
        /// <param name="key">The key whose value will be retrieved (case-insensitive match).</param>
        /// <param name="delimiter">The character that separates each key-value pair in the input string (default is ';').</param>
        /// <returns>The value associated with the specified key, or <c>null</c> if the key is not found or the input is invalid.</returns>
        public static string GetValueFromDelimitedString(this string input, string key, char delimiter = ';')
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            var span = input.AsSpan();
            var start = 0;

            while (start < span.Length)
            {
                var delimiterIndex = span.Slice(start).IndexOf(delimiter);
                var length = delimiterIndex == -1 ? span.Length - start : delimiterIndex;
                var segment = span.Slice(start, length).Trim();

                start += length + (delimiterIndex == -1 ? 0 : 1);

                if (segment.IsEmpty)
                {
                    continue;
                }

                int equalsIndex = segment.IndexOf('=');
                if (equalsIndex <= 0 || equalsIndex >= segment.Length - 1)
                {
                    continue;
                }

                var keyPart = segment.Slice(0, equalsIndex).Trim();
                var valuePart = segment.Slice(equalsIndex + 1).Trim();

                if (keyPart.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return valuePart.ToString();
                }
            }

            return null;
        }
    }
}
