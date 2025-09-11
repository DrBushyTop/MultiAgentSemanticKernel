# MultiAgentSemanticKernel

A .NET 9 console demo app for Semantic Kernel multi-agent orchestration patterns. It scaffolds the base with DI, options, Azure OpenAI via DefaultAzureCredential, a console function-invocation filter, and stubs for orchestration modes. You will implement the actual orchestration later.

## Requirements

- .NET SDK 9
- Access to Azure OpenAI (for real runs)
- Auth via DefaultAzureCredential (e.g., Azure CLI login, Managed Identity, or Visual Studio/VS Code sign-in)

## Setup

```bash
# Restore, build
dotnet restore
dotnet build
```

## Configuration

Configuration uses the Options pattern bound from `appsettings.json` and environment variables.

- File: `appsettings.json`

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://YOUR-AOAI-ENDPOINT.openai.azure.com/",
    "Deployments": {
      "Llm": "your-llm-deployment",
      "Embeddings": "your-embeddings-deployment"
    }
  }
}
```

- Environment overrides (prefix `MASKE_`):
  - `MASKE_AzureOpenAI__Endpoint`
  - `MASKE_AzureOpenAI__Deployments__Llm`
  - `MASKE_AzureOpenAI__Deployments__Embeddings`

## Running

First argument is the mode. Remaining args are treated as the prompt (optional).

```bash
# Examples
dotnet run -- Sequential "Say hello from the demo base setup"
dotnet run -- Concurrent "Plan and summarize"
dotnet run -- GroupChat "Discuss pros and cons"
dotnet run -- Handoff "Break down tasks and delegate"
dotnet run -- Magentic "Route this request"
```

## What’s implemented

- DI with `Host.CreateApplicationBuilder`
- Options model in `AzureOpenAIOptions`
- Semantic Kernel 1.65.0 with Azure OpenAI chat completion
- Authentication via `DefaultAzureCredential`
- Console logging and a function invocation filter that prints which function runs and its result
- Hardcoded demo plugin (`DemoPlugin`) with simple functions
- Mode stubs: `Sequential`, `Concurrent`, `GroupChat`, `Handoff`, `Magentic`

## Notes

- `SequentialRunner` currently invokes the model with the provided prompt. Others are stubs for you to fill in orchestration patterns.
- If you don’t have valid Azure OpenAI config, calls will fail at runtime.

## References

- Function invocation filtering sample: [FunctionInvocationFiltering.cs](https://github.com/microsoft/semantic-kernel/blob/main/dotnet/samples/Concepts/Filtering/FunctionInvocationFiltering.cs)
- Kernel filters design and DI: [docs/decisions/0033-kernel-filters.md](https://github.com/microsoft/semantic-kernel/blob/main/docs/decisions/0033-kernel-filters.md)
- Migration: [Kernel events and filters migration](https://learn.microsoft.com/en-us/semantic-kernel/support/migration/kernel-events-and-filters-migration)
- Filters overview: [Filters in Semantic Kernel](https://devblogs.microsoft.com/semantic-kernel/filters-in-semantic-kernel/)
