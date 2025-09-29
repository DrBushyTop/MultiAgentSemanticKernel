using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents.Orchestration.Handoff;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Runners;

public sealed class HandoffRunner(Kernel kernel, ILogger<HandoffRunner> logger, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {
        string task = string.IsNullOrWhiteSpace(prompt)
            ? "We need to add a dark mode feature toggle and roll it out safely. Code review is required before I make the merge call."
            : prompt;

        logger.LogInformation("[Runner] Handoff");

        // Define agents (software development pipeline)
        var triageAgent = AgentUtils.Create(
            instructions: "An engineering triage agent that routes and coordinates software development tasks (design, implementation, code review).",
            name: "DevTriageAgent",
            description: "Routes product requests into design, implementation, or code review.",
            kernel: kernel);

        var designAgent = AgentUtils.Create(
            name: "DesignAgent",
            instructions: "Handle feature design and technical specification requests.",
            description: "Creates concise technical designs and clarifies requirements.",
            kernel: kernel);
        designAgent.Kernel.ImportPluginFromObject(new DesignPlugin(), nameof(DesignPlugin));

        var implementationAgent = AgentUtils.Create(
            name: "ImplementationAgent",
            instructions: "Handle implementation tasks based on the agreed design.",
            description: "Prepares branches and change summaries, and coordinates PR creation.",
            kernel: kernel);
        implementationAgent.Kernel.ImportPluginFromObject(new ImplementationPlugin(), nameof(ImplementationPlugin));

        // Monitor and interactive responses (non-interactive console scenario)
        var responses = new Queue<string>();
        responses.Enqueue("Constraints: UI only for now");
        responses.Enqueue("Proceed to implementation");
        responses.Enqueue("Create a branch and open a PR, then let's review the code");
        responses.Enqueue("Looks good, merge it");

        // Optionally, you could use structured inputs and/or outputs here:
        // HandoffOrchestration<MyInputType, MyOutputType> = new()...
        var orchestration = new HandoffOrchestration(
            OrchestrationHandoffs
                .StartWith(triageAgent)
                .Add(triageAgent, designAgent, implementationAgent)
                .Add(designAgent, triageAgent, "Transfer to this agent if the issue is not design related or relates to implementation")
                .Add(implementationAgent, triageAgent, "Transfer to this agent if the issue is not implementation related"),
            triageAgent,
            designAgent,
            implementationAgent)
        {
            InteractiveCallback = () =>
            {
                var input = responses.Count > 0 ? responses.Dequeue() : "No, bye";
                cli.UserInput(input);
                return ValueTask.FromResult(new ChatMessageContent(AuthorRole.User, input));
            },
            LoggerFactory = kernel.LoggerFactory,
            ResponseCallback = AgentResponseCallbacks.Create(cli),
        };

        var runtime = new InProcessRuntime();
        await runtime.StartAsync();

        cli.UserInput(task);
        var result = await orchestration.InvokeAsync(task, runtime);
        var text = await result.GetValueAsync(TimeSpan.FromSeconds(300));
        cli.RunnerResult(text);

        await runtime.RunUntilIdleAsync();
    }

    private sealed class DesignPlugin
    {
        [KernelFunction]
        public string CreateDesignSummary(string title, string constraints) => $"Design for '{title}': scope, UX impact, risks, {constraints}.";
    }

    private sealed class ImplementationPlugin
    {
        [KernelFunction]
        public string PrepareBranch(string name) => $"Branch '{name}' is ready.";

        [KernelFunction]
        public string OpenPullRequest(string title) => $"Pull request created: {title}.";
    }
}