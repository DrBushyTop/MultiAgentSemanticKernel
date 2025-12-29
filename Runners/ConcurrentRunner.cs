using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using MultiAgentSemanticKernel.Plugins;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Runners;

public class ConcurrentRunner(IChatClient chatClient, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = "Analyze PR #123 for the authentication refactoring changes";
        }

        cli.Header("Concurrent Pipeline: PR Analysis");
        cli.Info($"Prompt: {prompt}");

        // Create tools
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(PrAnalysisTools.GitGetPrDiff),
            AIFunctionFactory.Create(PrAnalysisTools.CiGetTestMap),
            AIFunctionFactory.Create(PrAnalysisTools.LintRun),
            AIFunctionFactory.Create(PrAnalysisTools.SecretScan),
            AIFunctionFactory.Create(PrAnalysisTools.LicenseCheckHeaders)
        };

        // Create parallel analysis agents
        var diffAnalyst = AgentFactory.CreateAgent(
            chatClient,
            name: "DiffAnalyst",
            instructions: """
                You analyze code diffs for changes, complexity hotspots, and risk areas.
                Use the GitGetPRDiff tool to get diff information.
                Provide a summary of changes and potential risks.
                """,
            tools);

        var testImpactor = AgentFactory.CreateAgent(
            chatClient,
            name: "TestImpactor",
            instructions: """
                You identify which test suites are affected by code changes.
                Use the CIGetTestMap tool to determine affected tests.
                Recommend which tests should be run.
                """,
            tools);

        var secLint = AgentFactory.CreateAgent(
            chatClient,
            name: "SecLint",
            instructions: """
                You perform security analysis and linting checks.
                Use the LintRun tool to check for code quality issues.
                Report any security concerns or code quality issues.
                """,
            tools);

        var compliance = AgentFactory.CreateAgent(
            chatClient,
            name: "Compliance",
            instructions: """
                You check for secret exposure and license compliance.
                Use the SecretScan and LicenseCheckHeaders tools to verify compliance.
                Report any compliance issues found.
                """,
            tools);

        // Build concurrent workflow - all agents run in parallel
        var workflow = AgentWorkflowBuilder.BuildConcurrent([diffAnalyst, testImpactor, secLint, compliance]);

        // Execute workflow
        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var result = await WorkflowRunner.ExecuteAsync(workflow, messages, cli);

        // Display combined results
        cli.Header("Combined PR Analysis Results");
        foreach (var message in result.Where(m => m.Role != ChatRole.User))
        {
            cli.AgentResult(message.AuthorName ?? "Agent", message.Text);
        }
    }
}
