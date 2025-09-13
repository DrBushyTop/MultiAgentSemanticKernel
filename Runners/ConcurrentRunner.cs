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
            var defaultPrompt = """
            Analyze the following pull request. Provide a concise, bullet-style summary covering:
            - Diff size and hotspots
            - Impacted test suites and rough runtime estimate
            - Security/lint concerns
            - Compliance/secrets/license concerns

            PR: feat(auth): add input validation and fix null handling

            Description:
            - Add basic server-side validation to signup flow
            - Fix potential null reference in UserService
            - Minor UI tweak in SignupForm and dep bump

            Files changed (5):
            1) src/Controllers/AuthController.cs (+23 −4)
            2) src/Services/UserService.cs (+18 −6)
            3) src/Models/SignupRequest.cs (+12 −0)
            4) web/Frontend/components/SignupForm.tsx (+9 −2)
            5) package.json (+1 −1)

            Relevant diffs (snippets):
            --- a/src/Services/UserService.cs
            +++ b/src/Services/UserService.cs
            @@ -42,7 +42,13 @@
            - var user = await _repo.FindByEmailAsync(request.Email);
            - if (user.IsActive) { /* ... */ }
            + var user = await _repo.FindByEmailAsync(request.Email);
            + if (user == null)
            + {
            +     _logger.LogWarning("Signup requested for unknown email {Email}", request.Email);
            +     throw new NotFoundException("User not found");
            + }
            + if (user.IsActive) { /* ... */ }

            --- a/src/Controllers/AuthController.cs
            +++ b/src/Controllers/AuthController.cs
            @@ -88,3 +102,12 @@
            - return Ok(await _service.Signup(request));
            + if (!ModelState.IsValid) return BadRequest(ModelState);
            + return Ok(await _service.Signup(request));

            Constraints:
            - Tests live under tests/ and follow *Tests.cs naming
            - Assume CI has 8 parallel workers available
            """;
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
        await result.GetValueAsync(TimeSpan.FromSeconds(120));

        await runtime.RunUntilIdleAsync();
    }
}