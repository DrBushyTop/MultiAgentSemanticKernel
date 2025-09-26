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
            var defaultPrompt = "Ticket INC-1234: Product catalog item 325 fails to load.";
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
            @"You are TriageAgent. Set the incident severity and hand off to the appropriate agents to repro, create a plan for a fix, and implement the fix.

Rules:
- Persist ONLY via MiniIncidentPlugin.SetSeverity(id, severity, signal?).
- The incident id is provided as [incidentId: INC-####] in the last user message; pass that exact id to all calls.
- Do not ask the end user questions; proceed autonomously.
- End the task only when the incident has been fixed.";

        const string reproInstructions =
            @"You are ReproAgent. Produce a deterministic reproduction step list for the incident. Imagine you have access to the website and can reproduce the bug.

Rules:
- Persist via MiniIncidentPlugin.SetRepro(id, status, blocker?) where status is Confirmed or Blocked.
- The incident id is provided as [incidentId: INC-####] in the last user message; pass that exact id to all calls.
- Do not end the task";

        const string plannerInstructions =
            @"You are FixPlanner. Propose a plan with Action(Hotfix|Rollback|FlagFlip|Investigate) and Decision(Go|NoGo).

Rules:
- Persist via MiniIncidentPlugin.SetPlan(id, action, decision).
- The incident id is provided as [incidentId: INC-####] in the last user message; pass that exact id to all calls.
- Do not end the task";

        const string fixerInstructions =
            @"You are FixerAgent. Your job is to fix the incident based on the plan. Use the MiniIncidentPlugin.ImplementFix(id, fixDescription) to implement the fix.
            - Do not end the task";

        var triage = AgentUtils.Create(name: "TriageAgent", description: "Agent responsible for Triaging", instructions: triageInstructions, kernel: kernel);
        var repro = AgentUtils.Create(name: "ReproAgent", description: "Agent responsible for Reproducing Bugs", instructions: reproInstructions, kernel: kernel);
        var planner = AgentUtils.Create(name: "FixPlanner", description: "Agent Responsible for planning a Fix based on the repro", instructions: plannerInstructions, kernel: kernel);
        var fixer = AgentUtils.Create(name: "FixerAgent", description: "Agent responsible for Implementing Bug Fixes based on the plan", instructions: fixerInstructions, kernel: kernel);

        // --- Logging callback with handoff/final markers ---
        var ResponseCallback = AgentResponseCallbacks.Create(cli);

        // --- Minimal handoff graph: 5 edges ---
        var handoffs = OrchestrationHandoffs
            .StartWith(triage)
            .Add(triage, repro, planner, fixer)
            .Add(repro, triage, "Transfer to this agent once you've reproduced the bug")
            .Add(planner, triage, "Transfer to this agent once you've created a plan for a fix")
            .Add(fixer, triage, "Transfer to this agent once you've implemented the fix");

        var orchestration = new HandoffOrchestration(handoffs, triage, repro, planner, fixer)
        {
            LoggerFactory = kernel.LoggerFactory,
            ResponseCallback = ResponseCallback,
        };

        var runtime = new InProcessRuntime();
        await runtime.StartAsync();

        // Kick off with the original prompt; agents use MiniIncidentPlugin.* to persist state under incidentId.
        var result = await orchestration.InvokeAsync(prompt + $"\n\n[incidentId: {incidentId}]", runtime);
        var output = await result.GetValueAsync(TimeSpan.FromSeconds(120));

        // Print compact state summary for the demo
        cli.Info("####### RESULT #######\n");
        cli.Info(output);
        cli.Info("# STATE");
        cli.Info(state.GetIncidentState(incidentId));

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
        public void IpmlementFix(string id, string fixDescription)
        {
            Get(id).Fix = fixDescription;
        }

        [KernelFunction]
        public void MarkDone(string id)
        {
            Get(id).Done = true;
        }

        public string GetIncidentState(string id)
        {
            var s = Get(id);
            return $"Severity={s.Severity}, Signal={s.Signal}, Repro={s.ReproStatus}{(string.IsNullOrWhiteSpace(s.Blocker) ? "" : $"(Blocker={s.Blocker})")}, Action={s.Action}, Decision={s.Decision}, Fix={s.Fix}, Done={s.Done}";
        }

        private S Get(string id) => _db.TryGetValue(id, out var s) ? s : (_db[id] = new S());

        private sealed class S
        {
            public string Severity { get; set; } = "";
            public string? Signal { get; set; }
            public string ReproStatus { get; set; } = "None";
            public string? Blocker { get; set; }
            public string Action { get; set; } = "";
            public string Fix { get; set; } = "";
            public string Decision { get; set; } = "Pending";
            public bool Done { get; set; }
        }
    }
}