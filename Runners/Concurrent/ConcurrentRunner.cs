using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Orchestration;
using Microsoft.SemanticKernel.Agents.Orchestration.Concurrent;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Runners;

public sealed class ConcurrentRunner(Kernel kernel, ILogger<ConcurrentRunner> logger, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            var defaultPrompt = "Analyze a small PR in parallel and summarize risk.";
            logger.LogWarning("[Concurrent] Using default prompt: {Prompt}", defaultPrompt);
            prompt = defaultPrompt;
        }
        else
        {
            logger.LogWarning("[Concurrent] Starting with prompt: {Prompt}", prompt);
        }

        var diffAnalyst = new ChatCompletionAgent
        {
            Name = "DiffAnalyst",
            Instructions = "Summarize the PR diff size, risky files, and hot spots. Keep it concise.",
            Kernel = kernel
        };

        var testImpactor = new ChatCompletionAgent
        {
            Name = "TestImpactor",
            Instructions = "Map changed files to impacted test suites and estimate runtime.",
            Kernel = kernel
        };

        var secLint = new ChatCompletionAgent
        {
            Name = "SecLint",
            Instructions = "Run a lightweight lint/SAST mental pass; report any obvious issues.",
            Kernel = kernel
        };

        var compliance = new ChatCompletionAgent
        {
            Name = "Compliance",
            Instructions = "Check secrets and license headers hypothetically; report any concerns.",
            Kernel = kernel
        };

        ValueTask ResponseCallback(ChatMessageContent response)
        {
            var author = string.IsNullOrWhiteSpace(response.AuthorName) ? "Agent" : response.AuthorName;
            cli.AgentResult(author!, response.Content ?? string.Empty);
            return ValueTask.CompletedTask;
        }

        var orchestration = new ConcurrentOrchestration(diffAnalyst, testImpactor, secLint, compliance)
        {
            ResponseCallback = ResponseCallback,
        };

        var runtime = new InProcessRuntime();
        await runtime.StartAsync();

        var result = await orchestration.InvokeAsync(prompt, runtime);
        var outputs = await result.GetValueAsync(TimeSpan.FromSeconds(120));

        if (outputs is { } lines)
        {
            cli.Info("# RESULT");
            foreach (var line in lines)
            {
                cli.Info(line);
            }
        }

        await runtime.RunUntilIdleAsync();
    }
}