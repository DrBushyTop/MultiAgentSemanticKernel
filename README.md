# MultiAgentSemanticKernel

A .NET 9 console demo app showcasing Semantic Kernel multi‑agent orchestration patterns end‑to‑end. It wires up DI, options, Azure OpenAI via `DefaultAzureCredential`, ANSI console output, and multiple orchestration styles (sequential, concurrent, group chat, handoff, magentic).

## Requirements

- .NET SDK 9
- Azure OpenAI access (for real runs)
- Authentication via `DefaultAzureCredential` (e.g., Azure CLI login, Managed Identity, or Visual Studio/VS Code sign-in)

## Setup

```bash
# Restore and build
dotnet restore
dotnet build
```

## Configuration

Configuration uses the Options pattern bound from `appsettings.json` and environment variables (prefix `MASKE_`).

- File: `appsettings.json`

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://YOUR-AOAI-ENDPOINT.openai.azure.com/",
    "Deployments": {
      "Llm": "your-llm-deployment"
    }
  },
  "EnableAgentLogging": false
}
```

- Environment overrides (prefix `MASKE_`):
  - `MASKE_AzureOpenAI__Endpoint`
  - `MASKE_AzureOpenAI__Deployments__Llm`
  - `MASKE_EnableAgentLogging` (bool; if true, agent/orchestration logging is wired into SK)

The kernel is configured with Azure OpenAI chat completion using the provided endpoint and deployments. Credentials are sourced from `DefaultAzureCredential`.

## Running

First argument selects the runner mode. Remaining args are treated as the prompt. **If no prompt is supplied, each runner uses a sensible baked‑in default for demo purposes**.

```bash
Usage:
  dotnet run -- <Sequential|Concurrent|GroupChat|Handoff|Magentic> [prompt...]

# Examples
dotnet run -- Sequential "As a user, I can upload avatars up to 2MB."
dotnet run -- Concurrent "Analyze PR: feat(auth): add input validation and fix null handling"
dotnet run -- GroupChat "Move session state to Azure Cache for Redis Enterprise, SKU E3"
dotnet run -- Handoff "Add dark mode feature toggle and roll it out safely"
dotnet run -- Magentic "Stabilize error budget for service 'catalog'"
```

## Runners

- Sequential: Executes a deliberate pipeline of agents (BacklogRefiner, Scaffolder, APIDesigner, TestWriter, DocWriter) using Sequential Orchestration. Imports `DevWorkflowPlugin`.
- Concurrent: Runs multiple analysis agents concurrently (DiffAnalyst, TestImpactor, SecLint, Compliance). Imports `PrAnalysisPlugin`.
- GroupChat: Round‑robin group chat between TechLead, SRE, Security, and DataEng.
- Handoff: Triage, Design, and Implementation agents using explicit handoff rules and an interactive callback.
- Magentic: Ops‑focused flow (DeployInspector, Deployer, Notifier) using Magentic Manager with tools from `OpsPlugin`.

## Console UX

Console output is optimized for readability:

- Agent lifecycle and tool calls are rendered with ANSI colors and icons.
- User input is highlighted via a dedicated `UserInput` block for dark‑mode friendly contrast.
- Results are printed compactly after orchestration completes.

## What’s implemented

- DI via `Host.CreateApplicationBuilder`
- Options model in `Options/AzureOpenAIOptions.cs`
- Semantic Kernel 1.65.0 with Azure OpenAI chat completion
- SK Agents packages (preview) for orchestration patterns
- Authentication via `DefaultAzureCredential`
- Console logging, plus a function invocation filter (`ConsoleFunctionInvocationFilter`) to display tool invocations
- Plugins: `DevWorkflowPlugin`, `PrAnalysisPlugin`, `OpsPlugin` (+ `OpsInspectorTools`, `OpsDeployerTools`, `OpsNotifierTools`)

## Notes

- If you don’t have valid Azure OpenAI configuration, calls will fail at runtime.
- Some runners use longer timeouts (e.g., 120–300s) to await model output.
