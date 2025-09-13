using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace MultiAgentSemanticKernel.Runners;

public sealed class HandoffRunner(Kernel kernel, ILogger<HandoffRunner> logger)
{
    public async Task RunAsync(string prompt)
    {
        logger.LogWarning("[Handoff] Starting with prompt: {Prompt}", string.IsNullOrWhiteSpace(prompt) ? "<none>" : prompt);
        await Task.CompletedTask; // Stub
    }
}


