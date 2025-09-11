using Azure.Identity;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using MultiAgentSemanticKernel.Options;
using MultiAgentSemanticKernel.Plugins;
using MultiAgentSemanticKernel.Runners;
using MultiAgentSemanticKernel.Runtime;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables(prefix: "MASKE_");

builder.Services.Configure<AzureOpenAIOptions>(builder.Configuration.GetSection("AzureOpenAI"));

builder.Services.AddSingleton<DefaultAzureCredential>(_ => new DefaultAzureCredential());

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AzureOpenAIOptions>>().Value;
    var credential = sp.GetRequiredService<DefaultAzureCredential>();

    var kernelBuilder = Kernel.CreateBuilder();

    kernelBuilder.AddAzureOpenAIChatCompletion(
        deploymentName: options.Deployments.Llm,
        endpoint: options.Endpoint,
        credentials: credential);

    kernelBuilder.Services.AddLogging(l => l.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    }));

    // Register filters via DI per SK guidance
    kernelBuilder.Services.AddSingleton<IFunctionInvocationFilter, ConsoleFunctionInvocationFilter>();

    return kernelBuilder.Build();
});

builder.Services.AddSingleton<DemoPlugin>();

builder.Services.AddSingleton<SequentialRunner>();
builder.Services.AddSingleton<ConcurrentRunner>();
builder.Services.AddSingleton<GroupChatRunner>();
builder.Services.AddSingleton<HandoffRunner>();
builder.Services.AddSingleton<MagenticRunner>();

var host = builder.Build();

// Post-build registrations
var kernel = host.Services.GetRequiredService<Kernel>();
kernel.ImportPluginFromType<DemoPlugin>();

// Minimal CLI handling: first arg = mode, second (optional) = prompt
var mode = args.Length > 0 ? args[0] : string.Empty;
var prompt = args.Length > 1 ? string.Join(' ', args.Skip(1)) : string.Empty;

if (string.IsNullOrWhiteSpace(mode))
{
    PrintUsage();
    return;
}

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Main");
logger.LogInformation("Mode: {Mode}; Prompt: {Prompt}", mode, string.IsNullOrWhiteSpace(prompt) ? "<none>" : prompt);

int exitCode = 0;
try
{
    switch (mode.ToLowerInvariant())
    {
        case "sequential":
            await host.Services.GetRequiredService<SequentialRunner>().RunAsync(prompt);
            break;
        case "concurrent":
            await host.Services.GetRequiredService<ConcurrentRunner>().RunAsync(prompt);
            break;
        case "groupchat":
            await host.Services.GetRequiredService<GroupChatRunner>().RunAsync(prompt);
            break;
        case "handoff":
            await host.Services.GetRequiredService<HandoffRunner>().RunAsync(prompt);
            break;
        case "magentic":
            await host.Services.GetRequiredService<MagenticRunner>().RunAsync(prompt);
            break;
        default:
            PrintUsage();
            exitCode = 1;
            break;
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "Unhandled exception while running mode {Mode}", mode);
    exitCode = 2;
}

Environment.ExitCode = exitCode;
return;

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run -- <Sequential|Concurrent|GroupChat|Handoff|Magentic> [prompt...]");
}
