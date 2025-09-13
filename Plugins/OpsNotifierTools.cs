using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace MultiAgentSemanticKernel.Plugins;

public sealed class OpsNotifierTools
{
    private readonly OpsPlugin _ops;

    public OpsNotifierTools(OpsPlugin ops)
    {
        _ops = ops;
    }

    [KernelFunction, Description("Post a message to comms")]
    public string Comms_Post([Description("channel")] string channel, [Description("message")] string message)
        => _ops.Comms_Post(channel, message);
}


