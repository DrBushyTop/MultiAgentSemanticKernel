using System.ClientModel;
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
using OpenAI.Chat;

var builder = Host.CreateApplicationBuilder(args);

// Load configuration
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables("MASKE_");

builder.Services.Configure<AzureOpenAIOptions>(
    builder.Configuration.GetSection("AzureOpenAI"));

var options = builder.Configuration.GetSection("AzureOpenAI").Get<AzureOpenAIOptions>()!;

// Create Azure OpenAI client
var credential = new DefaultAzureCredential();
var azureClient = new AzureOpenAIClient(new Uri(options.Endpoint), credential);

// Register IChatClient for the LLM deployment
builder.Services.AddSingleton<IChatClient>(sp =>
    azureClient.GetChatClient(options.Deployments.Llm).AsIChatClient());

// Register CLI writer
builder.Services.AddSingleton<ICliWriter, AnsiCliWriter>();

// Register runners
builder.Services.AddTransient<SequentialRunner>();
builder.Services.AddTransient<ConcurrentRunner>();
builder.Services.AddTransient<GroupChatRunner>();
builder.Services.AddTransient<HandoffRunner>();
builder.Services.AddTransient<MagenticRunner>();

var app = builder.Build();

// Parse command line
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
        default:
            cli.Warn($"Unknown mode: {mode}");
            PrintUsage();
            Environment.ExitCode = 1;
            break;
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "Error running {Mode}", mode);
    cli.Warn($"Error: {ex.Message}");
    Environment.ExitCode = 1;
}

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run -- <Sequential|Concurrent|GroupChat|Handoff|Magentic> [prompt...]");
}
