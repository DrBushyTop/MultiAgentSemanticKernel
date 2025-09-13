using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace MultiAgentSemanticKernel.Plugins;

public sealed class OpsDeployerTools
{
    private readonly OpsPlugin _ops;

    public OpsDeployerTools(OpsPlugin ops)
    {
        _ops = ops;
    }

    [KernelFunction, Description("Deploy a specific version (use for rollback or upgrade)")]
    public string Deploy_Version([Description("service")] string service, [Description("version")] string version)
        => _ops.Deploy_Version(service, version);

    [KernelFunction, Description("List available versions with notes")]
    public string Versions_Available([Description("service")] string service)
        => _ops.Versions_Available(service);

    [KernelFunction, Description("Get deploy status")]
    public string Deploy_Status([Description("service")] string service)
        => _ops.Deploy_Status(service);
}


