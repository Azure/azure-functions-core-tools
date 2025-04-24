# Azure Functions Core Tools E2E Testing Guide

## Overview

This project contains the E2E tests for Azure Functions Core Tools. When adding a new E2E test, please follow these guidelines to ensure consistency and efficient test execution.

### Test Organization

- Create tests within `Commands/Func[COMMAND_NAME]` directories
- Separate tests into categories by organizing them in files with similar tests
  - Examples: `AuthTests`, `LogLevelTests`, etc.
- This organization allows tests in different files to run in parallel, improving execution speed

### Test Types for `func start`

There are two main types of tests for the `func start` command:

#### 1. Tests with Fixtures

- Based on `BaseFunctionAppFixture`
- The fixture handles initialization (`func init` and `func new`)
- Best for tests that require a simple `HttpTrigger` with no additional configuration
- Add your test to the appropriate fixture to avoid setup overhead

#### 2. Tests without Fixtures

- Based on `BaseE2ETests`
- Sets up variables like `WorkingDirectory` and `FuncPath`
- You must handle application setup in each test
- Use the provided helper methods `FuncInitWithRetryAsync` and `FuncNewWithRetryAsync` for setup

### Important Notes

### Test Parallelization

❗ **Dotnet isolated templates and dotnet in-proc templates CANNOT be run in parallel**

- Use traits to distinguish between dotnet-isolated and dotnet-inproc tests
- Refer to TestTraits.cs for trait names and explanations on when they should be used
- This ensures they run sequentially rather than in parallel

### Node.js Tests

⚠️ **Node.js tests require explicit environment variable configuration**

- Due to flakiness in `func init` and `func new` not properly initializing environment variables
- Always append `.WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "node")` to the `funcStartCommand` like this:

```csharp
var result = funcStartCommand
    .WithWorkingDirectory(WorkingDirectory)
    .WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "node")
    .Execute(commandArgs.ToArray());
```

## Step-by-Step Guide: Creating a `func start` Test without a Fixture

### 1. Get an Available Port

First, get an available port using `ProcessHelper.GetAvailablePort()` to ensure your test doesn't conflict with other processes:

```csharp
int port = ProcessHelper.GetAvailablePort();
```

### 2. Define Test Names

Create descriptive names for your test. For parameterized tests, include the parameters in the test name for better traceability:

```csharp
string methodName = "Start_DotnetIsolated_Test_EnableAuthFeature";
string uniqueTestName = $"{methodName}_{parameterValue1}_{parameterValue2}";
```

### 3. Initialize a Function App

Use `FuncInitWithRetryAsync` to create a new function app with the appropriate runtime:

```csharp
await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "dotnet-isolated" });
```

### 4. Add a Trigger Function

Use `FuncNewWithRetryAsync` to add a trigger function with specific configuration:

```csharp
await FuncNewWithRetryAsync(uniqueTestName, new[] { 
    ".", 
    "--template", "HttpTrigger", 
    "--name", "HttpTrigger", 
    "--authlevel", authLevel 
});
```

### 5. Create a FuncStartCommand

Initialize a `FuncStartCommand` with the path to the func executable, test name, and logger:

```csharp
var funcStartCommand = new FuncStartCommand(FuncPath, methodName, Log);
```

### 6. Add a Process Started Handler (Optional)

If you need to wait for the host to start or check logs from a different process, add a `ProcessStartedHandler`:

```csharp
funcStartCommand.ProcessStartedHandler = async (process) =>
{
    await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter, "HttpTrigger");
};
```

The `ProcessStartedHandlerHelper` method:
- Waits for the host to start
- Makes an HTTP request to the function
- Captures logs from the process
- Returns when the function has processed the request

### 7. Build Command Arguments

Build your command arguments based on test parameters:

```csharp
var commandArgs = new List<string> { "start", "--verbose", "--port", port.ToString() };
if (enableSomeFeature)
{
    commandArgs.Add("--featureFlag");
}
```

### 8. Execute the Command

Execute the command with the working directory and arguments:

```csharp
var result = funcStartCommand
    .WithWorkingDirectory(WorkingDirectory)
    .Execute(commandArgs.ToArray());
```

### 9. Validate the Results

Validate the command output contains the expected results based on test parameters:

```csharp
if (someCondition)
{
    result.Should().HaveStdOutContaining("expected output 1");
}
else
{
    result.Should().HaveStdOutContaining("expected output 2");
}
```

You may also create a custom condition and then call the method like so:

```csharp
// Validate inproc6 host was started
result.Should().StartInProc6Host();

```

## Testing with Fixtures

The steps are similar for creating a `func start` test with a fixture, except you can skip the setup logic that calls `func init` and `func new` as the fixture handles this for you. Please ensure that the test that are being added to the fixture DO NOT change the environment or add any extra variables or config, as that may cause problems with the existing tests.

## Complete Example

```csharp
public async Task Start_DotnetIsolated_Test_EnableAuthFeature(
    string authLevel,
    bool enableAuth,
    string expectedResult)
{
    int port = ProcessHelper.GetAvailablePort();
    string methodName = "Start_DotnetIsolated_Test_EnableAuthFeature";
    string uniqueTestName = $"{methodName}_{authLevel}_{enableAuth}";
    
    // Setup the function app
    await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "dotnet-isolated" });
    await FuncNewWithRetryAsync(uniqueTestName, new[] { ".", "--template", "HttpTrigger", "--name", "HttpTrigger", "--authlevel", authLevel });
    
    // Create and configure the start command
    var funcStartCommand = new FuncStartCommand(FuncPath, methodName, Log);
    funcStartCommand.ProcessStartedHandler = async (process) =>
    {
        await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter, "HttpTrigger");
    };
    
    // Build command arguments
    var commandArgs = new List<string> { "start", "--verbose", "--port", port.ToString() };
    if (enableAuth)
    {
        commandArgs.Add("--enableAuth");
    }
    
    // Execute and validate
    var result = funcStartCommand
        .WithWorkingDirectory(WorkingDirectory)
        .Execute(commandArgs.ToArray());
    
    if (string.IsNullOrEmpty(expectedResult))
    {
        result.Should().HaveStdOutContaining("\"status\": \"401\"");
    }
    else
    {
        result.Should().HaveStdOutContaining("Selected out-of-process host.");
    }
}
```

## How to Build and Run Tests Locally

1. Run `dotnet build` on `Azure.Functions.Cli.E2E.Tests.csproj`.

2. Navigate to `CORE_TOOLS_REPO_ROOT\out\bin\Azure.Functions.Cli.E2E.Tests\debug` and add a `templates` file if it doesn't already exist. There are two options for how to obtain the contents of the templates folder:
   - Copy the existing templates file from the core tools installation on your local machine to the debug directory.
   - Run the `Build.csproj` project to generate the `templates` folder within the debug directory of the build project. Copy that file over to the debug directory for the E2E tests.

3. To test in-proc artifacts, copy the `inproc6` and `inproc8` directories by either:
   - Building the `inproc` branch locally and manually copying the folders into the debug folder, or
   - Copying these folders from the core tools installation on your local machine to the debug folder.

4. Execute the tests using the `dotnet test` command or Visual Studio Test Explorer. Note that only tests requiring default artifacts (not in-proc artifacts) will run by default. 
   - To run individual tests, use the `--filter` option with the test name. For example: `dotnet test --filter FullyQualifiedName~Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.Start_MissingLocalSettingsJson_BehavesAsExpected`.
   - To filter tests by languge, use the `--filter` option with the language name. For example: `dotnet test --filter WorkerRuntime=DotnetIsolated`.

5. To run a specific test with runtime settings, use: `dotnet test {PATH_TO_E2E_TEST_PROJECT} --settings {PATH_TO_RUNSETTINGS_FILE}.`