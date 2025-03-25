using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Azure.Functions.Cli.Common
{
    /// <summary>
    /// This is a C# reimplementation of https://github.com/codemix/gitignore-parser
    /// License for gitignore-parser:
    /// # Copyright 2014 codemix ltd.
    /// Licensed under the Apache License, Version 2.0 (the "License");
    /// you may not use this file except in compliance with the License.
    /// You may obtain a copy of the License at
    ///
    /// [http://www.apache.org/licenses/LICENSE-2.0](http://www.apache.org/licenses/LICENSE-2.0)
    ///
    /// Unless required by applicable law or agreed to in writing, software
    /// distributed under the License is distributed on an "AS IS" BASIS,
    /// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
    /// See the License for the specific language governing permissions and
    /// limitations under the License.
    /// </summary>
    public class GitIgnoreParser
    {
        private readonly Regex[] _negative;
        private readonly Regex[] _positive;

        public GitIgnoreParser(string gitIgnoreContent)
        {
            var parsed = gitIgnoreContent
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => !l.StartsWith("#"))
                .Where(l => !string.IsNullOrEmpty(l))
                .Aggregate(new List<List<string>>() { new List<string>(), new List<string>() }, (lists, line) =>
                {
                    var isNegative = line.StartsWith("!");
                    if (isNegative)
                    {
                        line = line.Substring(1);
                    }
                    if (line.StartsWith("/"))
                    {
                        line = line.Substring(1);
                    }
                    if (isNegative)
                    {
                        lists[1].Add(line);
                    }
                    else
                    {
                        lists[0].Add(line);
                    }
                    return lists;
                })
                .Select(l =>
                {
                    return l
                        .OrderBy(i => i)
                        .Select(i => new[] { PrepareRegexPattern(i), PreparePartialRegex(i) })
                        .Aggregate(new List<List<string>>() { new List<string>(), new List<string>() }, (list, prepared) =>
                        {
                            list[0].Add(prepared[0]);
                            list[1].Add(prepared[1]);
                            return list;
                        });
                })
                .Select(item => new[]
                {
                    item[0].Count > 0 ? new Regex("^((" + string.Join(")|(", item[0]) + "))\\b", RegexOptions.ECMAScript) : new Regex("$^", RegexOptions.ECMAScript),
                    item[1].Count > 0 ? new Regex("^((" + string.Join(")|(", item[1]) + "))\\b", RegexOptions.ECMAScript) : new Regex("$^", RegexOptions.ECMAScript)
                })
                .ToArray();
            _positive = parsed[0];
            _negative = parsed[1];
        }

        public bool Accepts(string input)
        {
            if (input == "/")
            {
                input = input.Substring(1);
            }
            return _negative[0].IsMatch(input) || !_positive[0].IsMatch(input);
        }

        public bool Denies(string input)
        {
            if (input == "/")
            {
                input = input.Substring(1);
            }
            return !(_negative[0].IsMatch(input) || !_positive[0].IsMatch(input));
        }

        private string PrepareRegexPattern(string pattern)
        {
            return Regex.Replace(pattern, @"[\-\[\]\/\{\}\(\)\+\?\.\\\^\$\|]", "\\$&", RegexOptions.ECMAScript)
                .Replace("**", "(.+)")
                .Replace("*", "([^\\/]+)");
        }

        private string PreparePartialRegex(string pattern)
        {
            return pattern
                .Split('/')
                .Select((item, index) =>
                {
                    if (index == 0)
                    {
                        return "([\\/]?(" + PrepareRegexPattern(item) + "\\b|$))";
                    }
                    else
                    {
                        return "(" + PrepareRegexPattern(item) + "\\b)";
                    }
                })
                .Aggregate((a, b) => a + b);
        }
    }
}
