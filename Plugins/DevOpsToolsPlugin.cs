using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace MultiAgentSemanticKernel.Plugins;

public sealed class DevOpsToolsPlugin
{
    // --- Concurrent demo tools ---
    [KernelFunction, Description("Get PR diff summary")] 
    public string Git_GetPRDiff([Description("PR id or URL")] string pr)
        => "{\n  \"files\": [\"src/foo.cs\", \"src/bar.cs\"],\n  \"addedLines\": 120,\n  \"deletedLines\": 32\n}";

    [KernelFunction, Description("Map changed files to impacted test suites")] 
    public string CI_GetTestMap([Description("files JSON")] string files)
        => "{\n  \"suites\": [\"Unit-Core\", \"API-Contract\"],\n  \"estRuntimeSec\": 420\n}";

    [KernelFunction, Description("Run a simple lint over a diff")] 
    public string Lint_Run([Description("diff JSON")] string diff)
        => "{\n  \"findings\": [\"unused var\", \"missing await\"]\n}";

    [KernelFunction, Description("Run a simple secret scan")] 
    public string Secret_Scan([Description("diff JSON")] string diff)
        => "{\n  \"secrets\": [\"FAKE_API_KEY\"]\n}";

    [KernelFunction, Description("Check license headers")] 
    public string License_CheckHeaders([Description("files JSON")] string files)
        => "{\n  \"missing\": [\"src/bar.cs\"]\n}";

    // --- Sequential demo tools ---
    [KernelFunction, Description("Generate OpenAPI from story and AC")] 
    public string Oas_Generate([Description("story")] string story, [Description("acceptance JSON")] string acceptance)
        => "{\n  \"openapiYaml\": \"openapi: 3.1.0\\ninfo: {title: demo, version: 1.0.0}\\npaths: {}\"\n}";

    [KernelFunction, Description("Create a git branch")] 
    public string Repo_CreateBranch([Description("name")] string name)
        => "{\n  \"branch\": \"feature/demo\"\n}";

    [KernelFunction, Description("Commit changes")] 
    public string Repo_Commit([Description("branch")] string branch, [Description("changes JSON")] string changes)
        => "{\n  \"commitSha\": \"a1b2c3d\"\n}";

    [KernelFunction, Description("Generate tests from OAS and AC")] 
    public string Tests_Generate([Description("openapiYaml")] string openapiYaml, [Description("acceptance JSON")] string acceptance)
        => "{\n  \"files\": [\"tests/api_tests.cs\"]\n}";

    [KernelFunction, Description("Update docs and open PR")] 
    public string Docs_Update([Description("branch")] string branch, [Description("summary")] string summary)
        => "{\n  \"prUrl\": \"https://example.com/pr/123\"\n}";

    // --- GroupChat tools ---
    [KernelFunction, Description("Query observability via KQL")] 
    public string Observability_Query([Description("kql")] string kql)
        => "{\n  \"table\": [{\"metric\": \"p95\", \"value\": 123} ]\n}";

    [KernelFunction, Description("Estimate monthly cost from template")] 
    public string Cost_Estimate([Description("template")] string template)
        => "{\n  \"monthlyUsd\": 123.45\n}";

    [KernelFunction, Description("Lookup Azure limits for SKU")] 
    public string Azure_Limits([Description("sku")] string sku)
        => "{\n  \"maxConnections\": 1000, \n  \"notes\": \"demo\"\n}";

    [KernelFunction, Description("Lookup runbook steps")] 
    public string Runbook_Lookup([Description("service")] string service)
        => "{\n  \"steps\": [\"step1\", \"step2\"]\n}";

    // --- Handoff tools ---
    [KernelFunction, Description("Update ticket fields")] 
    public string Ticket_Update([Description("id")] string id, [Description("fields JSON")] string fields)
        => "{\n  \"ok\": true\n}";

    [KernelFunction, Description("Create repro CI job")] 
    public string CI_CreateReproJob([Description("params JSON")] string paramsJson)
        => "{\n  \"jobUrl\": \"https://ci.example/job/1\", \n  \"artifactUrl\": \"https://ci.example/artifact/1\"\n}";

    [KernelFunction, Description("Query logs via KQL")] 
    public string Logs_Query([Description("kql")] string kql)
        => "{\n  \"events\": [\"Null GUID at checkout\"]\n}";

    [KernelFunction, Description("Lookup code ownership")] 
    public string CodeOwnership_Lookup([Description("path")] string path)
        => "{\n  \"team\": \"team-checkout\", \n  \"oncall\": \"alice\"\n}";

    [KernelFunction, Description("Set feature flag")] 
    public string FeatureFlags_Set([Description("key")] string key, [Description("value")] string value)
        => "{\n  \"ok\": true\n}";

    // --- Magentic tools ---
    [KernelFunction, Description("Get deploy status")] 
    public string Deploy_Status([Description("service")] string service)
        => "{\n  \"version\": \"1.3.2\", \n  \"startedAt\": \"2025-09-12T12:00:00Z\"\n}";

    [KernelFunction, Description("Diff last deploy")] 
    public string Deploy_Diff([Description("prev")] string prev)
        => "{\n  \"changes\": [\"serializer update\"]\n}";

    [KernelFunction, Description("Rollback a deploy")] 
    public string Deploy_Rollback([Description("service")] string service, [Description("toVersion")] string? toVersion = null)
        => "{\n  \"ok\": true\n}";

    [KernelFunction, Description("Post a message to comms")] 
    public string Comms_Post([Description("channel")] string channel, [Description("message")] string message)
        => "{\n  \"link\": \"https://chat.example/msg/1\"\n}";

    [KernelFunction, Description("Get feature flag value")] 
    public string FeatureFlags_Get([Description("key")] string key)
        => "{\n  \"value\": true\n}";
}


