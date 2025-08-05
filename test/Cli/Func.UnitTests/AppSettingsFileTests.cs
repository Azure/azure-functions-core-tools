// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Azure.Functions.Cli.UnitTests;

public class AppSettingsFileTests : IDisposable
{
    private readonly string _tempFilePath;

    public AppSettingsFileTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
    }

    [Fact]
    public void Ctor_WithValidJson_LoadsAllProperties()
    {
        // Arrange
        var json = @"
            {
              ""IsEncrypted"": false,
              ""Values"": {
                ""MySecret"": ""PlainTextValue""
              },
              ""ConnectionStrings"": {
                ""MyConn"": {
                  ""ConnectionString"": ""Server=.;Database=MyDb;"",
                  ""ProviderName"": ""System.Data.SqlClient""
                }
              },
              ""Host"": {}
            }";
        File.WriteAllText(_tempFilePath, json);

        // Act
        var settings = new AppSettingsFile(_tempFilePath);

        // Assert
        Assert.False(settings.IsEncrypted);
        Assert.Single(settings.Values);
        Assert.Equal("PlainTextValue", settings.Values["MySecret"]);

        Assert.Single(settings.ConnectionStrings);
        var token = settings.ConnectionStrings["MyConn"];
        Assert.Equal(JTokenType.Object, token.Type);
        Assert.Equal("Server=.;Database=MyDb;", token["ConnectionString"]?.ToString());
        Assert.Equal("System.Data.SqlClient", token["ProviderName"]?.ToString());

        Assert.NotNull(settings.Host);
    }

    [Fact]
    public void Ctor_WithInvalidJson_FallsBackToDefaults()
    {
        // Arrange: write syntactically invalid JSON
        File.WriteAllText(_tempFilePath, "{ this is : not valid JSON }");

        // Act
        var settings = new AppSettingsFile(_tempFilePath);

        // Assert: constructor should catch the JsonException and reset
        Assert.True(settings.IsEncrypted, "Defaults to encrypted");
        Assert.Empty(settings.Values);
        Assert.Empty(settings.ConnectionStrings);
        Assert.Null(settings.Host);
    }

    [Fact]
    public void Ctor_WithMissingFile_FallsBackToDefaults()
    {
        // Arrange: ensure the file does not exist
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }

        // Act
        var settings = new AppSettingsFile(_tempFilePath);

        // Assert: missing‐file (FileNotFoundException) is also caught
        Assert.True(settings.IsEncrypted, "Defaults to encrypted");
        Assert.Empty(settings.Values);
        Assert.Empty(settings.ConnectionStrings);
        Assert.Null(settings.Host);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }
}
