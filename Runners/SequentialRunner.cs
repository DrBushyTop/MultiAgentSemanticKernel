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
        if (string.IsNullOrWhiteSpace(prompt))
        {
            var defaultPrompt = "Story: 'As a user, I can upload avatars up to 2MB. Add 3 acceptance criteria.' Write specs and set up a new branch so I can get started.";
            prompt = defaultPrompt;
        }
        logger.LogInformation("[Runner] Sequential");
        cli.UserInput(prompt);

        kernel.ImportPluginFromType<DevWorkflowPlugin>();
        var nopluginKernel = kernel.Clone();
        nopluginKernel.Plugins.Clear();
        var backlogRefiner = AgentUtils.Create(
            name: "BacklogRefiner",
            description: "Transforms a raw requirement into a crisp user story with INVEST attributes and acceptance criteria.",
            instructions: "Rewrite as INVEST story and produce acceptance criteria. Keep acceptance criteria as JSON format. Be concise, no fluff",
            kernel: nopluginKernel);
        var scaffolder = AgentUtils.Create(
            name: "Scaffolder",
            description: "Suggests initial project structure, layers, and TODOs to get started.",
            instructions: "Describe the service skeleton and TODOs. Create a working branch via tool Repo_CreateBranch(name) and Scaffold(branch). Do not overreach your responsibilities. Be concise, no fluff",
            kernel: kernel);
        var apiDesigner = AgentUtils.Create(
            name: "APIDesigner",
            description: "Outlines REST endpoints and request/response schemas at a high level.",
            instructions: "Propose endpoints and contracts briefly. Always generate OpenAPI using tool Oas_Generate(story, acceptanceJson); include the YAML. Do not overreach your responsibilities. Be concise, no fluff",
            kernel: kernel);
        var testWriter = AgentUtils.Create(
            name: "TestWriter",
            description: "Derives unit tests and contract tests using the acceptance criteria and API surface.",
            instructions: "Propose unit and contract tests from AC+OAS. Use tool Tests_Generate(openapiYaml, acceptanceJson) to generate files in repo. Do not overreach your responsibilities. Be concise, no fluff",
            kernel: kernel);
        var docWriter = AgentUtils.Create(
            name: "DocWriter",
            description: "Produces concise documentation: README and endpoint overview for quick onboarding.",
            instructions: "Draft README + endpoint docs summary. Always use tool Docs_Update(branch, summary) to open a PR when done. Do not overreach your responsibilities. Be concise, no fluff",
            kernel: kernel);

        var responseCallback = AgentResponseCallbacks.Create(cli);

        var orchestration = new SequentialOrchestration(backlogRefiner, scaffolder, apiDesigner, testWriter, docWriter)
        {
            LoggerFactory = kernel.LoggerFactory,
            ResponseCallback = responseCallback,
        };

        var runtime = new InProcessRuntime();
        await runtime.StartAsync();

        var result = await orchestration.InvokeAsync(prompt, runtime);
        var output = await result.GetValueAsync(TimeSpan.FromSeconds(300));
        cli.RunnerResult(output);

        await runtime.RunUntilIdleAsync();
    }
}