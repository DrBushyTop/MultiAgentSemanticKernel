using System.Runtime.CompilerServices;

namespace MultiAgentSemanticKernel.Runtime;

public interface ICliWriter
{
    void AgentStart(string agentName, string? detail = null);
    void AgentResult(string agentName, string result);
    void Info(string message);
    void Warn(string message);
}

public sealed class AnsiCliWriter : ICliWriter
{
    private static readonly object _lock = new();

    public void AgentStart(string agentName, string? detail = null)
    {
        lock (_lock)
        {
            Console.Write("\x1b[2m"); // dim
            Console.Write("→ ");
            Console.Write("\x1b[36m"); // cyan
            Console.Write(agentName);
            Console.Write("\x1b[0m");
            if (!string.IsNullOrWhiteSpace(detail))
            {
                Console.Write("  ");
                Console.Write(detail);
            }
            Console.WriteLine();
        }
    }

    public void AgentResult(string agentName, string result)
    {
        lock (_lock)
        {
            Console.Write("\x1b[32m"); // green
            Console.Write("✔ ");
            Console.Write(agentName);
            Console.Write("\x1b[0m");
            Console.WriteLine();
            Console.WriteLine(result);
            Console.WriteLine();
        }
    }

    public void Info(string message)
    {
        lock (_lock)
        {
            Console.WriteLine(message);
        }
    }

    public void Warn(string message)
    {
        lock (_lock)
        {
            Console.Write("\x1b[33m!\x1b[0m "); // yellow bang
            Console.WriteLine(message);
        }
    }
}


