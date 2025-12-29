namespace MultiAgentSemanticKernel.Runtime;

public interface ICliWriter
{
    void Header(string text);
    void Info(string text);
    void AgentStart(string agentId, string agentName);
    void AgentResult(string agentName, string result);
    void UserInput(string input);
    void ToolStart(string agentName, string toolName, Dictionary<string, string> args);
    void RunnerResult(string result);
    void Warn(string message);
    void TurnSeparator(int turnNumber);
    void IterationSeparator(int iterationNumber);
}

public sealed class AnsiCliWriter : ICliWriter
{
    private static readonly Lock Lock = new();

    public void Header(string text)
    {
        lock (Lock)
        {
            Console.WriteLine();
            Console.Write("\x1b[1;36m"); // bold cyan
            Console.Write("═══ ");
            Console.Write(text);
            Console.Write(" ═══");
            Console.Write("\x1b[0m");
            Console.WriteLine();
            Console.WriteLine();
        }
    }

    public void Info(string text)
    {
        lock (Lock)
        {
            Console.Write("\x1b[2m"); // dim
            Console.Write("ℹ ");
            Console.Write("\x1b[0m");
            Console.WriteLine(text);
        }
    }

    public void AgentStart(string agentId, string agentName)
    {
        lock (Lock)
        {
            Console.WriteLine();
            Console.Write("\x1b[2m"); // dim
            Console.Write("→ ");
            Console.Write("\x1b[36m"); // cyan
            Console.Write(agentName);
            Console.Write("\x1b[0m");
            if (agentId != agentName)
            {
                Console.Write("  ");
                Console.Write($"({agentId})");
            }
            Console.WriteLine();
        }
    }

    public void AgentResult(string agentName, string result)
    {
        lock (Lock)
        {
            Console.Write("\x1b[32m"); // green
            Console.Write("[");
            Console.Write(agentName);
            Console.Write("]");
            Console.Write("\x1b[0m");
            Console.Write(" ");
            Console.WriteLine(result);
            Console.WriteLine();
        }
    }

    public void UserInput(string input)
    {
        lock (Lock)
        {
            Console.WriteLine();
            Console.Write("\x1b[1;94m"); // bold bright blue
            Console.Write("👤 User: ");
            Console.Write("\x1b[0m");
            Console.WriteLine(input);
            Console.WriteLine();
        }
    }

    public void ToolStart(string agentName, string toolName, Dictionary<string, string> args)
    {
        lock (Lock)
        {
            Console.Write("\x1b[2m"); // dim
            Console.Write("  🔧 ");
            Console.Write("\x1b[35m"); // magenta function
            Console.Write(toolName);
            Console.Write("\x1b[0m");
            Console.Write(" by ");
            Console.Write("\x1b[36m"); // cyan agent
            Console.Write(agentName);
            Console.Write("\x1b[0m");
            if (args.Count > 0)
            {
                Console.Write(" (");
                Console.Write(string.Join(", ", args.Select(kv => $"{kv.Key}={kv.Value}")));
                Console.Write(")");
            }
            Console.WriteLine();
        }
    }

    public void RunnerResult(string result)
    {
        lock (Lock)
        {
            Console.WriteLine();
            Console.Write("\x1b[36m"); // cyan label
            Console.Write("🏁 Result");
            Console.Write("\x1b[0m");
            Console.WriteLine();
            Console.WriteLine(result);
            Console.WriteLine();
        }
    }

    public void Warn(string message)
    {
        lock (Lock)
        {
            Console.Write("\x1b[33m⚠ \x1b[0m "); // yellow warning
            Console.WriteLine(message);
        }
    }

    public void TurnSeparator(int turnNumber)
    {
        lock (Lock)
        {
            Console.WriteLine();
            Console.Write("\x1b[2;36m"); // dim cyan
            Console.Write("─── Turn ");
            Console.Write(turnNumber);
            Console.Write(" ");
            Console.Write("─".PadRight(60, '─'));
            Console.Write("\x1b[0m");
            Console.WriteLine();
        }
    }

    public void IterationSeparator(int iterationNumber)
    {
        lock (Lock)
        {
            Console.WriteLine();
            Console.Write("\x1b[2;35m"); // dim magenta
            Console.Write("─── Iteration ");
            Console.Write(iterationNumber);
            Console.Write(" ");
            Console.Write("─".PadRight(56, '─'));
            Console.Write("\x1b[0m");
            Console.WriteLine();
        }
    }
}
