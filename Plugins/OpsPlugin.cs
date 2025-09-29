using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using MultiAgentSemanticKernel.Runtime;

namespace MultiAgentSemanticKernel.Plugins;

public sealed class OpsPlugin
{
    private readonly object _lock = new();
    private readonly Dictionary<string, ServiceState> _services = new();
    private string? _defaultService;

    // Runner can seed initial state via this method before importing the plugin
    public void SeedService(string service, string version, double p95Ms, double errorRate, string env = "prod", IEnumerable<string>? owners = null)
    {
        lock (_lock)
        {
            _services[service] = new ServiceState
            {
                Service = service,
                Version = version,
                StartedAt = DateTime.UtcNow.AddHours(-1).ToString("o"),
                P95Ms = p95Ms,
                ErrorRate = errorRate,
                Env = env,
                Owners = owners?.ToArray() ?? new[] { "@team-catalog" },
                Rollbacks = 0,
                LastAction = "deploy",
                AvailableVersions = new List<VersionInfo>
                {
                    new VersionInfo { Version = "1.31", Notes = "previous stable" }
                }
            };
            _defaultService ??= service;
        }
    }

    // Ops: deploys, flags, comms
    [KernelFunction, Description("Get deploy status")]
    public string Deploy_Status([Description("service")] string service)
    {
        var s = GetServiceOrDefault(service);
        var payload = new
        {
            service = s.Service,
            version = s.Version,
            startedAt = s.StartedAt,
            health = new { p95_ms = (int)Math.Round(s.P95Ms), errorRate = Math.Round(s.ErrorRate, 3) },
            env = s.Env,
            owners = s.Owners,
            lastAction = s.LastAction,
            rollbacks = s.Rollbacks,
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction, Description("Deploy a specific version (use for rollback or upgrade)")]
    public string Deploy_Version([Description("service")] string service, [Description("version")] string version)
    {
        var prevColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"ℹ️  Deploying version {version} for service {service}");
        Console.ForegroundColor = prevColor;
        var s = GetServiceOrDefault(service);
        lock (_lock)
        {
            var isRollback = string.Compare(version, s.Version, StringComparison.Ordinal) < 0 || version == "1.31";
            if (isRollback)
            {
                s.Rollbacks += 1;
                s.LastAction = "rollback";
            }
            else
            {
                s.LastAction = "deploy";
            }

            s.Version = version;
            s.StartedAt = DateTime.UtcNow.ToString("o");
            // simple model for health change by version
            if (version == "1.31")
            {
                s.P95Ms = Math.Max(220, s.P95Ms * 0.6);
                s.ErrorRate = Math.Max(0.010, s.ErrorRate * 0.35);
                // after rollback, hotfix 1.33 becomes available
                if (!s.AvailableVersions.Any(v => v.Version == "1.33"))
                {
                    s.AvailableVersions.Add(new VersionInfo { Version = "1.33", Notes = "hotfix: improves error rate" });
                }
            }
            else if (version == "1.33")
            {
                // hotfix installed, improve further
                s.P95Ms = Math.Max(200, s.P95Ms * 0.85);
                s.ErrorRate = Math.Max(0.006, s.ErrorRate * 0.2);
            }
            else
            {
                // generic small change
                s.P95Ms = Math.Max(200, s.P95Ms * 0.95);
                s.ErrorRate = Math.Max(0.010, s.ErrorRate * 0.95);
            }
        }

        var payload = new
        {
            service = s.Service,
            version = s.Version,
            ok = true,
            newHealth = new { p95_ms = (int)Math.Round(s.P95Ms), errorRate = Math.Round(s.ErrorRate, 3) },
            rollbacks = s.Rollbacks,
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction, Description("Post a message to comms")]
    public string Comms_Post([Description("channel")] string channel, [Description("message")] string message)
    {
        var prevColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"ℹ️  Message to channel {channel}: {message}");
        Console.ForegroundColor = prevColor;
        return "{\n" +
           "  \"channel\": \"" + channel + "\",\n" +
           "  \"message\": \"" + message.Replace("\"", "\\\"") + "\",\n" +
           "  \"link\": \"https://chat.example/msg/1\"\n" +
           "}";
    }


    [KernelFunction, Description("List available versions with notes")]
    public string Versions_Available([Description("service")] string service)
    {
        var s = GetServiceOrDefault(service);
        var payload = new
        {
            service = s.Service,
            versions = s.AvailableVersions.Select(v => new { version = v.Version, notes = v.Notes }).ToArray()
        };
        var prevColor = Console.ForegroundColor;
        foreach (var v in s.AvailableVersions)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"ℹ️  Available version: {v.Version} - {v.Notes}");
        }
        Console.ForegroundColor = prevColor;
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private ServiceState GetServiceOrDefault(string service)
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(service) && _services.TryGetValue(service, out var s))
            {
                return s;
            }
            if (_defaultService is not null && _services.TryGetValue(_defaultService, out var def))
            {
                return def;
            }
            // initialize a minimal default to keep things simple
            SeedService("service", "1.0.0", 500, 0.02);
            return _services["service"];
        }
    }

    // serialization helper is inlined above

    private sealed class ServiceState
    {
        public string Service { get; set; } = "service";
        public string Version { get; set; } = "1.0.0";
        public string StartedAt { get; set; } = DateTime.UtcNow.ToString("o");
        public double P95Ms { get; set; } = 500;
        public double ErrorRate { get; set; } = 0.02;
        public string Env { get; set; } = "prod";
        public string[] Owners { get; set; } = Array.Empty<string>();
        public int Rollbacks { get; set; } = 0;
        public string LastAction { get; set; } = "deploy";
        public List<VersionInfo> AvailableVersions { get; set; } = new();
    }

    private sealed class VersionInfo
    {
        public string Version { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }
}


