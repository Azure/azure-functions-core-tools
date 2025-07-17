# Func Pack Test Suite

This directory contains comprehensive tests for the `func pack` command across all supported Azure Functions runtimes.

## Overview

The `func pack` command creates deployment-ready zip packages from function app projects. This test suite ensures:

1. ✅ **Runtime Coverage**: Tests all supported worker runtimes (Node.js, Python, PowerShell, Java, .NET in-process, .NET isolated, Custom)
2. ✅ **Output Validation**: Verifies zip files are created in correct locations with expected contents
3. ✅ **Ignore Functionality**: Tests `.funcignore` file behavior for excluding files from packages
4. ✅ **Edge Cases**: Handles error scenarios, file conflicts, and various command options
5. ✅ **Extensibility**: Provides framework for easily adding new runtime tests and scenarios

## Test Files

### Core Test Classes

- **`ComprehensivePackTests.cs`** - Main test suite covering all runtimes with core scenarios:
  - Default output path behavior
  - Custom output path specification 
  - `.funcignore` file handling
  - Output directory creation
  - Existing file replacement

- **`DotnetPackTests.cs`** - .NET-specific tests handling special cases:
  - In-process .NET pack limitations (local builds not supported)
  - Remote build support for .NET runtimes
  - .NET isolated runtime pack behavior

- **`EnhancedPythonPackTests.cs`** - Extended Python tests beyond existing coverage:
  - Native dependency building (`--build-native-deps`)
  - Additional packages (`--additional-packages`)
  - Squashfs output format (`--squashfs`)
  - Complex `.funcignore` patterns
  - Custom requirements.txt handling

- **`AdvancedPackTests.cs`** - Advanced scenarios and edge cases:
  - Zip content validation
  - Large file set handling
  - Absolute output paths
  - Invalid function app error handling
  - Performance with many files

- **`ExtensiblePackTests.cs`** - Demonstrates extensible testing patterns:
  - Parameterized tests across all runtimes
  - Data-driven ignore pattern testing
  - Custom scenario examples
  - Helper utility usage

### Helper Framework

- **`PackTestHelpers.cs`** - Extensible utilities for maintainable pack testing:
  - `SupportedRuntimes` - Configuration for all runtime stacks
  - `StandardFuncIgnorePatterns` - Common ignore patterns for testing
  - `CreateFunctionApp()` - Setup helper for consistent test initialization
  - `ValidatePackOutput()` - Zip content validation with detailed reporting
  - `CreateTestFilesForIgnoreTesting()` - Standard test file creation

## Usage Examples

### Adding a New Runtime

To add support for a new runtime, update `PackTestHelpers.SupportedRuntimes`:

```csharp
["newruntime"] = new RuntimeConfig
{
    Runtime = "newruntime",
    SupportsLocalBuild = true,
    SupportsRemoteBuild = true,
    DefaultTemplate = "HTTP Trigger",
    ExpectedFiles = new[] { "host.json", "runtime-config.json" },
    IgnoredFiles = new[] { "local.settings.json", "cache/" }
}
```

The parameterized tests in `ExtensiblePackTests.cs` will automatically include the new runtime.

### Adding Custom Test Scenarios

Use the helper framework for consistent test setup:

```csharp
[Fact]
public void Pack_CustomScenario_Works()
{
    var setup = PackTestHelpers.CreateFunctionApp(FuncPath, WorkingDirectory, "node", testName, Log);
    
    // Custom setup...
    
    var packResult = new FuncPackCommand(FuncPath, testName, Log)
        .WithWorkingDirectory(WorkingDirectory)
        .Execute(["--custom-option"]);
        
    var validation = PackTestHelpers.ValidatePackOutput(zipPath, setup.Config, Log);
    Assert.True(validation.IsValid);
}
```

### Testing New Ignore Patterns

Add patterns to `StandardFuncIgnorePatterns` in `PackTestHelpers.cs`:

```csharp
["newpattern"] = @"*.newext
newdir/
!important.newext
"
```

## Runtime-Specific Behavior

### Node.js
- ✅ Supports local and remote builds
- ✅ Includes `package.json`, excludes `node_modules/`
- ✅ Standard ignore patterns for JS/TS development

### Python  
- ✅ Supports native dependency building
- ✅ Supports Squashfs output format
- ✅ Handles `requirements.txt` and `.python_packages/` caching
- ✅ Excludes `__pycache__/`, `.venv/`, virtual environments

### .NET In-Process
- ⚠️ **Local builds not supported** - throws expected error
- ✅ Remote builds work correctly
- ✅ Excludes `bin/`, `obj/` directories

### .NET Isolated
- ✅ Supports local builds with `--no-build`
- ✅ Supports remote builds
- ✅ Standard .NET ignore patterns

### PowerShell
- ✅ Supports local and remote builds
- ✅ Includes `profile.ps1`
- ✅ Standard script ignore patterns

### Java
- ✅ Supports local and remote builds  
- ✅ Includes `pom.xml`, excludes `target/`
- ✅ Maven-specific ignore patterns

### Custom Runtime
- ✅ Flexible configuration support
- ✅ Minimal required files (just `host.json`)
- ✅ Configurable ignore patterns

## Test Categories

Tests are organized using xUnit traits:

- `[Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]` - Runtime-specific tests
- `[Theory]` with `[MemberData]` - Parameterized tests across runtimes
- `[Fact]` - Individual scenario tests

Run specific categories:
```bash
dotnet test --filter "WorkerRuntime=Node"
dotnet test --filter "WorkerRuntime=Python" 
dotnet test --filter "FullyQualifiedName~PackTests"
```

## Integration with CI/CD

These tests integrate with the existing Azure Functions Core Tools CI pipeline:

- Uses existing `BaseE2ETests` infrastructure
- Follows established test patterns and assertions
- Compatible with existing build and test scripts
- Supports parallel execution across runtime categories

## Future Enhancements

### Stretch Goals (Commented Examples)
- **Azure CLI Integration**: End-to-end deployment verification
- **Performance Benchmarks**: Pack time and size measurements  
- **Binary Content Validation**: Deep zip content analysis
- **Multi-platform Testing**: Windows/Linux/macOS compatibility

The helper framework makes these additions straightforward to implement.