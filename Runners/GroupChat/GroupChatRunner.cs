using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Orchestration;
using Microsoft.SemanticKernel.Agents.Orchestration.GroupChat;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Runners;

public sealed class GroupChatRunner(Kernel kernel, ILogger<GroupChatRunner> logger, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {

        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = "Proposed change: Move session state to Azure Cache for Redis Enterprise, SKU E3. Constraints: cost cap, rollout safety.";
            logger.LogWarning("[GroupChat] Using default prompt: {Prompt}", prompt);
        }
        else
        {
            logger.LogWarning("[GroupChat] Starting with prompt: {Prompt}", prompt);
        }

        var techLead = new ChatCompletionAgent { Name = "TechLead", Instructions = "Assess feasibility and complexity; propose deployment approach. Max 100 words.", Kernel = kernel };
        var sre = new ChatCompletionAgent { Name = "SRE", Instructions = "Evaluate reliability, error budget, rollout, and operational risk. Max 100 words.", Kernel = kernel };
        var security = new ChatCompletionAgent { Name = "Security", Instructions = "Assess threat model, secrets handling, egress. Max 100 words.", Kernel = kernel };
        var dataEng = new ChatCompletionAgent { Name = "DataEng", Instructions = "Cover schema, migration strategy, data implications. Max 100 words.", Kernel = kernel };

        ValueTask ResponseCallback(ChatMessageContent response)
        {
            var author = string.IsNullOrWhiteSpace(response.AuthorName) ? "Agent" : response.AuthorName;
            cli.AgentResult(author!, response.Content ?? string.Empty);
            return ValueTask.CompletedTask;
        }

        var manager = new RoundRobinGroupChatManager { MaximumInvocationCount = 5 };
        var orchestration = new GroupChatOrchestration(manager, techLead, sre, security, dataEng)
        {
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


