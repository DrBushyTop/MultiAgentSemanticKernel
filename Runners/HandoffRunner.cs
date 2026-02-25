using System.ComponentModel;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Runners;

public class HandoffRunner(IChatClient chatClient, ICliWriter cli)
{
    // Simulated user responses for demo purposes.
    // These are dequeued one at a time by the InteractiveCallback whenever an agent
    // hands back to the human for input — the correct HITL pattern.
    private readonly Queue<string> _simulatedResponses = new(
    [
        "Constraints: UI only for now, we want Stripe integration",
        "Proceed to implementation",
        "Create a branch and open a PR, then let's review the code",
        "Looks good, merge it",
        "Thanks, that's all for now!"
    ]);

    public async Task RunAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = "I need to add a new payment gateway integration to our system";
        }

        cli.Header("Handoff: Dev Triage (Human-in-the-Loop Demo)");
        cli.Info($"Request: {prompt}");

        // AgentWorkflowBuilder handoff workflows are stateless per-turn: they complete after
        // each agent response and do not emit RequestInfoEvent. The human-in-the-loop is
        // implemented as an outer loop — per the official Agent Framework handoff sample:
        //   https://github.com/microsoft/agent-framework/blob/main/dotnet/samples/community/Workflow.Handoff
        //
        // Each iteration:
        //   1. Add the current user message to the shared history
        //   2. Recreate the workflow (stateless) and run it with the full history
        //   3. Append the agent replies to history
        //   4. Dequeue the next simulated user response and loop

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        while (true)
        {
            // Workflow is stateless — recreate each turn with the full conversation history
            var workflow = CreateHandoffWorkflow();
            var turnResults = await WorkflowRunner.ExecuteAsync(workflow, messages, cli);

            // Accumulate agent responses into history so the next turn has full context
            foreach (var msg in turnResults.Where(m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text)))
            {
                messages.Add(new ChatMessage(ChatRole.Assistant, msg.Text!) { AuthorName = msg.AuthorName });
            }

            // Dequeue the next simulated human response (HITL step)
            if (!_simulatedResponses.TryDequeue(out var userResponse))
                break;

            cli.UserInput(userResponse);
            messages.Add(new ChatMessage(ChatRole.User, userResponse));
        }

        cli.Header("Handoff Complete");
        var summary = messages
            .Where(m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text))
            .TakeLast(3)
            .Select(m => $"[{m.AuthorName ?? "Agent"}]: {(m.Text!.Length > 200 ? m.Text[..200] + "..." : m.Text)}");
        cli.RunnerResult(string.Join("\n\n", summary));
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
