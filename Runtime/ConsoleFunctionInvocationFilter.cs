using Microsoft.SemanticKernel;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Linq;

namespace MultiAgentSemanticKernel.Runtime;

public sealed class ConsoleFunctionInvocationFilter : IFunctionInvocationFilter
{
    private readonly ICliWriter _cli;
    private readonly AgentIdentity? _id;
    private readonly ILogger<ConsoleFunctionInvocationFilter>? _log;

    public ConsoleFunctionInvocationFilter(ICliWriter cli, ILogger<ConsoleFunctionInvocationFilter>? log = null, AgentIdentity? id = null)
    {
        _cli = cli;
        _log = log;
        _id = id;
    }

    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        var functionName = context.Function.Name;
        var pluginName = context.Function.PluginName;
        var caller = _id?.Name ?? "Agent";

        _cli.ToolStart(caller, pluginName ?? "", functionName);
        _log?.LogInformation("🔧 {Plugin}.{Func} by {Agent}", pluginName, functionName, caller);

        await next(context);

        // TODO: You could also check the result of the function call and set success accordingly
        // _cli.ToolEnd(caller, pluginName ?? "", functionName, success: true);
    }
}