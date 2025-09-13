using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace MultiAgentSemanticKernel.Runners;

public sealed class ConcurrentRunner(Kernel kernel, ILogger<ConcurrentRunner> logger)
{
    public async Task RunAsync(string prompt)
    {
        logger.LogWarning("[Concurrent] Starting with prompt: {Prompt}", string.IsNullOrWhiteSpace(prompt) ? "<none>" : prompt);
        await Task.CompletedTask; // Stub
    }
}


