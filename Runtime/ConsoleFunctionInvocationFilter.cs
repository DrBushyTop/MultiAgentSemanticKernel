using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace MultiAgentSemanticKernel.Runtime;

public sealed class ConsoleFunctionInvocationFilter : IFunctionInvocationFilter
{
    private readonly ILogger<ConsoleFunctionInvocationFilter> _logger;

    public ConsoleFunctionInvocationFilter(ILogger<ConsoleFunctionInvocationFilter> logger)
    {
        _logger = logger;
    }

    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        var functionName = context.Function.Name;
        var pluginName = context.Function.PluginName;
        _logger.LogInformation("[Invoke] {Plugin}.{Function}", pluginName, functionName);

        await next(context);

        var text = context.Result?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            _logger.LogInformation("[Result] {Plugin}.{Function}: {Text}", pluginName, functionName, text);
        }
    }
}


