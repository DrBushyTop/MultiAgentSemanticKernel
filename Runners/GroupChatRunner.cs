using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Orchestration;
using Microsoft.SemanticKernel.Agents.Orchestration.GroupChat;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using MultiAgentSemanticKernel.Runtime;
using MultiAgentSemanticKernel.Plugins;

namespace MultiAgentSemanticKernel.Runners;

public sealed class GroupChatRunner(Kernel kernel, ILogger<GroupChatRunner> logger, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {
        // Import ops tools as they are relevant for architecture/rollout discussions
        kernel.ImportPluginFromType<OpsPlugin>();

        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = "Proposed change: Move session state to Azure Cache for Redis Enterprise, SKU E3. Constraints: cost cap, rollout safety.";
            logger.LogWarning("[GroupChat] Using default prompt: {Prompt}", prompt);
        }
        else
        {
            logger.LogWarning("[GroupChat] Starting with prompt: {Prompt}", prompt);
        }

        var techLead = AgentUtils.Create(
            name: "TechLead",
            description: "Balances scope, complexity, and delivery approach; proposes rollout strategy.",
            instructions: "Assess feasibility and complexity; propose deployment approach. When discussing deploys/flags/comms, use Deploy_Status(service), Deploy_Diff(prev), FeatureFlags_Get(key), Comms_Post(channel,message). Only respond with the result, no fluff, be concise.",
            kernel: kernel);
        var sre = AgentUtils.Create(
            name: "SRE",
            description: "Evaluates reliability, SLO/SLA impact, rollout safeguards, and ops risk.",
            instructions: "Evaluate reliability, error budget, rollout, and operational risk. Query Deploy_Status(service) to reason about current state. Only respond with the result, no fluff, be concise.",
            kernel: kernel);
        var security = AgentUtils.Create(
            name: "Security",
            description: "Reviews threat model, secrets/egress handling, and key security risks.",
            instructions: "Assess threat model, secrets handling, egress. If relevant, consider FeatureFlags_Get(key) impacts. Only respond with the result, no fluff, be concise.",
            kernel: kernel);
        var dataEng = AgentUtils.Create(
            name: "DataEng",
            description: "Covers schema changes, migration plan, data quality and footprint.",
            instructions: "Cover schema, migration strategy, data implications. Coordinate comms using Comms_Post(channel,message) for migration notices. Only respond with the result, no fluff, be concise.",
            kernel: kernel);

        ValueTask ResponseCallback(ChatMessageContent response)
        {
            var author = string.IsNullOrWhiteSpace(response.AuthorName) ? "Agent" : response.AuthorName;
            cli.AgentResult(author!, response.Content ?? string.Empty);
            return ValueTask.CompletedTask;
        }

        var manager = new RoundRobinGroupChatManager { MaximumInvocationCount = 5 };
        var orchestration = new GroupChatOrchestration(manager, techLead, sre, security, dataEng)
        {
            LoggerFactory = kernel.LoggerFactory,
            ResponseCallback = ResponseCallback,
        };

        var runtime = new InProcessRuntime();
        await runtime.StartAsync();

        var result = await orchestration.InvokeAsync(prompt, runtime);
        var output = await result.GetValueAsync();
        //var output = await result.GetValueAsync(TimeSpan.FromSeconds(120));

        cli.Info("# RESULT");
        cli.Info(output);

        await runtime.RunUntilIdleAsync();
    }
}


