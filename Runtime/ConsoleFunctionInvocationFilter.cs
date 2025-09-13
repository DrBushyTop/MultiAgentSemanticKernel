using Microsoft.SemanticKernel;
using Microsoft.Extensions.Logging;
using System.Text.Json;

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

        string argsPreview;
        try
        {
            var keys = context.Arguments.Keys.ToArray();
            var pairs = keys.Select(k => $"{k}={FormatValue(context.Arguments[k])}");
            argsPreview = string.Join(", ", pairs);
        }
        catch
        {
            argsPreview = "<args unavailable>";
        }

        var header = $"🤖 {caller} → 🔧 {pluginName}.{functionName}";
        _cli.AgentStart(header, argsPreview);
        _log?.LogInformation("Agent {Agent} called {Plugin}.{Func} with {Args}", caller, pluginName, functionName, argsPreview);

        await next(context);

        try
        {
            var resultText = context.Result?.GetValue<string?>() ?? "<no result>";
            _cli.AgentResult($"{caller}", resultText);
        }
        catch
        {
            // ignore formatting issues
        }
    }

    private static string FormatValue(object? value)
        => value switch
        {
            null => "null",
            string s when s.Length > 120 => JsonSerializer.Serialize(s[..120] + "…"),
            string s => JsonSerializer.Serialize(s),
            _ => JsonSerializer.Serialize(value)
        };
}