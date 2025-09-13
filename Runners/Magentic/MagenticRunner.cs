using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Orchestration;
using Microsoft.SemanticKernel.Agents.Magentic;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Runners;

public sealed class MagenticRunner(Kernel kernel, ILogger<MagenticRunner> logger, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            var defaultPrompt = "Stabilize error budget for service 'catalog' given elevated p95 and 5xx.";
            logger.LogWarning("[Magentic] Using default prompt: {Prompt}", defaultPrompt);
            prompt = defaultPrompt;
        }
        else
        {
            logger.LogWarning("[Magentic] Starting with prompt: {Prompt}", prompt);
        }

        var logSearcher = new ChatCompletionAgent { Name = "LogSearcher", Instructions = "Use logs to find recent errors and anomalies.", Kernel = kernel };
        var deployInspector = new ChatCompletionAgent { Name = "DeployInspector", Instructions = "Compare last deploys to spot regressions.", Kernel = kernel };
        var flagger = new ChatCompletionAgent { Name = "FeatureFlagger", Instructions = "Get/set feature flags to mitigate.", Kernel = kernel };
        var roller = new ChatCompletionAgent { Name = "Roller", Instructions = "Plan rollback if needed.", Kernel = kernel };
        var notifier = new ChatCompletionAgent { Name = "Notifier", Instructions = "Post incident summary to comms.", Kernel = kernel };

        ValueTask ResponseCallback(ChatMessageContent response)
        {
            var author = string.IsNullOrWhiteSpace(response.AuthorName) ? "Agent" : response.AuthorName;
            cli.AgentResult(author!, response.Content ?? string.Empty);
            return ValueTask.CompletedTask;
        }

        var manager = new StandardMagenticManager(
            kernel.GetRequiredService<IChatCompletionService>(),
            new OpenAIPromptExecutionSettings())
        {
            MaximumInvocationCount = 6,
        };
        var orchestration = new MagenticOrchestration(manager, logSearcher, deployInspector, flagger, roller, notifier)
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


