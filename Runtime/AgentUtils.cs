using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MultiAgentSemanticKernel.Runtime;

public class AgentUtils
{
    public static ChatCompletionAgent Create(string name, string description, string instructions, Kernel kernel, object? responseFormat = null, Action<Kernel>? configureKernel = null)
    {
        return CreateAgent(name, description, instructions, kernel, responseFormat, configureKernel);
    }

    public static ChatCompletionAgent CreateAgent(string name, string description, string instructions, Kernel kernel, object? responseFormat = null, Action<Kernel>? configureKernel = null)
    {
        // Create a per-agent kernel with identity + filter for clean logging
        var agentId = Guid.NewGuid().ToString("N");

        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var cliWriter = kernel.GetRequiredService<ICliWriter>();
        var loggerFactory = kernel.LoggerFactory;

        var builder = Kernel.CreateBuilder();
        // Reuse the same chat completion service and logging
        builder.Services.AddSingleton<IChatCompletionService>(chatService);
        builder.Services.AddSingleton<ICliWriter>(cliWriter);
        builder.Services.AddSingleton<ILoggerFactory>(loggerFactory);
        builder.Services.AddLogging();
        // Per-agent identity and filter
        builder.Services.AddSingleton(new AgentIdentity(agentId, name));
        builder.Services.AddSingleton<IFunctionInvocationFilter, ConsoleFunctionInvocationFilter>();

        var agentKernel = builder.Build();
        if (configureKernel is not null)
        {
            configureKernel(agentKernel);
        }
        else
        {
            // Mirror plugins from the shared kernel (e.g., Ops/PR/Workflow and any runtime plugins)
            foreach (var plugin in kernel.Plugins)
            {
                agentKernel.Plugins.Add(plugin);
            }
        }

        var args = new KernelArguments(
            new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                Temperature = 0,
                ResponseFormat = responseFormat ?? "text"
            })
        {
            ["AgentName"] = name,
            ["AgentId"] = agentId,
        };

        return new ChatCompletionAgent
        {
            Name = name,
            Instructions = instructions,
            Description = description,
            Kernel = agentKernel,
            LoggerFactory = loggerFactory,
            Arguments = args,
        };
    }
}