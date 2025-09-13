using Microsoft.SemanticKernel;
using Microsoft.Extensions.Logging;

namespace MultiAgentSemanticKernel.Runtime;

public sealed class ConsoleFunctionInvocationFilter : IFunctionInvocationFilter
{
    private readonly ICliWriter _cli;

    public ConsoleFunctionInvocationFilter(ICliWriter cli)
    {
        _cli = cli;
    }

    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        var functionName = context.Function.Name;
        var pluginName = context.Function.PluginName;
        var agent = string.IsNullOrWhiteSpace(pluginName) ? functionName : $"{pluginName}.{functionName}";
        _cli.AgentStart(agent);

        await next(context);

        var text = context.Result?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            _cli.AgentResult(agent, text!);
        }
    }
}