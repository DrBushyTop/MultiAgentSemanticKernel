using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Orchestration;
using Microsoft.SemanticKernel.Agents.Orchestration.Sequential;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using MultiAgentSemanticKernel.Runtime;
using MultiAgentSemanticKernel.Plugins;

namespace MultiAgentSemanticKernel.Runners;

public sealed class SequentialRunner(Kernel kernel, ILogger<SequentialRunner> logger, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {
        // Import only development workflow tools for this runner
        kernel.ImportPluginFromType<DevWorkflowPlugin>();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            var defaultPrompt = "As a user, I can upload avatars up to 2MB. Add 3 acceptance criteria.";
            logger.LogWarning("[Sequential] Using default prompt: {Prompt}", defaultPrompt);
            prompt = defaultPrompt;
        }
        else
        {
            logger.LogWarning("[Sequential] Starting with prompt: {Prompt}", prompt);
        }

        var backlogRefiner = AgentUtils.Create(
            name: "BacklogRefiner",
            description: "Transforms a raw requirement into a crisp user story with INVEST attributes and acceptance criteria.",
            instructions: "Rewrite as INVEST story and produce acceptance criteria. Keep AC structured JSON.",
            kernel: kernel);
        var apiDesigner = AgentUtils.Create(
            name: "APIDesigner",
            description: "Outlines REST endpoints and request/response schemas at a high level.",
            instructions: "Propose endpoints and contracts briefly. Generate OpenAPI using tool Oas_Generate(story, acceptanceJson) where possible; include the YAML.",
            kernel: kernel);
        var scaffolder = AgentUtils.Create(
            name: "Scaffolder",
            description: "Suggests initial project structure, layers, and TODOs to get started.",
            instructions: "Describe the service skeleton and TODOs. Create a working branch via tool Repo_CreateBranch(name) and include commands.",
            kernel: kernel);
        var testWriter = AgentUtils.Create(
            name: "TestWriter",
            description: "Derives unit tests and contract tests using the acceptance criteria and API surface.",
            instructions: "Propose unit and contract tests from AC+OAS. Use tool Tests_Generate(openapiYaml, acceptanceJson) to suggest files.",
            kernel: kernel);
        var docWriter = AgentUtils.Create(
            name: "DocWriter",
            description: "Produces concise documentation: README and endpoint overview for quick onboarding.",
            instructions: "Draft README + endpoint docs summary. Use Docs_Update(branch, summary) to open a PR when appropriate.",
            kernel: kernel);

        ValueTask ResponseCallback(ChatMessageContent response)
        {
            var author = string.IsNullOrWhiteSpace(response.AuthorName) ? "Agent" : response.AuthorName;
            cli.AgentResult(author!, response.Content ?? string.Empty);
            return ValueTask.CompletedTask;
        }

        var orchestration = new SequentialOrchestration(backlogRefiner, apiDesigner, scaffolder, testWriter, docWriter)
        {
            LoggerFactory = kernel.LoggerFactory,
            ResponseCallback = ResponseCallback,
        };

        var runtime = new InProcessRuntime();
        await runtime.StartAsync();

        var result = await orchestration.InvokeAsync(prompt, runtime);
        var output = await result.GetValueAsync(TimeSpan.FromSeconds(300));

        cli.Info("# RESULT");
        cli.Info(output);

        await runtime.RunUntilIdleAsync();
    }
}