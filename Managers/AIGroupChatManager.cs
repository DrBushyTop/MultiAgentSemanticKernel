using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Managers;

/// <summary>
/// A <see cref="GroupChatManager"/> that uses an AI model to select agents and determine termination.
/// </summary>
public class AiGroupChatManager : GroupChatManager
{
    private readonly IReadOnlyList<AIAgent> _agents;
    private readonly IChatClient _chatClient;
    private readonly ICliWriter _cli;
    private readonly string _topic;

    private static class Prompts
    {
        public static string Termination(string topic) =>
            $$"""
            You are a mediator that guides a discussion on the topic of '{{topic}}'.
            You need to determine if the discussion has reached a conclusion.
            
            Respond with a JSON object in this exact format:
            {"shouldTerminate": true, "reason": "your reason here"}
            
            Set shouldTerminate to true only if:
            - The participants have reached a consensus
            - All key points have been adequately discussed
            - The discussion is going in circles without new insights
            
            Otherwise, set shouldTerminate to false.
            """;

        public static string Selection(string topic, string participants) =>
            $$"""
            You are a mediator that guides a discussion on the topic of '{{topic}}'.
            You need to select the next participant to speak based on the conversation flow.
            
            Here are the available participants:
            {{participants}}
            
            Consider:
            - Who hasn't spoken recently
            - Who has relevant expertise for the current point being discussed
            - Natural conversation flow
            
            Respond with a JSON object in this exact format:
            {"selectedAgent": "AgentName", "reason": "your reason here"}
            
            The selectedAgent must be exactly one of the participant names listed above.
            """;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AiGroupChatManager"/> class.
    /// </summary>
    /// <param name="agents">The agents to be managed as part of this workflow.</param>
    /// <param name="chatClient">The chat client used for AI-based decisions.</param>
    /// <param name="cli">The CLI writer for logging.</param>
    /// <param name="topic">The topic of the discussion.</param>
    public AiGroupChatManager(
        IReadOnlyList<AIAgent> agents,
        IChatClient chatClient,
        ICliWriter cli,
        string topic)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(cli);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        if (agents.Count == 0)
        {
            throw new ArgumentException("At least one agent is required.", nameof(agents));
        }

        foreach (var agent in agents)
        {
            ArgumentNullException.ThrowIfNull(agent, nameof(agents));
        }

        _agents = agents;
        _chatClient = chatClient;
        _cli = cli;
        _topic = topic;
    }

    /// <inheritdoc />
    protected override async ValueTask<AIAgent> SelectNextAgentAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken = default)
    {
        var participants = FormatParticipantList();
        var prompt = Prompts.Selection(_topic, participants);

        var messages = new List<ChatMessage>(history)
        {
            new(ChatRole.System, prompt)
        };

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
            var responseText = response.Text;

            var result = JsonSerializer.Deserialize<SelectionResult>(responseText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result?.SelectedAgent is not null)
            {
                var selectedAgent = _agents.FirstOrDefault(a =>
                    string.Equals(a.Name, result.SelectedAgent, StringComparison.OrdinalIgnoreCase));

                if (selectedAgent is not null)
                {
                    _cli.AgentSelected("AI", selectedAgent.Name ?? selectedAgent.Id);
                    return selectedAgent;
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to default selection
        }

        // Fallback: select first agent if AI selection fails
        var fallbackAgent = _agents[0];
        _cli.AgentSelected("AI (fallback)", fallbackAgent.Name ?? fallbackAgent.Id);
        return fallbackAgent;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken = default)
    {
        // First check base termination (iteration count)
        if (await base.ShouldTerminateAsync(history, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        var prompt = Prompts.Termination(_topic);

        var messages = new List<ChatMessage>(history)
        {
            new(ChatRole.System, prompt)
        };

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
            var responseText = response.Text;

            var result = JsonSerializer.Deserialize<TerminationResult>(responseText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result?.ShouldTerminate == true)
            {
                _cli.Info($"AI decided to terminate: {result.Reason}");
                return true;
            }
        }
        catch (JsonException)
        {
            // Continue discussion if we can't parse the response
        }

        return false;
    }

    private string FormatParticipantList()
    {
        var lines = _agents.Select(a =>
        {
            var name = a.Name ?? a.Id;
            return $"- {name}";
        });
        return string.Join("\n", lines);
    }

    private sealed class SelectionResult
    {
        public string? SelectedAgent { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class TerminationResult
    {
        public bool ShouldTerminate { get; set; }
        public string? Reason { get; set; }
    }
}
