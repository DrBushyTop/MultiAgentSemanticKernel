using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Orchestration;
using Microsoft.SemanticKernel.Agents.Orchestration.Handoff;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using MultiAgentSemanticKernel.Runtime;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MultiAgentSemanticKernel.Runners;

public sealed class HandoffRunner(Kernel kernel, ILogger<HandoffRunner> logger, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            var defaultPrompt = "Ticket INC-1234: Customer checkout fails with NullReferenceException in Guid parsing.";
            logger.LogWarning("[Handoff] Using default prompt: {Prompt}", defaultPrompt);
            prompt = defaultPrompt;
        }
        else
        {
            logger.LogWarning("[Handoff] Starting with prompt: {Prompt}", prompt);
        }

        // --- Extract/seed an incident id for state (defaults if not found) ---
        var incidentId = Regex.Match(prompt, @"INC-\d+").Success
            ? Regex.Match(prompt, @"INC-\d+").Value
            : "INC-0000";

        // --- Tiny baton/state plugin ---
        var state = new MiniIncidentPlugin();
        state.Init(incidentId);
        var statePlugin = KernelPluginFactory.CreateFromObject(state, pluginName: null, loggerFactory: kernel.LoggerFactory);
        // Single Kernel instance is shared; add plugin once
        kernel.Plugins.Add(statePlugin);

        // --- Super-minimal agent contracts ---
        const string triageInstructions =
            @"You are TriageAgent. Set Severity (Sev1..Sev4) and a minimal Signal (free text).
Persist ONLY via MiniIncident.SetSeverity(id, severity, signal?).
If reproduction is needed → output 'HANDOFF: ReproAgent | need repro'.
Otherwise → output 'HANDOFF: FixPlanner'.
Never finalize.
Only respond with the result, no fluff, be concise.";

        const string reproInstructions =
            @"You are ReproAgent. Produce deterministic reproduction.
Persist via MiniIncident.SetRepro(id, status, blocker?) where status is Confirmed or Blocked.
If Confirmed → output 'HANDOFF: FixPlanner'.
If Blocked → output 'HANDOFF: TriageAgent | missing <x>'.
Only respond with the result, no fluff, be concise.";

        const string plannerInstructions =
            @"You are FixPlanner. Propose a plan with Action(Hotfix|Rollback|FlagFlip|Investigate) and Decision(Go|NoGo).
Persist via MiniIncident.SetPlan(id, action, decision).
If missing repro → output 'HANDOFF: ReproAgent'.
When complete, call MiniIncident.MarkDone(id) and reply 'FINAL: plan=<Decision> action=<Action>'.
Only respond with the result, no fluff, be concise.";

        var triage = new ChatCompletionAgent { Name = "TriageAgent", Instructions = triageInstructions, Kernel = kernel, LoggerFactory = kernel.LoggerFactory };
        var repro = new ChatCompletionAgent { Name = "ReproAgent", Instructions = reproInstructions, Kernel = kernel, LoggerFactory = kernel.LoggerFactory };
        var planner = new ChatCompletionAgent { Name = "FixPlanner", Instructions = plannerInstructions, Kernel = kernel, LoggerFactory = kernel.LoggerFactory };

        // --- Logging callback with handoff/final markers ---
        ValueTask ResponseCallback(ChatMessageContent response)
        {
            var author = string.IsNullOrWhiteSpace(response.AuthorName) ? "Agent" : response.AuthorName;
            var content = response.Content ?? string.Empty;

            if (content.StartsWith("HANDOFF:", StringComparison.OrdinalIgnoreCase))
            {
                cli.Info($"🔁 {author} → {content}");
            }
            else if (content.StartsWith("FINAL:", StringComparison.OrdinalIgnoreCase))
            {
                cli.Info($"✅ {author} → {content}");
            }
            else
            {
                cli.AgentResult(author!, content);
            }

            return ValueTask.CompletedTask;
        }

        // --- Minimal handoff graph: 5 edges ---
        var handoffs = OrchestrationHandoffs
            .StartWith(triage)
            .Add(triage, repro, planner)
            .Add(repro, planner, "Repro confirmed")
            .Add(repro, triage, "Repro blocked, need info")
            .Add(planner, repro, "Plan needs confirmed repro");

        var orchestration = new HandoffOrchestration(handoffs, triage, repro, planner)
        {
            LoggerFactory = kernel.LoggerFactory,
            ResponseCallback = ResponseCallback,
        };

        var runtime = new InProcessRuntime();
        await runtime.StartAsync();

        // Kick off with the original prompt; agents use MiniIncident.* to persist state under incidentId.
        var result = await orchestration.InvokeAsync(prompt + $"\n\n[incidentId: {incidentId}]", runtime);
        var output = await result.GetValueAsync(TimeSpan.FromSeconds(120));

        cli.Info("# RESULT");
        cli.Info(output);

        // Print compact state summary for the demo
        cli.Info("# STATE");
        cli.Info(state.Summarize(incidentId));

        await runtime.RunUntilIdleAsync();
    }

    /// <summary>
    /// Tiny baton with 4 fields: Severity, Repro(Status/Blocker), Plan(Action/Decision), Done.
    /// Exposed as SK plugin functions prefixed with MiniIncident.* for agents to call.
    /// </summary>
    private sealed class MiniIncidentPlugin
    {
        private readonly Dictionary<string, S> _db = new();

        public void Init(string id) => _db[id] = new S();

        [KernelFunction]
        public void SetSeverity(string id, string severity, string? signal = null)
        {
            var s = Get(id);
            s.Severity = severity ?? s.Severity;
            s.Signal = string.IsNullOrWhiteSpace(signal) ? s.Signal : signal;
        }

        [KernelFunction]
        public void SetRepro(string id, string status, string? blocker = null)
        {
            var s = Get(id);
            s.ReproStatus = status ?? s.ReproStatus;
            s.Blocker = string.IsNullOrWhiteSpace(blocker) ? s.Blocker : blocker;
        }

        [KernelFunction]
        public void SetPlan(string id, string action, string decision)
        {
            var s = Get(id);
            s.Action = action ?? s.Action;
            s.Decision = decision ?? s.Decision;
        }

        [KernelFunction]
        public void MarkDone(string id)
        {
            Get(id).Done = true;
        }

        public string Summarize(string id)
        {
            var s = Get(id);
            return $"Severity={s.Severity}, Signal={s.Signal}, Repro={s.ReproStatus}{(string.IsNullOrWhiteSpace(s.Blocker) ? "" : $"(Blocker={s.Blocker})")}, Action={s.Action}, Decision={s.Decision}, Done={s.Done}";
        }

        private S Get(string id) => _db.TryGetValue(id, out var s) ? s : (_db[id] = new S());

        private sealed class S
        {
            public string Severity { get; set; } = "";
            public string? Signal { get; set; }
            public string ReproStatus { get; set; } = "None";
            public string? Blocker { get; set; }
            public string Action { get; set; } = "";
            public string Decision { get; set; } = "Pending";
            public bool Done { get; set; }
        }
    }
}
