using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace MultiAgentSemanticKernel.Runners;

public sealed class SequentialRunner(Kernel kernel, ILogger<SequentialRunner> logger)
{
    public async Task RunAsync(string prompt)
    {
        logger.LogInformation("[Sequential] Starting with prompt: {Prompt}", string.IsNullOrWhiteSpace(prompt) ? "<none>" : prompt);

        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = "Say hello from Sequential mode.";
        }

        var function = kernel.CreateFunctionFromPrompt(prompt);
        var result = await kernel.InvokeAsync(function);
        logger.LogInformation("[Sequential] Completed: {Result}", result.GetValue<string>());
    }
}


