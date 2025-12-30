namespace MultiAgentSemanticKernel.Runtime;

public interface ICliWriter
{
    void Header(string text);
    void Info(string text);
    void AgentStart(string agentId, string agentName);
    void AgentSelected(string managerName, string agentName);
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
            Console.Write("→ ");
            Console.WriteLine(text);
            Console.Write("\x1b[0m");
        }
    }

    public void AgentStart(string agentId, string agentName)
    {
        lock (Lock)
        {
            Console.WriteLine();
            Console.Write("\x1b[2m"); // dim
            Console.Write("→ ");
            Console.Write("\x1b[96m"); // bright cyan
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

    public void AgentSelected(string managerName, string agentName)
    {
        lock (Lock)
        {
            Console.WriteLine();
            Console.Write("\x1b[2m"); // dim
            Console.Write("[");
            Console.Write("\x1b[93m"); // bright yellow for manager
            Console.Write(managerName);
            Console.Write("\x1b[0m\x1b[2m"); // reset to dim
            Console.Write("] selected → ");
            Console.Write("\x1b[96m"); // bright cyan for agent
            Console.Write(agentName);
            Console.Write("\x1b[0m");
            Console.WriteLine();
        }
    }

    public void AgentResult(string agentName, string result)
    {
        lock (Lock)
        {
            Console.Write("\x1b[92m"); // bright green
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
            Console.Write("  🔧 ");
            
            // Split toolName into plugin.function if it contains a dash (plugin-function format)
            var parts = toolName.Split('-', 2);
            if (parts.Length == 2)
            {
                Console.Write("\x1b[96m"); // bright cyan plugin
                Console.Write(parts[0]);
                Console.Write("\x1b[0m");
                Console.Write(".");
                Console.Write("\x1b[95m"); // bright magenta function
                Console.Write(parts[1]);
                Console.Write("\x1b[0m");
            }
            else
            {
                Console.Write("\x1b[95m"); // bright magenta function
                Console.Write(toolName);
                Console.Write("\x1b[0m");
            }
            
            Console.Write("\x1b[2m"); // dim for "by"
            Console.Write(" by ");
            Console.Write("\x1b[0m");
            Console.Write("\x1b[96m"); // bright cyan agent
            Console.Write(agentName);
            Console.Write("\x1b[0m");
            if (args.Count > 0)
            {
                Console.Write("\x1b[2m"); // dim for args
                Console.Write(" (");
                Console.Write(string.Join(", ", args.Select(kv => $"{kv.Key}={kv.Value}")));
                Console.Write(")");
                Console.Write("\x1b[0m");
            }
            Console.WriteLine();
        }
    }

    public void RunnerResult(string result)
    {
        lock (Lock)
        {
            Console.WriteLine();
            Console.Write("\x1b[96m"); // bright cyan label
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
