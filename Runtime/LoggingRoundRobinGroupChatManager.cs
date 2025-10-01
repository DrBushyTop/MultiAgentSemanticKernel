using Microsoft.SemanticKernel.Agents.Orchestration.GroupChat;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MultiAgentSemanticKernel.Runtime
{
    public class LoggingRoundRobinGroupChatManager : RoundRobinGroupChatManager
    {
        private int _currentAgentIndex;

        public override ValueTask<GroupChatManagerResult<string>> SelectNextAgent(ChatHistory history, GroupChatTeam team, CancellationToken cancellationToken = default(CancellationToken))
        {
            string key = team.Skip(_currentAgentIndex).First().Key;
            _currentAgentIndex = (_currentAgentIndex + 1) % team.Count;

            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"ℹ️  Manager Selected agent: {key}");
            Console.ForegroundColor = prevColor;
            return ValueTask.FromResult(new GroupChatManagerResult<string>(key)
            {
                Reason = $"Selected agent at index: {_currentAgentIndex}"
            });
        }
    }
}