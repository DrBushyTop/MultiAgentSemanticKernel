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
        var ops = new OpsPlugin();
        ops.SeedService("catalog", version: "1.3.2", p95Ms: 420, errorRate: 0.112, owners: new[] { "@team-catalog" });
        var opsPlugin =
            KernelPluginFactory.CreateFromObject(ops, pluginName: null, loggerFactory: kernel.LoggerFactory);
        kernel.Plugins.Add(opsPlugin);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            var defaultPrompt =
                "Stabilize error budget for service 'catalog' given elevated p95 and 5xx. Keep stakeholders updated with the actions taken and the results.";
            logger.LogWarning("[Magentic] Using default prompt: {Prompt}", defaultPrompt);
            prompt = defaultPrompt;
        }
        else
        {
            logger.LogWarning("[Magentic] Starting with prompt: {Prompt}", prompt);
        }

        var deployInspector = AgentUtils.Create(name: "DeployInspector",
            description: "Correlates recent deploys with regressions and notable changes.",
            instructions:
            "Compare last deploys to spot regressions. Use Deploy_Status(service) and Deploy_Diff(prevVersion) to summarize. Do not post to comms or initiate rollbacks, only report",
            kernel: kernel);
        var roller = AgentUtils.Create(name: "Roller",
            description: "Drafts rollback plan and safety checks if mitigation fails.",
            instructions:
            "Plan rollback if needed. Use Deploy_Rollback(service, toVersion?) to outline steps and risks. Do not notify comms via tools.",
            kernel: kernel);
        var notifier = AgentUtils.Create(name: "Notifier",
            description: "Prepares concise incident updates for stakeholder communications.",
            instructions:
            "Post incident summary to comms. Use Comms_Post(channel, message) to share updates. Do nothing else other than notify.",
            kernel: kernel);

        var responseCallback = AgentResponseCallbacks.Create(cli);

        var manager = new StandardMagenticManager(
            kernel.GetRequiredService<IChatCompletionService>(),
            new OpenAIPromptExecutionSettings())
        {
            MaximumInvocationCount = 25,
        };
        var orchestration = new MagenticOrchestration(manager, deployInspector, roller, notifier)
        {
            LoggerFactory = kernel.LoggerFactory,
            ResponseCallback = responseCallback,
        };

        var runtime = new InProcessRuntime();
        await runtime.StartAsync();

        var result = await orchestration.InvokeAsync(prompt, runtime);
        var output = await result.GetValueAsync(TimeSpan.FromSeconds(120));

        cli.Info("####### RESULT #######\n");
        cli.Info(output);

        await runtime.RunUntilIdleAsync();
    }
}