using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace MultiAgentSemanticKernel.Runners;

public sealed class MagenticRunner(Kernel kernel, ILogger<MagenticRunner> logger)
{
    public async Task RunAsync(string prompt)
    {
        logger.LogWarning("[Magentic] Starting with prompt: {Prompt}", string.IsNullOrWhiteSpace(prompt) ? "<none>" : prompt);
        await Task.CompletedTask; // Stub
    }
}


