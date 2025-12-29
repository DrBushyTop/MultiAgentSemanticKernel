using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Managers;

/// <summary>
/// A <see cref="RoundRobinGroupChatManager"/> that logs agent selection to the console.
/// </summary>
public class LoggingRoundRobinGroupChatManager : GroupChatManager
{
    private readonly IReadOnlyList<AIAgent> _agents;
    private readonly ICliWriter _cli;
    private readonly Func<LoggingRoundRobinGroupChatManager, IEnumerable<ChatMessage>, CancellationToken, ValueTask<bool>>? _shouldTerminateFunc;
    private int _nextIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingRoundRobinGroupChatManager"/> class.
    /// </summary>
    /// <param name="agents">The agents to be managed as part of this workflow.</param>
    /// <param name="cli">The CLI writer for logging agent selection.</param>
    /// <param name="shouldTerminateFunc">
    /// An optional function that determines whether the group chat should terminate based on the chat history.
    /// </param>
    public LoggingRoundRobinGroupChatManager(
        IReadOnlyList<AIAgent> agents,
        ICliWriter cli,
        Func<LoggingRoundRobinGroupChatManager, IEnumerable<ChatMessage>, CancellationToken, ValueTask<bool>>? shouldTerminateFunc = null)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(cli);
        
        if (agents.Count == 0)
        {
            throw new ArgumentException("At least one agent is required.", nameof(agents));
        }

        foreach (var agent in agents)
        {
            ArgumentNullException.ThrowIfNull(agent, nameof(agents));
        }

        _agents = agents;
        _cli = cli;
        _shouldTerminateFunc = shouldTerminateFunc;
    }

    /// <inheritdoc />
    protected override ValueTask<AIAgent> SelectNextAgentAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken = default)
    {
        AIAgent nextAgent = _agents[_nextIndex];

        _cli.AgentSelected("RoundRobin", nextAgent.Name ?? nextAgent.Id);

        _nextIndex = (_nextIndex + 1) % _agents.Count;

        return new ValueTask<AIAgent>(nextAgent);
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken = default)
    {
        if (_shouldTerminateFunc is { } func && await func(this, history, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return await base.ShouldTerminateAsync(history, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void Reset()
    {
        base.Reset();
        _nextIndex = 0;
    }
}
