# Multi-Agent Demo

A .NET 10 CLI demonstration of multi-agent orchestration patterns using **Microsoft Agent Framework** and **Semantic Kernel**.
**NOTE:** Semantic Kernel implementation is in the `semanticKernel` branch.

## Prerequisites

- .NET 10.0 SDK
- Azure OpenAI deployment
- Azure CLI (for authentication via `DefaultAzureCredential`)

## Configuration

Set up `appsettings.json`:

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "Deployments": {
      "Llm": "gpt-4o"
    }
  }
}
```

Environment overrides (prefix `MASKE_`):

- `MASKE_AzureOpenAI__Endpoint`
- `MASKE_AzureOpenAI__Deployments__Llm`

## Running

```bash
# Build and run
dotnet build
dotnet run -- <mode> [prompt...]

# Sequential pipeline (dev workflow)
dotnet run -- sequential "Create a REST API for user management"

# Concurrent analysis (PR review)
dotnet run -- concurrent "Analyze PR #123"

# Group chat (architecture discussion)
dotnet run -- groupchat "Design a caching strategy"

# Handoff (dev triage)
dotnet run -- handoff "Fix the login bug"

# Magentic (incident response)
dotnet run -- magentic "High error rate on catalog service"
```

## Orchestration Patterns

1. **Sequential**: Agents execute in order, each receiving the previous agent's output

   - BacklogRefiner → Scaffolder → APIDesigner → TestWriter → DocWriter

2. **Concurrent**: Agents execute in parallel on the same input

   - DiffAnalyst, TestImpactor, SecLint, Compliance analyze PR simultaneously

3. **GroupChat**: Agents discuss in round-robin turns

   - TechLead, SRE, Security, DataEngineer discuss architecture decisions

4. **Handoff**: Dynamic routing between specialist agents

   - TriageAgent routes to DesignAgent or ImplementationAgent based on request

5. **Magentic**: Manager-driven loop with planning and evaluation
   - Manager coordinates DeployInspector, Deployer, Notifier for incident response

## Architecture

- **Runtime/AgentFactory.cs**: Creates `ChatClientAgent` instances with consistent configuration
- **Runtime/WorkflowRunner.cs**: Shared execution logic with event streaming and console output
- **Runners/\*.cs**: Orchestration-specific implementations
- **Plugins/\*.cs**: Tool definitions using `AIFunctionFactory.Create()` with `[Description]` attributes

## Technologies

- Microsoft Agent Framework (`Microsoft.Agents.AI.OpenAI`, `Microsoft.Agents.AI.Workflows`)
- Azure OpenAI via `IChatClient`
- .NET Dependency Injection

## Notes

- If you don't have valid Azure OpenAI configuration, calls will fail at runtime
- Console output shows agent activity and tool calls with ANSI colors
- Each runner has a sensible default prompt if none is provided
