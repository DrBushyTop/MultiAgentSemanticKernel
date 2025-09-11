using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace MultiAgentSemanticKernel.Plugins;

public sealed class DemoPlugin
{
    [KernelFunction, Description("Returns a static weather report for a given city.")]
    public string GetWeather([Description("City name")] string city)
        => $"Weather in {city}: Sunny, 24°C. (static demo)";

    [KernelFunction, Description("Returns a static news headline for a topic.")]
    public string GetNewsHeadline([Description("Topic")] string topic)
        => $"Breaking: Major updates in {topic}. (static demo)";

    [KernelFunction, Description("Echoes the provided text.")]
    public string Echo([Description("Text to echo")] string text)
        => text;
}


