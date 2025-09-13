using Microsoft.SemanticKernel.Agents.Orchestration;
using System.Threading.Tasks;

namespace MultiAgentSemanticKernel.Runtime;

public static class AgentResponseCallbacks
{
    public static OrchestrationResponseCallback Create(ICliWriter cli)
        => response =>
        {
            // Use reflection to avoid hard dependency on ChatMessageContent type in this file
            var type = response.GetType();
            var content = type.GetProperty("Content")?.GetValue(response) as string;
            var authorRole = type.GetProperty("Role")?.GetValue(response);
            var authorRoleType = authorRole?.GetType();
            var roleName = authorRoleType?.GetProperty("Label")?.GetValue(authorRole) as string;
            if (string.IsNullOrWhiteSpace(content) || roleName == "tool")
            {
                // Skip tool-only responses or empty messages
                return ValueTask.CompletedTask;
            }

            var author = type.GetProperty("AuthorName")?.GetValue(response) as string;
            author = string.IsNullOrWhiteSpace(author) ? "Agent" : author;
            cli.AgentResult(author!, content!);
            return ValueTask.CompletedTask;
        };
}