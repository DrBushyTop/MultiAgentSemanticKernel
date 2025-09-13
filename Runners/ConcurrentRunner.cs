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
            Instructions = "Summarize the PR diff size, risky files, and hot spots. Only respond with the result, no fluff, be concise.",
            Description = "Scans changes to identify hotspots, risk areas, and potential churn.",
            Kernel = kernel,
            LoggerFactory = kernel.LoggerFactory,
        };

        var testImpactor = new ChatCompletionAgent
        {
            Name = "TestImpactor",
            Instructions = "Map changed files to impacted test suites and estimate runtime. Only respond with the result, no fluff, be concise.",
            Description = "Estimates which test suites are affected by the diff and runtime impact.",
            Kernel = kernel,
            LoggerFactory = kernel.LoggerFactory,
        };

        var secLint = new ChatCompletionAgent
        {
            Name = "SecLint",
            Instructions = "Run a lightweight lint/SAST mental pass; report any obvious issues. Only respond with the result, no fluff, be concise.",
            Description = "Performs a quick security and linting pass to flag obvious issues.",
            Kernel = kernel,
            LoggerFactory = kernel.LoggerFactory,
        };

        var compliance = new ChatCompletionAgent
        {
            Name = "Compliance",
            Instructions = "Check secrets and license headers hypothetically; report any concerns. Only respond with the result, no fluff, be concise.",
            Description = "Checks for secret exposure and license/header compliance concerns.",
            Kernel = kernel,
            LoggerFactory = kernel.LoggerFactory,
        };

        ValueTask ResponseCallback(ChatMessageContent response)
        {
            var author = string.IsNullOrWhiteSpace(response.AuthorName) ? "Agent" : response.AuthorName;
            cli.AgentResult(author!, response.Content ?? string.Empty);
            return ValueTask.CompletedTask;
        }

        var orchestration = new ConcurrentOrchestration(diffAnalyst, testImpactor, secLint, compliance)
        {
            LoggerFactory = kernel.LoggerFactory,
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