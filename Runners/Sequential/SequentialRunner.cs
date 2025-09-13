using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Orchestration;
using Microsoft.SemanticKernel.Agents.Orchestration.Sequential;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Runners;

public sealed class SequentialRunner(Kernel kernel, ILogger<SequentialRunner> logger, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {
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

        var backlogRefiner = new ChatCompletionAgent
        {
            Name = "BacklogRefiner",
            Instructions = "Rewrite as INVEST story and produce acceptance criteria.",
            Kernel = kernel
        };
        var apiDesigner = new ChatCompletionAgent
        {
            Name = "APIDesigner",
            Instructions = "Propose endpoints and contracts briefly.",
            Kernel = kernel
        };
        var scaffolder = new ChatCompletionAgent
        {
            Name = "Scaffolder",
            Instructions = "Describe the service skeleton and TODOs.",
            Kernel = kernel
        };
        var testWriter = new ChatCompletionAgent
        {
            Name = "TestWriter",
            Instructions = "Propose unit and contract tests from AC+OAS.",
            Kernel = kernel
        };
        var docWriter = new ChatCompletionAgent
        {
            Name = "DocWriter",
            Instructions = "Draft README + endpoint docs summary.",
            Kernel = kernel
        };

        ValueTask ResponseCallback(ChatMessageContent response)
        {
            var author = string.IsNullOrWhiteSpace(response.AuthorName) ? "Agent" : response.AuthorName;
            cli.AgentResult(author!, response.Content ?? string.Empty);
            return ValueTask.CompletedTask;
        }

        var orchestration = new SequentialOrchestration(backlogRefiner, apiDesigner, scaffolder, testWriter, docWriter)
        {
            ResponseCallback = ResponseCallback,
        };

        var runtime = new InProcessRuntime();
        await runtime.StartAsync();

        var result = await orchestration.InvokeAsync(prompt, runtime);
        var output = await result.GetValueAsync(TimeSpan.FromSeconds(120));

        cli.Info("# RESULT");
        cli.Info(output);

        await runtime.RunUntilIdleAsync();
    }
}