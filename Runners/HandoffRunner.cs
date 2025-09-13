using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Orchestration;
using Microsoft.SemanticKernel.Agents.Orchestration.Handoff;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using Microsoft.SemanticKernel.ChatCompletion;
using MultiAgentSemanticKernel.Runtime;
using MultiAgentSemanticKernel.Plugins;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MultiAgentSemanticKernel.Runners;

public sealed class HandoffRunner(Kernel kernel, ILogger<HandoffRunner> logger, ICliWriter cli)
{
    public async Task RunAsync(string prompt)
    {
        // Import ops tools for incident management where applicable
        kernel.ImportPluginFromType<OpsPlugin>();

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
            @"You are TriageAgent. Your job is to set initial severity and then transfer control using the orchestration handoff tool.

Rules:
- Persist ONLY via MiniIncidentPlugin.SetSeverity(id, severity, signal?).
- The incident id is provided as [incidentId: INC-####] in the last user message; pass that exact id to all calls.
- After setting severity, decide:
  • If a reproduction is required → hand off to ReproAgent.
  • If a deterministic repro already exists → hand off to FixPlanner.
- Do not ask the end user questions; proceed autonomously.
- When helpful, use Ops tools: Deploy_Status(service) to check current deploy state, Comms_Post(channel,message) to notify stakeholders.
- Use the provided handoff tool to transfer to the chosen agent; do not terminate the conversation yourself.";

        const string reproInstructions =
            @"You are ReproAgent. Produce a deterministic reproduction for the incident.

Rules:
- Persist via MiniIncidentPlugin.SetRepro(id, status, blocker?) where status is Confirmed or Blocked.
- The incident id is provided as [incidentId: INC-####] in the last user message; pass that exact id to all calls.
- If status is Confirmed → hand off to FixPlanner.
- If status is Blocked → hand off to TriageAgent to resolve blockers (do not ask the end user).
- Use the orchestration handoff tool to transfer; do not terminate.";

        const string plannerInstructions =
            @"You are FixPlanner. Propose a plan with Action(Hotfix|Rollback|FlagFlip|Investigate) and Decision(Go|NoGo).

Rules:
- Persist via MiniIncidentPlugin.SetPlan(id, action, decision).
- The incident id is provided as [incidentId: INC-####] in the last user message; pass that exact id to all calls.
- When complete, call MiniIncidentPlugin.MarkDone(id) and reply EXACTLY:
  FINAL: plan=<Decision> action=<Action>
- Where appropriate, call Deploy_Rollback(service,toVersion?) or FeatureFlags_Get(key) to support the plan.
- Do not hand off after finalization.";

        var triage = AgentUtils.Create(name: "TriageAgent", description: "Agent responsible for Triaging", instructions: triageInstructions, kernel: kernel);
        var repro = AgentUtils.Create(name: "ReproAgent", description: "Agent responsible for Reproducing Bugs", instructions: reproInstructions, kernel: kernel);
        var planner = AgentUtils.Create(name: "FixPlanner", description: "Agent Responsible for planning a Fix", instructions: plannerInstructions, kernel: kernel);

        // --- Logging callback with handoff/final markers ---
        ValueTask ResponseCallback(ChatMessageContent response)
        {
            var author = string.IsNullOrWhiteSpace(response.AuthorName) ? "Agent" : response.AuthorName;
            var content = response.Content ?? string.Empty;

            return ValueTask.CompletedTask;
        }

        // --- Minimal handoff graph: 5 edges ---
        var handoffs = OrchestrationHandoffs
            .StartWith(triage)
            .Add(triage, repro, planner)
            .Add(planner, repro, "Transfer to this agent when plan needs confirmed repro")
            .Add(repro, planner, "Transfer to this agent when repro confirmed")
            .Add(repro, triage, "Transfer to this agent when repro blocked, need info");

        var orchestration = new HandoffOrchestration(handoffs, triage, repro, planner)
        {
            LoggerFactory = kernel.LoggerFactory,
            ResponseCallback = ResponseCallback,
        };

        var runtime = new InProcessRuntime();
        await runtime.StartAsync();

        // Kick off with the original prompt; agents use MiniIncidentPlugin.* to persist state under incidentId.
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