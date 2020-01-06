using Azure.Functions.Cli.Common;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.Tests
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
    public class GitIgnoreParserTests
    {
        const string gitIgnoreFile = @"# This is a comment in a .gitignore file!
/node_modules
*.log

# Ignore this nonexistent file
/nonexistent

# Do not ignore this file
!/nonexistent/foo

# Ignore some files

/baz

/foo/*.wat

/test1

test2

# Ignore some deep sub folders
/othernonexistent/**/what

# Unignore some other sub folders
!/othernonexistent/**/what/foo


*.swp
";
        const string gitIgnoreNoNegatives = "node_modules";
        private GitIgnoreParser _gitignore = new GitIgnoreParser(gitIgnoreFile);
        private GitIgnoreParser _gitignoreNoNegatives = new GitIgnoreParser(gitIgnoreNoNegatives);

        [Fact]
        public void AcceptShouldAcceptTheGivenFilenames()
        {
            _gitignore.Accepts("test/index.js").Should().BeTrue();
            _gitignore.Accepts("wat/test/index.js").Should().BeTrue();
            _gitignore.Accepts("/othernonexistent/blah/adsasd/whatads").Should().BeTrue();
            _gitignore.Accepts("/othernonexistent/blah/adsasd/what/foo").Should().BeTrue();
            _gitignoreNoNegatives.Accepts("test/index.js").Should().BeTrue();
        }

        [Fact]
        public void AcceptShouldAcceptFileNameThatContainsNameIgnored()
        {
            _gitignore.Accepts("test1").Should().BeFalse();
            _gitignore.Accepts("test1File.wat").Should().BeTrue();
        }

        [Fact]
        public void AcceptShouldNotAcceptTheGivenFilenames()
        {
            _gitignore.Accepts("test.swp").Should().BeFalse();
            _gitignore.Accepts("node_modules/wat.js").Should().BeFalse();
            _gitignore.Accepts("foo/bar.wat").Should().BeFalse();
            _gitignore.Accepts("othernonexistent/blah/what").Should().BeFalse();
            _gitignoreNoNegatives.Accepts("node_modules/wat.js").Should().BeFalse();
        }

        [Fact]
        public void AcceptShouldNotAcceptTheGivenDirectory()
        {
            _gitignore.Accepts("nonexistent").Should().BeFalse();
            _gitignore.Accepts("nonexistent/bar").Should().BeFalse();
            _gitignore.Accepts("test2").Should().BeFalse();
            _gitignoreNoNegatives.Accepts("node_modules").Should().BeFalse();
        }

        [Fact]
        public void AcceptShouldAcceptUnignoredFilesInIgnoredDirectories()
        {
            _gitignore.Accepts("nonexistent/foo").Should().BeTrue();
        }

        [Fact]
        public void AcceptShouldAcceptNestedUnignoredFilesInIgnoredDirectories()
        {
            _gitignore.Accepts("nonexistent/foo/wat").Should().BeTrue();
        }

        [Fact]
        public void DeniesShouldDenyTheGivenFilenames()
        {
            _gitignore.Denies("test.swp").Should().BeTrue();
            _gitignore.Denies("node_modules/wat.js").Should().BeTrue();
            _gitignore.Denies("foo/bar.wat").Should().BeTrue();
            _gitignoreNoNegatives.Denies("node_modules/wat.js").Should().BeTrue();
        }

        [Fact]
        public void DeniesShouldNotDenyTheGivenFilenames()
        {
            _gitignore.Denies("test/index.js").Should().BeFalse();
            _gitignore.Denies("wat/test/index.js").Should().BeFalse();
            _gitignore.Denies("test1File.wat").Should().BeFalse();
            _gitignoreNoNegatives.Denies("test/index.js").Should().BeFalse();
            _gitignoreNoNegatives.Denies("wat/test/index.js").Should().BeFalse();
        }

        [Fact]
        public void DeniesShouldDenyTheGivenDirectory()
        {
            _gitignore.Denies("nonexistent").Should().BeTrue();
            _gitignore.Denies("test1").Should().BeTrue();
            _gitignore.Denies("nonexistent/bar").Should().BeTrue();
            _gitignoreNoNegatives.Denies("node_modules").Should().BeTrue();
            _gitignoreNoNegatives.Denies("node_modules/foo").Should().BeTrue();
        }

        [Fact]
        public void DeniesShouldNotDenyUnignoredFilesInIgnoredDirectories()
        {
            _gitignore.Denies("nonexistent/foo").Should().BeFalse();
        }

        [Fact]
        public void DeniesShouldNotDenyNestedUnignoredFilesInIgnoredDirectories()
        {
            _gitignore.Denies("nonexistent/foo/wat").Should().BeFalse();
        }
    }
}
