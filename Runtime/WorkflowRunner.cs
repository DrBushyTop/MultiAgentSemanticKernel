using System.Text.Json;
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
        string? lastExecutorId = null;

        StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        await foreach (WorkflowEvent evt in run.WatchStreamAsync().WithCancellation(cancellationToken))
        {
            switch (evt)
            {
                case AgentRunUpdateEvent e:
                    if (e.ExecutorId != lastExecutorId)
                    {
                        lastExecutorId = e.ExecutorId;
                        cli.AgentStart(e.ExecutorId, e.Update.AuthorName ?? e.ExecutorId);
                    }

                    if (!string.IsNullOrEmpty(e.Update.Text))
                    {
                        Console.Write(e.Update.Text);
                    }

                    // Log function calls
                    if (e.Update.Contents.OfType<FunctionCallContent>().FirstOrDefault() is FunctionCallContent call)
                    {
                        cli.ToolStart(e.ExecutorId, call.Name, 
                            call.Arguments?.ToDictionary(x => x.Key, x => x.Value?.ToString() ?? "") 
                            ?? new Dictionary<string, string>());
                    }
                    break;

                case WorkflowOutputEvent output:
                    Console.WriteLine();
                    return output.As<List<ChatMessage>>() ?? [];

                case ExecutorFailedEvent failed:
                    if (failed.Data is Exception ex)
                    {
                        cli.Warn($"Agent {failed.ExecutorId} failed: {ex.Message}");
                    }
                    break;
            }
        }

        return [];
    }
}
