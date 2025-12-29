using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using MultiAgentSemanticKernel.Plugins;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Runners;

public class SequentialRunner(IChatClient chatClient, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = "Create a REST API for a todo list application with CRUD operations";
        }

        cli.Header("Sequential Pipeline: Dev Workflow");
        cli.Info($"Prompt: {prompt}");

        // Create tools from DevWorkflowTools
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(DevWorkflowTools.OasGenerate),
            AIFunctionFactory.Create(DevWorkflowTools.RepoCreateBranch),
            AIFunctionFactory.Create(DevWorkflowTools.CreateScaffold),
            AIFunctionFactory.Create(DevWorkflowTools.TestsGenerate),
            AIFunctionFactory.Create(DevWorkflowTools.DocsUpdate)
        };

        // Create agents for the pipeline
        var backlogRefiner = AgentFactory.CreateAgent(
            chatClient,
            name: "BacklogRefiner",
            instructions: """
                You are a product owner. Transform the user's request into a clear user story 
                with acceptance criteria. Output format:
                USER STORY: As a [user], I want [feature] so that [benefit]
                ACCEPTANCE CRITERIA:
                - [criterion 1]
                - [criterion 2]
                """);

        var scaffolder = AgentFactory.CreateAgent(
            chatClient,
            name: "Scaffolder",
            instructions: """
                You are a software architect. Based on the user story, suggest a project structure.
                Use the CreateScaffold tool to generate the structure.
                """,
            tools);

        var apiDesigner = AgentFactory.CreateAgent(
            chatClient,
            name: "APIDesigner",
            instructions: """
                You are an API designer. Based on the user story and acceptance criteria,
                design REST endpoints. Use the OasGenerate tool to create an OpenAPI spec.
                """,
            tools);

        var testWriter = AgentFactory.CreateAgent(
            chatClient,
            name: "TestWriter",
            instructions: """
                You are a QA engineer. Based on the API design, generate test stubs.
                Use the TestsGenerate tool to create test cases.
                """,
            tools);

        var docWriter = AgentFactory.CreateAgent(
            chatClient,
            name: "DocWriter",
            instructions: """
                You are a technical writer. Create documentation for the API.
                Use the DocsUpdate tool to create documentation.
                """,
            tools);

        // Build sequential workflow
        var workflow = AgentWorkflowBuilder.BuildSequential(
            backlogRefiner, scaffolder, apiDesigner, testWriter, docWriter);

        // Execute workflow
        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var result = await WorkflowRunner.ExecuteAsync(workflow, messages, cli);

        // Display final result
        cli.RunnerResult(string.Join("\n\n", result
            .Where(m => m.Role != ChatRole.User)
            .Select(m => $"[{m.AuthorName}]: {m.Text}")));
    }
}
