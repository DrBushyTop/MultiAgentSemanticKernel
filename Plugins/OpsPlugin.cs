using System.ComponentModel;
using System.Text.Json;

namespace MultiAgentSemanticKernel.Plugins;

/// <summary>
/// Shared state for Ops tools - enables realistic simulation of deployments and notifications.
/// </summary>
public class OpsState
{
    public Dictionary<string, ServiceInfo> Services { get; } = new();
    public List<string> Notifications { get; } = new();
    public List<DeploymentRecord> Deployments { get; } = new();
    public List<VersionInfo> AvailableVersions { get; } = new();
}

public record ServiceInfo(string Name, string Version, double P95Ms, double ErrorRate, string[] Owners);
public record DeploymentRecord(string Service, string Version, DateTime Timestamp, string Status);
public record VersionInfo(string Version, string Notes);

/// <summary>
/// Inspector tools - for checking service status and deployment history.
/// </summary>
public class OpsInspectorTools(OpsState state)
{
    [Description("Get current status of a service including version, latency, and error rate")]
    public string GetServiceStatus(string serviceName)
    {
        if (state.Services.TryGetValue(serviceName, out var info))
        {
            return $"""
                Service: {info.Name}
                Version: {info.Version}
                P95 Latency: {info.P95Ms}ms
                Error Rate: {info.ErrorRate:P1}
                Owners: {string.Join(", ", info.Owners)}
                """;
        }
        return $"Service '{serviceName}' not found";
    }

    [Description("List recent deployments for a service")]
    public string GetRecentDeployments(string serviceName)
    {
        var deployments = state.Deployments
            .Where(d => d.Service == serviceName)
            .OrderByDescending(d => d.Timestamp)
            .Take(5);
        
        if (!deployments.Any())
        {
            return $"No recent deployments for '{serviceName}'";
        }
        
        return string.Join("\n", deployments.Select(d => 
            $"- {d.Version} deployed at {d.Timestamp:g} ({d.Status})"));
    }

    [Description("List available versions for a service")]
    public string GetAvailableVersions(string serviceName)
    {
        if (!state.AvailableVersions.Any())
        {
            return "No versions available";
        }
        
        return string.Join("\n", state.AvailableVersions.Select(v => 
            $"- {v.Version}: {v.Notes}"));
    }
}

/// <summary>
/// Deployer tools - for deploying and rolling back services.
/// </summary>
public class OpsDeployerTools(OpsState state)
{
    [Description("Deploy a new version of a service")]
    public string DeployService(string serviceName, string version)
    {
        var record = new DeploymentRecord(serviceName, version, DateTime.UtcNow, "Success");
        state.Deployments.Add(record);
        
        if (state.Services.TryGetValue(serviceName, out var info))
        {
            // Update service info with new version
            state.Services[serviceName] = info with { Version = version };
            
            // Simulate improved metrics for newer versions
            if (version == "1.33")
            {
                state.Services[serviceName] = state.Services[serviceName] with 
                { 
                    P95Ms = Math.Max(200, info.P95Ms * 0.85),
                    ErrorRate = Math.Max(0.006, info.ErrorRate * 0.2)
                };
            }
        }
        
        return $"Deployed {serviceName} v{version} successfully";
    }

    [Description("Rollback a service to a previous version")]
    public string RollbackService(string serviceName, string version)
    {
        var record = new DeploymentRecord(serviceName, version, DateTime.UtcNow, "Rollback");
        state.Deployments.Add(record);
        
        if (state.Services.TryGetValue(serviceName, out var info))
        {
            state.Services[serviceName] = info with { Version = version };
            
            // Rollback typically improves stability
            state.Services[serviceName] = state.Services[serviceName] with 
            { 
                P95Ms = Math.Max(220, info.P95Ms * 0.6),
                ErrorRate = Math.Max(0.010, info.ErrorRate * 0.35)
            };
            
            // Make hotfix available after rollback
            if (!state.AvailableVersions.Any(v => v.Version == "1.33"))
            {
                state.AvailableVersions.Add(new VersionInfo("1.33", "hotfix: improves error rate"));
            }
        }
        
        return $"Rolled back {serviceName} to v{version}";
    }
}

/// <summary>
/// Notifier tools - for sending notifications and paging on-call.
/// </summary>
public class OpsNotifierTools(OpsState state)
{
    [Description("Send notification to a channel about an incident or update")]
    public string SendNotification(string channel, string message)
    {
        state.Notifications.Add($"[{channel}] {message}");
        return $"Notification sent to {channel}: {message}";
    }

    [Description("Page on-call engineer for critical issues")]
    public string PageOnCall(string serviceName, string severity, string reason)
    {
        if (state.Services.TryGetValue(serviceName, out var info))
        {
            var owners = string.Join(", ", info.Owners);
            state.Notifications.Add($"[PAGE-{severity}] {owners}: {reason}");
            return $"Paged {owners} for {serviceName} ({severity}): {reason}";
        }
        return $"Could not page - service '{serviceName}' not found";
    }
}
