using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using MultiAgentSemanticKernel.Plugins;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Runners;

public class MagenticRunner(IChatClient chatClient, ICliWriter cli)
{
    private const int MaxIterations = 10;

    public async Task RunAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = "The catalog service is showing elevated error rates. Investigate and resolve.";
        }

        cli.Header("Magentic: Ops Incident Response");
        cli.Info($"Incident: {prompt}");

        // Initialize shared state with seed data
        var opsState = new OpsState
        {
            Services =
            {
                ["catalog"] = new ServiceInfo("catalog", "1.32", 420, 0.112, ["@team-catalog"]),
            }
        };
        opsState.AvailableVersions.Add(new VersionInfo("1.31", "previous stable"));

        // Create tool instances with shared state
        var inspectorTools = new OpsInspectorTools(opsState);
        var deployerTools = new OpsDeployerTools(opsState);
        var notifierTools = new OpsNotifierTools(opsState);

        // Create specialist agents
        var inspector = AgentFactory.CreateAgent(
            chatClient,
            name: "DeployInspector",
            instructions: """
                You investigate service issues by checking status and recent deployments.
                Look for correlations between deployments and problems.
                Report your findings clearly.
                """,
            [
                AIFunctionFactory.Create(inspectorTools.GetServiceStatus),
                AIFunctionFactory.Create(inspectorTools.GetRecentDeployments),
                AIFunctionFactory.Create(inspectorTools.GetAvailableVersions)
            ]);

        var deployer = AgentFactory.CreateAgent(
            chatClient,
            name: "Deployer",
            instructions: """
                You handle deployments and rollbacks.
                Only deploy or rollback when instructed by the manager.
                Confirm actions taken.
                """,
            [
                AIFunctionFactory.Create(deployerTools.DeployService),
                AIFunctionFactory.Create(deployerTools.RollbackService)
            ]);

        var notifier = AgentFactory.CreateAgent(
            chatClient,
            name: "Notifier",
            instructions: """
                You handle communications during incidents.
                Send notifications and page on-call when needed.
                Keep stakeholders informed.
                """,
            [
                AIFunctionFactory.Create(notifierTools.SendNotification),
                AIFunctionFactory.Create(notifierTools.PageOnCall)
            ]);

        // Create manager agent with structured output for decisions
        var manager = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "Manager",
            ChatOptions = new ChatOptions
            {
                Instructions = """
                    You are an incident manager coordinating a team of specialists:
                    - DeployInspector: Investigates service status and deployments
                    - Deployer: Handles deployments and rollbacks
                    - Notifier: Sends notifications and pages on-call
                    
                    Analyze the situation and decide:
                    1. Is the incident resolved?
                    2. Which agent should act next?
                    3. What specific instruction should they follow?
                    
                    Be methodical: investigate first, then act, then communicate.
                    """,
                Temperature = 0f,
                ResponseFormat = ChatResponseFormat.ForJsonSchema<ManagerDecision>()
            }
        });

        // Run the magentic loop
        var history = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var agents = new Dictionary<string, AIAgent>
        {
            ["DeployInspector"] = inspector,
            ["Deployer"] = deployer,
            ["Notifier"] = notifier
        };

        for (int iteration = 1; iteration <= MaxIterations; iteration++)
        {
            cli.IterationSeparator(iteration);

            // Manager evaluates and decides
            cli.AgentStart("Manager", "Manager");
            var managerResponse = await manager.RunAsync(history);
            
            ManagerDecision decision;
            try
            {
                decision = managerResponse.Deserialize<ManagerDecision>(JsonSerializerOptions.Web);
            }
            catch
            {
                // If parsing fails, try to extract from text
                var text = managerResponse.Messages.LastOrDefault()?.Text ?? "";
                cli.Warn($"Could not parse manager decision, raw response: {text}");
                continue;
            }
            
            Console.WriteLine($"Resolved: {decision.IsResolved}");
            Console.WriteLine($"Next Agent: {decision.NextAgent}");
            Console.WriteLine($"Instruction: {decision.Instruction}");
            Console.WriteLine($"Reasoning: {decision.Reasoning}");

            history.Add(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(decision)));

            if (decision.IsResolved)
            {
                cli.Header("Incident Resolved");
                cli.RunnerResult(decision.Reasoning);
                break;
            }

            // Execute the chosen agent
            if (agents.TryGetValue(decision.NextAgent, out var agent))
            {
                cli.AgentStart(decision.NextAgent, decision.NextAgent);
                
                var agentMessages = new List<ChatMessage>(history)
                {
                    new(ChatRole.User, decision.Instruction)
                };

                var agentResponse = await agent.RunAsync(agentMessages);
                var responseText = agentResponse.Messages.LastOrDefault()?.Text ?? "";
                
                Console.WriteLine(responseText);
                history.Add(new ChatMessage(ChatRole.Assistant, $"[{decision.NextAgent}]: {responseText}"));
            }
            else
            {
                cli.Warn($"Unknown agent: {decision.NextAgent}");
            }
        }

        // Show final state
        cli.Header("Final State");
        cli.Info($"Notifications sent: {opsState.Notifications.Count}");
        cli.Info($"Deployments: {opsState.Deployments.Count}");
    }
}

[Description("Manager's decision for the next action")]
internal sealed class ManagerDecision
{
    [JsonPropertyName("is_resolved")]
    [Description("Whether the incident is fully resolved")]
    public bool IsResolved { get; set; }

    [JsonPropertyName("next_agent")]
    [Description("Which agent should act next: DeployInspector, Deployer, or Notifier")]
    public string NextAgent { get; set; } = "";

    [JsonPropertyName("instruction")]
    [Description("Specific instruction for the next agent")]
    public string Instruction { get; set; } = "";

    [JsonPropertyName("reasoning")]
    [Description("Explanation of the decision")]
    public string Reasoning { get; set; } = "";
}
