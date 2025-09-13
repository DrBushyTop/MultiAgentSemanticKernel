using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace MultiAgentSemanticKernel.Plugins;

public sealed class OpsInspectorTools
{
    private readonly OpsPlugin _ops;

    public OpsInspectorTools(OpsPlugin ops)
    {
        _ops = ops;
    }

    [KernelFunction, Description("Get deploy status")]
    public string Deploy_Status([Description("service")] string service)
        => _ops.Deploy_Status(service);

    [KernelFunction, Description("Diff last deploy")]
    public string Deploy_Diff([Description("prev")] string prev)
        => _ops.Deploy_Diff(prev);

    [KernelFunction, Description("List available versions with notes")]
    public string Versions_Available([Description("service")] string service)
        => _ops.Versions_Available(service);
}


