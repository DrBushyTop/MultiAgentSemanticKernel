using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MultiAgentSemanticKernel.Options;
using MultiAgentSemanticKernel.Runners;
using MultiAgentSemanticKernel.Runtime;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables("MASKE_");

builder.Services.Configure<AzureOpenAiOptions>(
    builder.Configuration.GetSection("AzureOpenAI"));

var options = builder.Configuration.GetSection("AzureOpenAI").Get<AzureOpenAiOptions>()
    ?? throw new InvalidOperationException("AzureOpenAI configuration section is missing or invalid");

if (string.IsNullOrWhiteSpace(options.Endpoint))
    throw new InvalidOperationException("AzureOpenAI:Endpoint is required");

if (options.Deployments.Llm is null)
    throw new InvalidOperationException("AzureOpenAI:Deployments:Llm is required");

var credential = new DefaultAzureCredential();
var azureClient = new AzureOpenAIClient(new Uri(options.Endpoint), credential);

builder.Services.AddSingleton<IChatClient>(_ =>
    azureClient.GetChatClient(options.Deployments.Llm).AsIChatClient());

builder.Services.AddSingleton<ICliWriter, AnsiCliWriter>();

builder.Services.AddTransient<SequentialRunner>();
builder.Services.AddTransient<ConcurrentRunner>();
builder.Services.AddTransient<GroupChatRunner>();
builder.Services.AddTransient<HandoffRunner>();
builder.Services.AddTransient<MagenticRunner>();
builder.Services.AddTransient<GraphRunner>();

using var app = builder.Build();

var mode = args.Length > 0 ? args[0] : "";
var prompt = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "";

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var cli = app.Services.GetRequiredService<ICliWriter>();

if (string.IsNullOrWhiteSpace(mode))
{
    PrintUsage();
    return;
}

try
{
    cli.Header($"Running {mode} orchestration");
    
    switch (mode.ToLowerInvariant())
    {
        case "sequential":
            await app.Services.GetRequiredService<SequentialRunner>().RunAsync(prompt);
            break;
        case "concurrent":
            await app.Services.GetRequiredService<ConcurrentRunner>().RunAsync(prompt);
            break;
        case "groupchat":
            await app.Services.GetRequiredService<GroupChatRunner>().RunAsync(prompt);
            break;
        case "handoff":
            await app.Services.GetRequiredService<HandoffRunner>().RunAsync(prompt);
            break;
        case "magentic":
            await app.Services.GetRequiredService<MagenticRunner>().RunAsync(prompt);
            break;
        case "graph":
            await app.Services.GetRequiredService<GraphRunner>().RunAsync(prompt);
            break;
        default:
            cli.Warn($"Unknown mode: {mode}");
            PrintUsage();
            Environment.ExitCode = 1;
            break;
    }
}
catch (OperationCanceledException)
{
    cli.Warn("Operation cancelled");
    Environment.ExitCode = 130;
}
catch (Exception ex)
{
    logger.LogError(ex, "Error running {Mode}", mode);
    cli.Warn($"Error: {ex.Message}");
    Environment.ExitCode = 1;
}

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run -- <Sequential|Concurrent|GroupChat|Handoff|Magentic|Graph> [prompt...]");
}
