using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Runners;

/// <summary>
/// Executor that starts the concurrent code review processing by dispatching messages to the reviewers.
/// </summary>
[SendsMessage(typeof(ChatMessage))]
[SendsMessage(typeof(TurnToken))]
internal sealed class ConcurrentStartExecutor : Executor<List<ChatMessage>>
{
    public ConcurrentStartExecutor() : base("ConcurrentStart")
    {
    }

    /// <summary>
    /// Dispatches the code review request to concurrent reviewers.
    /// </summary>
    /// <param name="messages">The initial user messages containing the PR to review</param>
    /// <param name="context">Workflow context for accessing workflow services and adding events</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public override async ValueTask HandleAsync(List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // Broadcast the messages to all connected agents. Receiving agents will queue
        // the messages but will not start processing until they receive a turn token.
        foreach (var message in messages)
        {
            await context.SendMessageAsync(message, cancellationToken);
        }

        // Broadcast the turn token to kick off the agents.
        await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken);
    }
}

/// <summary>
/// Executor that combines messages from multiple reviewers into a formatted prompt.
/// This is a pure data transformation step - no LLM analysis happens here.
/// Fan-in executors are called once per source, so we must accumulate results
/// and only forward when all expected sources have completed.
/// </summary>
[SendsMessage(typeof(ChatMessage))]
[SendsMessage(typeof(TurnToken))]
internal sealed class ReviewCombinerExecutor : Executor<List<ChatMessage>>
{
    private readonly List<ChatMessage> _collectedMessages = [];
    private const int ExpectedSourceCount = 2; // QualityReviewer + SecurityReviewer

    public ReviewCombinerExecutor() : base("ReviewCombiner")
    {
    }

    /// <summary>
    /// Combines review messages into a formatted prompt for the report generator.
    /// Called once per source agent - accumulates until all sources complete.
    /// </summary>
    /// <param name="messages">The messages from one reviewer</param>
    /// <param name="context">Workflow context for accessing workflow services and adding events</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public override async ValueTask HandleAsync(List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // Accumulate messages from each source
        _collectedMessages.AddRange(messages);
        
        // Filter to get only assistant responses (reviews from the agents)
        var reviewMessages = _collectedMessages
            .Where(m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text))
            .ToList();
        
        // Only proceed when we have reviews from all expected sources
        if (reviewMessages.Count < ExpectedSourceCount)
        {
            return;
        }

        // Combine all reviews into a formatted prompt (data transformation only)
        var combinedReviews = string.Join("\n\n---\n\n", 
            reviewMessages.Select(m => $"**{m.AuthorName ?? "Reviewer"} Review:**\n{m.Text}"));
        
        var synthesisPrompt = $"""
            Based on the following reviews, create a comprehensive final report:

            {combinedReviews}

            Synthesize this into a final report with:
            - Overall Assessment (APPROVE / REQUEST_CHANGES / NEEDS_DISCUSSION)
            - Key Findings (organized by category)
            - Action Items (prioritized and actionable)
            - Summary of both quality and security concerns

            Be clear, actionable, and comprehensive.
            """;
        
        var synthesisMessage = new ChatMessage(ChatRole.User, synthesisPrompt);
        
        // Forward the formatted prompt to the report generator agent
        await context.SendMessageAsync(synthesisMessage, cancellationToken);
        
        // Send turn token to trigger the report generator agent
        await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken);
    }
}

/// <summary>
/// Demonstrates custom graph-based workflow with concurrent execution pattern.
/// This runner showcases:
/// - ConcurrentStartExecutor: Dispatches work to multiple agents in parallel
/// - ReviewCombinerExecutor: Formats and combines results from parallel agents (no LLM analysis)
/// - Fan-out pattern: One executor sends to multiple agents
/// - Fan-in pattern: Multiple agents converge to one executor for message aggregation
/// - AI agents for domain-specific LLM processing (quality review, security review, final report)
/// </summary>
public class GraphRunner(IChatClient chatClient, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = """
                Review the following PR changes:
                
                PR #1234: Update authentication module with JWT token refresh
                
                Files Changed:
                
                1. src/Auth/TokenService.cs (Modified)
                   - Added RefreshTokenAsync method
                   - Implemented token rotation logic
                   - Added validation for refresh token expiration
                   
                   ```csharp
                   + public async Task<TokenResponse> RefreshTokenAsync(string refreshToken)
                   + {
                   +     var principal = ValidateToken(refreshToken);
                   +     var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                   +     
                   +     if (string.IsNullOrEmpty(userId))
                   +         throw new SecurityException("Invalid token");
                   +     
                   +     var user = await _userRepository.GetByIdAsync(userId);
                   +     return await GenerateTokensAsync(user);
                   + }
                   ```
                
                2. src/Auth/AuthMiddleware.cs (Modified)
                   - Updated token validation logic
                   - Added automatic token refresh on 401
                   
                   ```csharp
                   - if (context.Response.StatusCode == 401)
                   - {
                   -     return;
                   - }
                   + if (context.Response.StatusCode == 401)
                   + {
                   +     var refreshToken = context.Request.Cookies["refresh_token"];
                   +     if (!string.IsNullOrEmpty(refreshToken))
                   +     {
                   +         var newTokens = await _tokenService.RefreshTokenAsync(refreshToken);
                   +         context.Response.Headers.Add("X-New-Token", newTokens.AccessToken);
                   +     }
                   + }
                   ```
                
                3. tests/Auth/TokenServiceTests.cs (Added)
                   - New unit tests for RefreshTokenAsync
                   - Edge case coverage for expired tokens
                   
                   ```csharp
                   + [Fact]
                   + public async Task RefreshTokenAsync_WithExpiredToken_ThrowsException()
                   + {
                   +     var expiredToken = GenerateExpiredToken();
                   +     await Assert.ThrowsAsync<SecurityException>(
                   +         () => _tokenService.RefreshTokenAsync(expiredToken));
                   + }
                   ```
                
                4. docs/Authentication.md (Modified)
                   - Updated documentation with refresh token flow
                   - Added security considerations
                
                Summary: This PR implements JWT refresh token functionality to improve user experience
                by allowing seamless token renewal without re-authentication.
                """;
        }

        cli.Header("Custom Graph Workflow: Code Review Pipeline with Custom Executors");
        cli.Info($"Task: {prompt}");

        // EXECUTOR 1: Concurrent Start (custom executor - entry point)
        var startExecutor = new ConcurrentStartExecutor();

        // AGENT 2a: Quality Reviewer (parallel branch A)
        var qualityReviewer = AgentFactory.CreateAgent(
            chatClient,
            name: "QualityReviewer",
            instructions: """
                You perform code quality reviews.
                Check for:
                - Code structure and organization
                - Best practices
                - Maintainability
                - Testing considerations
                
                Provide specific, actionable feedback.
                Keep your review concise and focused.
                """);

        // AGENT 2b: Security Reviewer (parallel branch B)
        var securityReviewer = AgentFactory.CreateAgent(
            chatClient,
            name: "SecurityReviewer",
            instructions: """
                You perform security reviews.
                Check for:
                - Authentication/authorization
                - Input validation
                - Data protection
                - Common vulnerabilities
                
                Provide specific security recommendations.
                Keep your review concise and focused.
                """);

        // EXECUTOR 3: Review Combiner (custom executor - convergence point for message formatting)
        var combinerExecutor = new ReviewCombinerExecutor();
        
        // AGENT 4: Final Report Generator
        var reportGenerator = AgentFactory.CreateAgent(
            chatClient,
            name: "ReportGenerator",
            instructions: """
                You create the final code review report based on synthesized feedback.
                Create a comprehensive summary with:
                - Overall Assessment (APPROVE / REQUEST_CHANGES / NEEDS_DISCUSSION)
                - Key Findings (organized by category)
                - Action Items (prioritized)
                - Risk Assessment
                
                Be clear, professional, and actionable.
                Format the report in a structured, easy-to-read manner.
                """);

        // Start building the workflow graph
        var builder = new WorkflowBuilder(startExecutor);

        // FAN-OUT: Both reviewers run in parallel after start executor
        builder.AddFanOutEdge(startExecutor, [qualityReviewer, securityReviewer]);

        // FAN-IN: Both reviewers feed into combiner executor (message formatting only)
        builder.AddFanInBarrierEdge([qualityReviewer, securityReviewer], combinerExecutor);

        // FINAL EDGE: Combiner feeds formatted prompt to report generator (LLM analysis happens here)
        builder.AddEdge(combinerExecutor, reportGenerator);

        // Specify which executors produce the final output
        builder.WithOutputFrom(reportGenerator);

        // Build the workflow
        var workflow = builder.Build();

        cli.Info(workflow.ToMermaidString());

        // Execute the workflow
        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        cli.Info($"▶ Starting workflow execution...\n");
        
        await WorkflowRunner.ExecuteAsync(workflow, messages, cli);

        cli.Header("Workflow Completed");
        cli.Info("All review stages completed successfully!");
    }
}
