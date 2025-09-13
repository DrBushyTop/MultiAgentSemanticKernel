using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Orchestration;
using Microsoft.SemanticKernel.Agents.Orchestration.Concurrent;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using MultiAgentSemanticKernel.Runtime;
using MultiAgentSemanticKernel.Plugins;

namespace MultiAgentSemanticKernel.Runners;

public sealed class ConcurrentRunner(Kernel kernel, ILogger<ConcurrentRunner> logger, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {
        // Import only PR analysis tools for this runner
        kernel.ImportPluginFromType<PrAnalysisPlugin>();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            var defaultPrompt = """
            Analyze the following pull request. Provide a concise summary.

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

        var diffAnalyst = AgentUtils.Create(
            name: "DiffAnalyst",
            description: "Scans changes to identify hotspots, risk areas, and potential churn.",
            instructions: "Summarize diff size, risky files, and hot spots. Use available tools: call Git_GetPRDiff(prText) to compute stats from the PR text or ID; if needed, pass the entire prompt as input. Only respond with the result, no fluff, be concise.",
            kernel: kernel);

        var testImpactor = AgentUtils.Create(
            name: "TestImpactor",
            description: "Estimates which test suites are affected by the diff and runtime impact.",
            instructions: "Map changed files to impacted test suites and estimate runtime. Use Git_GetPRDiff(prText) to obtain changed files, then CI_GetTestMap(filesJson) to get suites and runtime. Only respond with the result, no fluff, be concise.",
            kernel: kernel);

        var secLint = AgentUtils.Create(
            name: "SecLint",
            description: "Performs a quick security and linting pass to flag obvious issues.",
            instructions: "Run a lightweight lint/SAST pass over the diff. Use Git_GetPRDiff(prText) to get a diff and then Lint_Run(diffJson) and Secret_Scan(diffJson). Summarize findings. Only respond with the result, no fluff, be concise.",
            kernel: kernel);

        var compliance = AgentUtils.Create(
            name: "Compliance",
            description: "Checks for secret exposure and license/header compliance concerns.",
            instructions: "Check secrets and license headers. Use Git_GetPRDiff(prText) to get changed files and diff; run Secret_Scan(diffJson) and License_CheckHeaders(filesJson). Report any concerns. Only respond with the result, no fluff, be concise.",
            kernel: kernel);

        var responseCallback = AgentResponseCallbacks.Create(cli);

        var orchestration = new ConcurrentOrchestration(diffAnalyst, testImpactor, secLint, compliance)
        {
            LoggerFactory = kernel.LoggerFactory,
            ResponseCallback = responseCallback,
        };

        var runtime = new InProcessRuntime();
        await runtime.StartAsync();

        var result = await orchestration.InvokeAsync(prompt, runtime);
        await result.GetValueAsync(TimeSpan.FromSeconds(120));

        await runtime.RunUntilIdleAsync();
    }
}