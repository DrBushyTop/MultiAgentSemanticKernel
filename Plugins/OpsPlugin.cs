using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;
using System.Linq;

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

    [KernelFunction, Description("Diff last deploy")]
    public string Deploy_Diff([Description("prev")] string prev)
    {
        var s = GetServiceOrDefault(_defaultService ?? "");
        var payload = new
        {
            previous = prev,
            current = s.Version,
            changes = new[]
            {
                new { component = s.Service, type = "code", detail = "rollback or hotfix applied" }
            }
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction, Description("Rollback a deploy")]
    public string Deploy_Rollback([Description("service")] string service, [Description("toVersion")] string? toVersion = null)
    {
        var s = GetServiceOrDefault(service);
        lock (_lock)
        {
            s.Rollbacks += 1;
            s.LastAction = "rollback";
            s.Version = toVersion ?? "previous-stable";
            s.StartedAt = DateTime.UtcNow.ToString("o");
            // simple improvement model
            s.P95Ms = Math.Max(200, s.P95Ms * 0.6);
            s.ErrorRate = Math.Max(0.005, s.ErrorRate * 0.3);
        }

        var payload = new
        {
            service = s.Service,
            toVersion = s.Version,
            ok = true,
            newHealth = new { p95_ms = (int)Math.Round(s.P95Ms), errorRate = Math.Round(s.ErrorRate, 3) },
            rollbacks = s.Rollbacks,
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    [KernelFunction, Description("Post a message to comms")]
    public string Comms_Post([Description("channel")] string channel, [Description("message")] string message)
        => "{\n" +
           "  \"channel\": \"" + channel + "\",\n" +
           "  \"message\": \"" + message.Replace("\"", "\\\"") + "\",\n" +
           "  \"link\": \"https://chat.example/msg/1\"\n" +
           "}";

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
    }
}


