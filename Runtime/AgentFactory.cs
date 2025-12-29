using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace MultiAgentSemanticKernel.Runtime;

public static class AgentFactory
{
    public static ChatClientAgent CreateAgent(
        IChatClient chatClient,
        string name,
        string instructions,
        IList<AITool>? tools = null,
        float temperature = 0f)
    {
        return new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = name,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Temperature = temperature,
                Tools = tools ?? []
            }
        });
    }
}
