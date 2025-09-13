using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Orchestration;
using Microsoft.SemanticKernel.Agents.Magentic;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using MultiAgentSemanticKernel.Runtime;
using MultiAgentSemanticKernel.Plugins;

namespace MultiAgentSemanticKernel.Runners;

public sealed class MagenticRunner(Kernel kernel, ILogger<MagenticRunner> logger, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {
        // Import only ops tools for this runner
        kernel.ImportPluginFromType<OpsPlugin>();
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

        var logSearcher = AgentUtils.Create(name: "LogSearcher", description: "Surfaces recent error spikes and anomalous patterns from logs.", instructions: "Use logs to find recent errors and anomalies. Only respond with the result, no fluff, be concise.", kernel: kernel);
        var deployInspector = AgentUtils.Create(name: "DeployInspector", description: "Correlates recent deploys with regressions and notable changes.", instructions: "Compare last deploys to spot regressions. Use Deploy_Status(service) and Deploy_Diff(prevVersion) to summarize. Only respond with the result, no fluff, be concise.", kernel: kernel);
        var flagger = AgentUtils.Create(name: "FeatureFlagger", description: "Assesses and recommends feature flag flips to reduce impact.", instructions: "Get/set feature flags to mitigate. Use FeatureFlags_Get(key) to check flags and recommend flips. Only respond with the result, no fluff, be concise.", kernel: kernel);
        var roller = AgentUtils.Create(name: "Roller", description: "Drafts rollback plan and safety checks if mitigation fails.", instructions: "Plan rollback if needed. Use Deploy_Rollback(service, toVersion?) to outline steps and risks. Only respond with the result, no fluff, be concise.", kernel: kernel);
        var notifier = AgentUtils.Create(name: "Notifier", description: "Prepares concise incident updates for stakeholder communications.", instructions: "Post incident summary to comms. Use Comms_Post(channel, message) to share updates. Only respond with the result, no fluff, be concise.", kernel: kernel);

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
            LoggerFactory = kernel.LoggerFactory,
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


