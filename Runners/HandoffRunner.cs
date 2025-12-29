using System.ComponentModel;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Runners;

public class HandoffRunner(IChatClient chatClient, ICliWriter cli)
{
    // Simulated user responses for demo purposes (like the old InteractiveCallback)
    private readonly Queue<string> _simulatedResponses = new(
    [
        "Constraints: UI only for now, we want Stripe integration",
        "Proceed to implementation",
        "Create a branch and open a PR, then let's review the code",
        "Looks good, merge it"
    ]);

    public async Task RunAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = "I need to add a new payment gateway integration to our system";
        }

        cli.Header("Handoff: Dev Triage (Human-in-the-Loop Demo)");
        cli.Info($"Request: {prompt}");

        // Track clean conversation history (only text messages, no tool calls)
        var conversationSummary = new List<(string Role, string Agent, string Text)>
        {
            // Initial message
            ("User", "User", prompt)
        };
        
        // Run multiple turns to simulate human-in-the-loop interaction
        const int maxTurns = 6;
        for (int turn = 1; turn <= maxTurns; turn++)
        {
            cli.TurnSeparator(turn);
            
            // Build messages from conversation summary (clean, no tool calls)
            var messages = BuildCleanMessages(conversationSummary);
            
            // Create fresh workflow for each turn
            var workflow = CreateHandoffWorkflow();
            
            // Execute and collect responses
            var responses = await ExecuteWorkflowTurn(workflow, messages);
            
            // Add agent responses to conversation summary
            foreach (var (agent, text) in responses.Where(r => !string.IsNullOrWhiteSpace(r.Text)))
            {
                conversationSummary.Add(("Assistant", agent, text));
            }

            // Check if we have more simulated responses
            if (_simulatedResponses.Count > 0)
            {
                var userResponse = _simulatedResponses.Dequeue();
                cli.UserInput(userResponse);
                conversationSummary.Add(("User", "User", userResponse));
            }
            else
            {
                // End the conversation
                var finalResponse = "Thanks, that's all for now!";
                cli.UserInput(finalResponse);
                conversationSummary.Add(("User", "User", finalResponse));
                
                // One final turn
                messages = BuildCleanMessages(conversationSummary);
                workflow = CreateHandoffWorkflow();
                responses = await ExecuteWorkflowTurn(workflow, messages);
                
                foreach (var (agent, text) in responses.Where(r => !string.IsNullOrWhiteSpace(r.Text)))
                {
                    conversationSummary.Add(("Assistant", agent, text));
                }
                break;
            }
        }

        // Display final summary
        cli.Header("Handoff Complete");
        var summary = conversationSummary
            .Where(c => c.Role == "Assistant" && !string.IsNullOrWhiteSpace(c.Text))
            .TakeLast(3)
            .Select(c => $"[{c.Agent}]: {(c.Text.Length > 200 ? c.Text[..200] + "..." : c.Text)}");
        cli.RunnerResult(string.Join("\n\n", summary));
    }

    private static List<ChatMessage> BuildCleanMessages(List<(string Role, string Agent, string Text)> summary)
    {
        var messages = new List<ChatMessage>();
        foreach (var (role, agent, text) in summary)
        {
            var chatRole = role == "User" ? ChatRole.User : ChatRole.Assistant;
            var msg = new ChatMessage(chatRole, text);
            if (role == "Assistant")
            {
                msg.AuthorName = agent;
            }
            messages.Add(msg);
        }
        return messages;
    }

    private async Task<List<(string Agent, string Text)>> ExecuteWorkflowTurn(
        Workflow workflow, 
        List<ChatMessage> messages)
    {
        var responses = new List<(string Agent, string Text)>();
        string? lastExecutorId = null;
        var currentText = new System.Text.StringBuilder();
        string currentAgent = "";

        await using var run = await InProcessExecution.StreamAsync(workflow, messages);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        await foreach (var evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case AgentRunUpdateEvent e:
                    if (e.ExecutorId != lastExecutorId)
                    {
                        // Save previous agent's response
                        if (!string.IsNullOrWhiteSpace(currentText.ToString()))
                        {
                            responses.Add((currentAgent, currentText.ToString()));
                        }
                        
                        lastExecutorId = e.ExecutorId;
                        currentAgent = e.Update.AuthorName ?? e.ExecutorId;
                        currentText.Clear();
                        cli.AgentStart(e.ExecutorId, currentAgent);
                    }

                    if (!string.IsNullOrEmpty(e.Update.Text))
                    {
                        Console.Write(e.Update.Text);
                        currentText.Append(e.Update.Text);
                    }

                    // Log function calls (but don't include in message history)
                    if (e.Update.Contents.OfType<FunctionCallContent>().FirstOrDefault() is { } call)
                    {
                        cli.ToolStart(e.ExecutorId, call.Name, 
                            call.Arguments?.ToDictionary(x => x.Key, x => x.Value?.ToString() ?? "") 
                            ?? new Dictionary<string, string>());
                    }
                    break;

                case WorkflowOutputEvent:
                    // Save final agent's response
                    if (!string.IsNullOrWhiteSpace(currentText.ToString()))
                    {
                        responses.Add((currentAgent, currentText.ToString()));
                    }
                    Console.WriteLine();
                    return responses;

                case ExecutorFailedEvent failed:
                    if (failed.Data is { } ex)
                    {
                        cli.Warn($"Agent {failed.ExecutorId} failed: {ex.Message}");
                    }
                    break;
            }
        }

        // Save any remaining response
        if (!string.IsNullOrWhiteSpace(currentText.ToString()))
        {
            responses.Add((currentAgent, currentText.ToString()));
        }
        
        return responses;
    }

    private Workflow CreateHandoffWorkflow()
    {
        // Create tools for design and implementation agents
        var designTools = new List<AITool>
        {
            AIFunctionFactory.Create(CreateDesignDoc),
            AIFunctionFactory.Create(CreateBranch)
        };

        var implTools = new List<AITool>
        {
            AIFunctionFactory.Create(GenerateCode),
            AIFunctionFactory.Create(OpenPullRequest),
            AIFunctionFactory.Create(MergePullRequest)
        };

        // Create triage agent (routes to specialists)
        var triageAgent = AgentFactory.CreateAgent(
            chatClient,
            name: "TriageAgent",
            instructions: """
                You are a development triage agent. Analyze incoming requests and route them
                to the appropriate specialist:
                - For architecture, design, or planning questions -> hand off to DesignAgent
                - For implementation, coding, or bug fixes -> hand off to ImplementationAgent
                
                When an agent completes work and returns to you, summarize what was done and 
                ask the user if they want to proceed further. Continue until the user says 
                they are done or the task is complete.
                
                Always explain why you're routing to a specific agent.
                """);

        // Create design specialist
        var designAgent = AgentFactory.CreateAgent(
            chatClient,
            name: "DesignAgent",
            instructions: """
                You are a software design specialist. Create design documents, 
                architecture diagrams, and technical specifications.
                Use the CreateDesignDoc tool to generate documentation.
                Use the CreateBranch tool to create feature branches.
                When you need more information from the user, ask clearly.
                When design is complete and user approves, hand back to TriageAgent.
                """,
            designTools);

        // Create implementation specialist
        var implAgent = AgentFactory.CreateAgent(
            chatClient,
            name: "ImplementationAgent",
            instructions: """
                You are an implementation specialist. Write code, fix bugs,
                and implement features based on designs.
                Use the GenerateCode tool to create code.
                Use the OpenPullRequest tool to create PRs for review.
                Use the MergePullRequest tool to merge approved PRs.
                When implementation is complete, hand back to TriageAgent.
                """,
            implTools);

        // Build handoff workflow
        return AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)
            .WithHandoffs(triageAgent, [designAgent, implAgent])
            .WithHandoffs(designAgent, [triageAgent, implAgent])
            .WithHandoffs(implAgent, [triageAgent, designAgent])
            .Build();
    }

    [Description("Create a design document for the given feature")]
    private static string CreateDesignDoc(string feature, string requirements)
    {
        return $"""
            # Design Document: {feature}
            
            ## Requirements
            {requirements}
            
            ## Proposed Solution
            - Component diagram: PaymentGateway -> StripeAdapter -> StripeSDK
            - Sequence diagram: User -> API -> PaymentService -> Stripe -> Webhook
            - API contracts: POST /payments, GET /payments/:id, POST /refunds
            
            ## Implementation Notes
            - Estimated effort: 2 sprints
            - Dependencies: Stripe.NET SDK, Database migrations for payment records
            - Security: PCI DSS compliance required, use Stripe Elements for card input
            """;
    }

    [Description("Create a feature branch for the implementation")]
    private static string CreateBranch(string branchName)
    {
        return $"Created branch: feature/{branchName}";
    }

    [Description("Generate code for the given component")]
    private static string GenerateCode(string component, string specification)
    {
        return $$"""
            // Generated code for {{component}}
            public class {{component}}
            {
                private readonly IStripeClient _stripe;
                
                public {{component}}(IStripeClient stripe)
                {
                    _stripe = stripe;
                }
                
                // Based on: {{specification}}
                public async Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request)
                {
                    var options = new PaymentIntentCreateOptions
                    {
                        Amount = request.Amount,
                        Currency = request.Currency,
                        PaymentMethodTypes = new List<string> { "card" }
                    };
                    
                    var intent = await _stripe.PaymentIntentService.CreateAsync(options);
                    return new PaymentResult { IntentId = intent.Id, Status = intent.Status };
                }
            }
            """;
    }

    [Description("Open a pull request for code review")]
    private static string OpenPullRequest(string title, string description)
    {
        return $"""
            Pull Request Created:
            - Title: {title}
            - Description: {description}
            - PR #42: https://github.com/example/repo/pull/42
            - Status: Ready for review
            - Reviewers: @team-payments
            """;
    }

    [Description("Merge an approved pull request")]
    private static string MergePullRequest(int prNumber)
    {
        return $"""
            Pull Request #{prNumber} merged successfully!
            - Merged to: main
            - Commit: abc123def
            - CI/CD: Deployment triggered to staging
            """;
    }
}
