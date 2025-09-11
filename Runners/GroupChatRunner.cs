using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace MultiAgentSemanticKernel.Runners;

public sealed class GroupChatRunner(Kernel kernel, ILogger<GroupChatRunner> logger)
{
    public async Task RunAsync(string prompt)
    {
        logger.LogInformation("[GroupChat] Starting with prompt: {Prompt}", string.IsNullOrWhiteSpace(prompt) ? "<none>" : prompt);
        await Task.CompletedTask; // Stub for future implementation
    }
}


