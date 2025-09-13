using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace MultiAgentSemanticKernel.Plugins;

public sealed class OpsPlugin
{
    // Ops: deploys, flags, comms
    [KernelFunction, Description("Get deploy status")]
    public string Deploy_Status([Description("service")] string service)
        => "{\n" +
           "  \"service\": \"" + service + "\",\n" +
           "  \"version\": \"1.3.2\",\n" +
           "  \"startedAt\": \"2025-09-12T12:00:00Z\",\n" +
           "  \"health\": { \"p95_ms\": 420, \"errorRate\": 0.012 },\n" +
           "  \"env\": \"prod\",\n" +
           "  \"owners\": [\"@team-catalog\"]\n" +
           "}";

    [KernelFunction, Description("Diff last deploy")]
    public string Deploy_Diff([Description("prev")] string prev)
        => "{\n" +
           "  \"previous\": \"" + prev + "\",\n" +
           "  \"current\": \"1.3.2\",\n" +
           "  \"changes\": [\n" +
           "    { \"component\": \"catalog-service\", \"type\": \"code\", \"detail\": \"serializer update\" },\n" +
           "    { \"component\": \"catalog-db\", \"type\": \"migration\", \"detail\": \"add index on products.name\" }\n" +
           "  ]\n" +
           "}";

    [KernelFunction, Description("Rollback a deploy")]
    public string Deploy_Rollback([Description("service")] string service, [Description("toVersion")] string? toVersion = null)
        => "{\n" +
           "  \"service\": \"" + service + "\",\n" +
           "  \"toVersion\": \"" + (toVersion ?? "previous-stable") + "\",\n" +
           "  \"steps\": [\n" +
           "    \"kubectl rollout undo deploy/" + service + "\",\n" +
           "    \"verify: health checks green\",\n" +
           "    \"notify: #incidents\"\n" +
           "  ],\n" +
           "  \"ok\": true\n" +
           "}";

    [KernelFunction, Description("Post a message to comms")]
    public string Comms_Post([Description("channel")] string channel, [Description("message")] string message)
        => "{\n" +
           "  \"channel\": \"" + channel + "\",\n" +
           "  \"message\": \"" + message.Replace("\"", "\\\"") + "\",\n" +
           "  \"link\": \"https://chat.example/msg/1\"\n" +
           "}";

    [KernelFunction, Description("Get feature flag value")]
    public string FeatureFlags_Get([Description("key")] string key)
        => "{\n" +
           "  \"key\": \"" + key + "\",\n" +
           "  \"value\": true,\n" +
           "  \"rules\": [\n" +
           "    { \"match\": \"env == 'prod'\", \"value\": true },\n" +
           "    { \"match\": \"user in beta_cohort\", \"value\": false }\n" +
           "  ]\n" +
           "}";
}


