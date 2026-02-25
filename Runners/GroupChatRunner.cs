using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using MultiAgentSemanticKernel.Managers;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Runners;

public class GroupChatRunner(IChatClient chatClient, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = "We need to design a new microservices architecture for our e-commerce platform. " +
                     "Discuss the key considerations and trade-offs.";
        }

        cli.Header("GroupChat: Architecture Review");
        cli.Info($"Topic: {prompt}");

        // Create discussion participants (no tools - pure discussion)
        var techLead = AgentFactory.CreateAgent(
            chatClient,
            name: "TechLead",
            instructions: """
                You are a Tech Lead. Balance technical excellence with delivery timelines.
                Consider scalability, maintainability, and team capabilities.
                Keep responses concise (2-3 paragraphs max).
                """,
            temperature: 0.7f);

        var sre = AgentFactory.CreateAgent(
            chatClient,
            name: "SRE",
            instructions: """
                You are an SRE (Site Reliability Engineer). Focus on reliability, 
                observability, and operational concerns. Consider failure modes,
                monitoring, and incident response. Keep responses concise.
                """,
            temperature: 0.7f);

        var security = AgentFactory.CreateAgent(
            chatClient,
            name: "Security",
            instructions: """
                You are a Security Engineer. Focus on threat modeling, authentication,
                authorization, and data protection. Identify potential vulnerabilities.
                Keep responses concise.
                """,
            temperature: 0.7f);

        var dataEng = AgentFactory.CreateAgent(
            chatClient,
            name: "DataEngineer",
            instructions: """
                You are a Data Engineer. Focus on data modeling, storage choices,
                data flow, and analytics requirements. Consider data consistency
                and migration strategies. Keep responses concise.
                """,
            temperature: 0.7f);

        // Build group chat with logging round-robin manager
        var workflow = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents => new LoggingRoundRobinGroupChatManager(agents, cli) 
            { 
                MaximumIterationCount = 5 
            })
            .AddParticipants(techLead, sre, security, dataEng)
            .Build();
        

        // Execute workflow
        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var result = await WorkflowRunner.ExecuteAsync(workflow, messages, cli);

        // Display discussion summary
        cli.Header("Discussion Summary");
        cli.RunnerResult($"Discussion completed with {result.Count} messages");
    }
}
