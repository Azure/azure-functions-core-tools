# prompt2func Command Documentation

## Overview

The `prompt2func` command integrates AI-powered function generation into the Azure Functions Core Tools. This command allows developers to create Azure Functions using natural language prompts, leveraging the prompt2func tool from the azure-functions-generator project.

## Prerequisites

Before using the `prompt2func` command, you must set up the prompt2func tool:

### Step 1: Clone the Repository
```bash
git clone https://github.com/devdiv-microsoft/azure-functions-generator.git
cd azure-functions-generator
git checkout feature/leo-branch
```

### Step 2: Install the CLI Tool
```bash
cd client-app
npm install
npm run build
```

### Step 3: Environment Configuration
Create a `.env` file in the `client-app/` directory:
```
AZURE_FUNCTION_URL=https://your-function-app.azurewebsites.net
AZURE_FUNCTION_KEY=your-function-key
```

**Configuration Notes:**
- Replace `your-function-app` with the actual Azure Function App name
- Replace `your-function-key` with the actual function key from the Azure portal

## Usage

### Quick Mode
Use quick mode to bypass interactive prompts and generate functions directly:

```bash
func prompt2func --quick -p "Make a C# HTTP trigger" -r csharp -o my-function
```

### Interactive Mode
Run without parameters for interactive mode:

```bash
func prompt2func
```

### API Mode
For full JSON output with MCP and RAG sources, provide Azure Function details:

```bash
func prompt2func --azure-function-url https://your-app.azurewebsites.net --azure-function-key your-key
```

## Command Options

| Option | Short | Description | Required |
|--------|-------|-------------|----------|
| `--prompt` | `-p` | The prompt describing the function to create | Yes (quick mode) |
| `--runtime` | `-r` | The runtime for the function (e.g., csharp, python, javascript) | Yes (quick mode) |
| `--output` | `-o` | Output directory for the generated function | Yes (quick mode) |
| `--quick` | | Use quick mode to bypass interactive prompts | No |
| `--azure-function-url` | | Azure Function URL for API calls | No |
| `--azure-function-key` | | Azure Function Key for authentication | No |
| `--max-iterations` | | Maximum number of evaluation loops (default: 3) | No |
| `--enable-self-evaluation` | | Enable self-evaluation mode (default: true) | No |

## Examples

### Example 1: Create a C# HTTP Trigger
```bash
func prompt2func --quick -p "Create a HTTP trigger that returns the current time" -r csharp -o time-function
```

### Example 2: Create a Python Timer Function
```bash
func prompt2func --quick -p "Create a timer function that runs every 5 minutes and logs a message" -r python -o timer-function
```

### Example 3: Interactive Mode with API Integration
```bash
func prompt2func --azure-function-url https://my-generator.azurewebsites.net --azure-function-key abc123
```

## API Integration

When using the `--azure-function-url` and `--azure-function-key` options, the command will make API calls to the Azure Function with the following JSON payload:

```json
{
  "Prompt": "Create a function that returns the current time",
  "max_iterations": 3,
  "runtime": "python",
  "enable_self_evaluation": true
}
```

This mode provides full JSON output with MCP (Model Context Protocol) and RAG (Retrieval-Augmented Generation) sources for enhanced function generation.

## Error Handling

The command includes comprehensive error handling:

- **Missing prompt2func tool**: Displays setup instructions
- **Missing required parameters**: Shows validation errors
- **API failures**: Displays HTTP status and error details
- **Process failures**: Shows exit codes and error messages

## Integration with Existing Workflow

The `prompt2func` command integrates seamlessly with existing Azure Functions CLI workflows:

1. Use `func init` to initialize a function app
2. Use `func prompt2func` to generate functions with AI
3. Use `func start` to run and test your functions
4. Use `func azure functionapp publish` to deploy

## Troubleshooting

### Common Issues

1. **"prompt2func tool not found"**
   - Complete the prerequisite setup steps
   - Ensure npm dependencies are installed
   - Verify the prompt2func tool is accessible via `npx prompt2func`

2. **"API call failed"**
   - Check your Azure Function URL and key
   - Verify the Azure Function is running and accessible
   - Ensure your .env file is properly configured

3. **"Quick mode validation errors"**
   - Ensure all required parameters are provided: --prompt, --runtime, --output
   - Check that runtime values are valid (csharp, python, javascript, etc.)

## Contributing

To contribute to the prompt2func integration:

1. Make changes to `src/Cli/func/Actions/LocalActions/Prompt2FuncAction.cs`
2. Build the project: `dotnet build src/Cli/func/Azure.Functions.Cli.csproj`
3. Test using the built executable in `out/bin/Azure.Functions.Cli/debug/func.exe`
