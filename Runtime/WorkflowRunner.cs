using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace MultiAgentSemanticKernel.Runtime;

/// <summary>
/// Delegate for handling interactive input requests during workflow execution.
/// Return null to end the conversation, or a ChatMessage with the user's response.
/// </summary>
public delegate ValueTask<ChatMessage?> InteractiveCallback();

public static class WorkflowRunner
{
    /// <summary>
    /// Executes a workflow with optional interactive callback for human-in-the-loop scenarios.
    /// </summary>
    /// <param name="workflow">The workflow to execute</param>
    /// <param name="messages">Initial messages to send to the workflow</param>
    /// <param name="cli">CLI writer for output</param>
    /// <param name="interactiveCallback">Optional callback invoked when the workflow requests user input. 
    /// Return null to end the conversation.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The final list of chat messages from the workflow</returns>
    public static async Task<List<ChatMessage>> ExecuteAsync(
        Workflow workflow,
        List<ChatMessage> messages,
        ICliWriter cli,
        InteractiveCallback? interactiveCallback = null,
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
                        
                        // Clear content after outputting (executor might be invoked again)
                        executorContent[completed.ExecutorId].Clear();
                    }
                    break;

                case RequestInfoEvent requestInfo:
                    // Workflow is requesting external input (human-in-the-loop)
                    if (interactiveCallback != null)
                    {
                        var userMessage = await interactiveCallback();
                        if (userMessage != null)
                        {
                            cli.UserInput(userMessage.Text ?? "");
                            
                            // Send response back to the workflow
                            var response = requestInfo.Request.CreateResponse(userMessage);
                            await run.SendResponseAsync(response);
                        }
                        else
                        {
                            // User wants to end - send empty response to let workflow complete
                            var response = requestInfo.Request.CreateResponse(new ChatMessage(ChatRole.User, ""));
                            await run.SendResponseAsync(response);
                        }
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
