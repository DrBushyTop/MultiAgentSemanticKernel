using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace MultiAgentSemanticKernel.Runtime;

public static class WorkflowRunner
{
    public static async Task<List<ChatMessage>> ExecuteAsync(
        Workflow workflow,
        List<ChatMessage> messages,
        ICliWriter cli,
        CancellationToken cancellationToken = default)
    {
        // Track accumulated content per executor
        var executorContent = new Dictionary<string, System.Text.StringBuilder>();
        var executorNames = new Dictionary<string, string>();

        StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages, cancellationToken: cancellationToken);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        await foreach (WorkflowEvent evt in run.WatchStreamAsync(cancellationToken))
        {
            switch (evt)
            {
                case AgentRunUpdateEvent e:
                    // Initialize tracking for this executor if first time seeing it
                    if (!executorContent.ContainsKey(e.ExecutorId))
                    {
                        executorContent[e.ExecutorId] = new System.Text.StringBuilder();
                        executorNames[e.ExecutorId] = e.Update.AuthorName ?? e.ExecutorId;
                        cli.AgentStart(e.ExecutorId, e.Update.AuthorName ?? e.ExecutorId);
                    }

                    // Accumulate text for this executor
                    if (!string.IsNullOrEmpty(e.Update.Text))
                    {
                        executorContent[e.ExecutorId].Append(e.Update.Text);
                    }

                    // Log function calls
                    if (e.Update.Contents.OfType<FunctionCallContent>().FirstOrDefault() is { } call)
                    {
                        cli.ToolStart(e.ExecutorId, call.Name, 
                            call.Arguments?.ToDictionary(x => x.Key, x => x.Value?.ToString() ?? "") 
                            ?? new Dictionary<string, string>());
                    }
                    break;

                case ExecutorCompletedEvent completed:
                    // Output accumulated text when executor completes
                    if (executorContent.TryGetValue(completed.ExecutorId, out var content) && content.Length > 0)
                    {
                        var authorName = executorNames.TryGetValue(completed.ExecutorId, out var name) 
                            ? name 
                            : completed.ExecutorId;
                        cli.AgentResult(authorName, content.ToString());
                    }
                    break;

                case WorkflowOutputEvent output:
                    return output.As<List<ChatMessage>>() ?? [];

                case ExecutorFailedEvent failed:
                    // Output accumulated text before showing failure
                    if (executorContent.TryGetValue(failed.ExecutorId, out var failedContent) && failedContent.Length > 0)
                    {
                        var authorName = executorNames.TryGetValue(failed.ExecutorId, out var name) 
                            ? name 
                            : failed.ExecutorId;
                        cli.AgentResult(authorName, failedContent.ToString());
                    }
                    
                    if (failed.Data is { } ex)
                    {
                        cli.Warn($"Agent {failed.ExecutorId} failed: {ex.Message}");
                    }
                    break;
            }
        }

        return [];
    }
}
