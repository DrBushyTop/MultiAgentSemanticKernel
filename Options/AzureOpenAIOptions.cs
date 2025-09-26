namespace MultiAgentSemanticKernel.Options;

public sealed class AzureOpenAIOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public DeploymentOptions Deployments { get; set; } = new();
}

public sealed class DeploymentOptions
{
    public string Llm { get; set; } = string.Empty;
}


