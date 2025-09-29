using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents.Orchestration.GroupChat;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Runners;

public sealed class GroupChatRunner(Kernel kernel, ILogger<GroupChatRunner> logger, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = "Proposed change: Move session state to Azure Cache for Redis Enterprise, SKU E3. Constraints: cost cap, rollout safety.";
        }
        logger.LogInformation("[Runner] GroupChat");
        cli.UserInput(prompt);

        var techLead = AgentUtils.Create(
            name: "TechLead",
            description: "Balances scope, complexity, and delivery approach; proposes rollout strategy.",
            instructions: "Assess feasibility and complexity; propose deployment approach. Be concise.",
            kernel: kernel);

        var sre = AgentUtils.Create(
            name: "SRE",
            description: "Evaluates reliability, SLO/SLA impact, rollout safeguards, and ops risk.",
            instructions: "Evaluate reliability, error budget, rollout, and operational risk. Be concise.",
            kernel: kernel);

        var security = AgentUtils.Create(
            name: "Security",
            description: "Reviews threat model, secrets/egress handling, and key security risks.",
            instructions: "Assess threat model, secrets handling, egress. Be concise.",
            kernel: kernel);

        var dataEng = AgentUtils.Create(
            name: "DataEng",
            description: "Covers schema changes, migration plan, data quality and footprint.",
            instructions: "Cover schema, migration strategy, data implications. Be concise.",
            kernel: kernel);

        var manager = new RoundRobinGroupChatManager
        {
            MaximumInvocationCount = 5,
            // InteractiveCallback = SomeFunc(),
        };
        var orchestration = new GroupChatOrchestration(manager, techLead, sre, security, dataEng)
        {
            LoggerFactory = kernel.LoggerFactory,
            ResponseCallback = AgentResponseCallbacks.Create(cli),
            // InputTransform...
            // ResultTransform...
        };

        var runtime = new InProcessRuntime();
        await runtime.StartAsync();

        var result = await orchestration.InvokeAsync(prompt, runtime);
        var output = await result.GetValueAsync(TimeSpan.FromSeconds(120));
        cli.RunnerResult(output);

        await runtime.RunUntilIdleAsync();
    }
}