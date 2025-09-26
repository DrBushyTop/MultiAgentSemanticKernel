using System.Runtime.CompilerServices;

namespace MultiAgentSemanticKernel.Runtime;

public interface ICliWriter
{
    void AgentStart(string agentName, string? detail = null);
    void AgentResult(string agentName, string result);
    void UserInput(string input);
    void ToolStart(string agentName, string plugin, string function);
    void ToolEnd(string agentName, string plugin, string function, bool success);
    void RunnerResult(string result);
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
            Console.Write("💬 ");
            Console.Write(agentName);
            Console.Write("\x1b[0m");
            Console.Write(" ");
            Console.Write("\x1b[2m"); // dim timestamp
            Console.Write("[");
            Console.Write(DateTime.Now.ToString("HH:mm:ss"));
            Console.Write("]");
            Console.Write("\x1b[0m");
            Console.WriteLine();
            Console.WriteLine(result);
            Console.WriteLine();
        }
    }

    public void UserInput(string input)
    {
        lock (_lock)
        {
            Console.Write("\x1b[94m"); // bright blue
            Console.Write("⌨️  ");
            Console.Write("UserInput");
            Console.Write("\x1b[0m");
            Console.WriteLine();
            Console.WriteLine(input);
            Console.WriteLine();
        }
    }

    public void ToolStart(string agentName, string plugin, string function)
    {
        lock (_lock)
        {
            Console.Write("\x1b[2m"); // dim
            Console.Write("🔧 ");
            Console.Write("\x1b[36m"); // cyan plugin
            Console.Write(plugin);
            Console.Write("\x1b[0m");
            Console.Write(".");
            Console.Write("\x1b[35m"); // magenta function
            Console.Write(function);
            Console.Write("\x1b[0m");
            Console.Write(" by ");
            Console.Write("\x1b[36m"); // cyan agent
            Console.Write(agentName);
            Console.Write("\x1b[0m");
            Console.WriteLine();
        }
    }

    public void ToolEnd(string agentName, string plugin, string function, bool success)
    {
        lock (_lock)
        {
            Console.Write(success ? "\x1b[32m" : "\x1b[31m"); // green or red
            Console.Write(success ? "✔ " : "✖ ");
            Console.Write("\x1b[2m"); // dim
            Console.Write("🔧 ");
            Console.Write("\x1b[36m"); // cyan plugin
            Console.Write(plugin);
            Console.Write("\x1b[0m");
            Console.Write(".");
            Console.Write("\x1b[35m"); // magenta function
            Console.Write(function);
            Console.Write("\x1b[0m");
            Console.Write(" by ");
            Console.Write("\x1b[36m"); // cyan agent
            Console.Write(agentName);
            Console.Write("\x1b[0m");
            Console.WriteLine();
        }
    }

    public void RunnerResult(string result)
    {
        lock (_lock)
        {
            Console.Write("\x1b[36m"); // cyan label
            Console.Write("🏁 Runner Result");
            Console.Write("\x1b[0m");
            Console.WriteLine();
            Console.WriteLine(result);
            Console.WriteLine();
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