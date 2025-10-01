using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents.Magentic;
using Microsoft.SemanticKernel.Agents.Magentic.Internal;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MultiAgentSemanticKernel.Runtime;

// NOTE: This is a very ugly hack to get the logging for the demo 15 minutes before go time.

//
// Summary:
//     A LoggingStandardMagenticManager that provides orchestration
//     logic for managing magentic agents, including preparing facts, plans, ledgers,
//     evaluating progress, and generating a final answer.
public sealed class LoggingStandardMagenticManager : MagenticManager
{
    private static readonly Kernel EmptyKernel = new Kernel();

    private readonly IChatCompletionService _service;

    private readonly PromptExecutionSettings _executionSettings;

    private string _facts = string.Empty;

    private string _plan = string.Empty;

    //
    // Summary:
    //     Initializes a new instance of the Microsoft.SemanticKernel.Agents.Magentic.StandardMagenticManager
    //     class.
    //
    // Parameters:
    //   service:
    //     The chat completion service to use for generating responses.
    //
    //   executionSettings:
    //     The prompt execution settings to use for the chat completion service.
    public LoggingStandardMagenticManager(IChatCompletionService service, PromptExecutionSettings executionSettings)
    {
        if (!executionSettings.SupportsResponseFormat())
        {
            throw new KernelException("Unable to proceed with PromptExecutionSettings that does not support structured JSON output.");
        }

        if (executionSettings.IsFrozen)
        {
            throw new KernelException("Unable to proceed with frozen PromptExecutionSettings.");
        }

        _service = service;
        _executionSettings = executionSettings;
        _executionSettings.SetResponseFormat<MagenticProgressLedger>();
    }

    public override async ValueTask<IList<ChatMessageContent>> PlanAsync(MagenticManagerContext context, CancellationToken cancellationToken)
    {
        _facts = await PrepareTaskFactsAsync(context, MagenticPrompts.NewFactsTemplate, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        _plan = await PrepareTaskPlanAsync(context, MagenticPrompts.NewPlanTemplate, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        var prevColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"ℹ️  Manager Planning!");
        Console.WriteLine($"ℹ️  Manager facts: {_facts}");
        Console.WriteLine($"ℹ️  Manager plan: {_plan}");
        Console.ForegroundColor = prevColor;

        return await PrepareTaskLedgerAsync(context, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    public override async ValueTask<IList<ChatMessageContent>> ReplanAsync(MagenticManagerContext context, CancellationToken cancellationToken)
    {
        _facts = await PrepareTaskFactsAsync(context, MagenticPrompts.RefreshFactsTemplate, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        _plan = await PrepareTaskPlanAsync(context, MagenticPrompts.RefreshPlanTemplate, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        var prevColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"ℹ️  Manager RePlanning!");
        Console.WriteLine($"ℹ️  Manager facts: {_facts}");
        Console.WriteLine($"ℹ️  Manager plan: {_plan}");
        Console.ForegroundColor = prevColor;

        return await PrepareTaskLedgerAsync(context, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    public override async ValueTask<MagenticProgressLedger> EvaluateTaskProgressAsync(MagenticManagerContext context, CancellationToken cancellationToken = default(CancellationToken))
    {
        ChatHistory chatHistory = new ChatHistory();
        foreach (ChatMessageContent item in context.History)
        {
            chatHistory.Add(item);
        }

        ChatHistory internalChat = chatHistory;
        KernelArguments arguments = new KernelArguments
        {
            {
                "task",
                FormatInputTask(context.Task)
            },
            {
                "team",
                context.Team.FormatNames()
            }
        };
        return JsonSerializer.Deserialize<MagenticProgressLedger>(await GetResponseAsync(internalChat, MagenticPrompts.StatusTemplate, arguments, _executionSettings, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)) ?? throw new InvalidDataException("Message content does not align with requested type: MagenticProgressLedger.");
    }

    public override async ValueTask<ChatMessageContent> PrepareFinalAnswerAsync(MagenticManagerContext context, CancellationToken cancellationToken = default(CancellationToken))
    {
        KernelArguments arguments = new KernelArguments { { "task", context.Task } };
        string content = await GetResponseAsync(context.History, MagenticPrompts.AnswerTemplate, arguments, null, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        // TODO: move these to the cli writer
        var prevColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"ℹ️  Manager Preparing final answer");
        Console.ForegroundColor = prevColor;
        return new ChatMessageContent(AuthorRole.Assistant, content);
    }

    private async ValueTask<string> PrepareTaskFactsAsync(MagenticManagerContext context, IPromptTemplate promptTemplate, CancellationToken cancellationToken = default(CancellationToken))
    {
        KernelArguments arguments = new KernelArguments
        {
            {
                "task",
                FormatInputTask(context.Task)
            },
            { "facts", _facts }
        };
        return await GetResponseAsync(context.History, promptTemplate, arguments, null, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async ValueTask<string> PrepareTaskPlanAsync(MagenticManagerContext context, IPromptTemplate promptTemplate, CancellationToken cancellationToken = default(CancellationToken))
    {
        KernelArguments arguments = new KernelArguments {
        {
            "team",
            context.Team.FormatList()
        } };
        return await GetResponseAsync(context.History, promptTemplate, arguments, null, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async ValueTask<IList<ChatMessageContent>> PrepareTaskLedgerAsync(MagenticManagerContext context, CancellationToken cancellationToken = default(CancellationToken))
    {
        KernelArguments arguments = new KernelArguments
        {
            {
                "task",
                FormatInputTask(context.Task)
            },
            {
                "team",
                context.Team.FormatList()
            },
            { "facts", _facts },
            { "plan", _plan }
        };
        string content = await GetMessageAsync(MagenticPrompts.LedgerTemplate, arguments).ConfigureAwait(continueOnCapturedContext: false);
        return new List<ChatMessageContent>(1)
        {
            new ChatMessageContent(AuthorRole.System, content)
        };
    }

    private async ValueTask<string> GetMessageAsync(IPromptTemplate template, KernelArguments arguments)
    {
        return await template.RenderAsync(EmptyKernel, arguments).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task<string> GetResponseAsync(IReadOnlyList<ChatMessageContent> internalChat, IPromptTemplate template, KernelArguments arguments, PromptExecutionSettings? executionSettings, CancellationToken cancellationToken = default(CancellationToken))
    {
        ChatHistory chatHistory = new ChatHistory();
        foreach (ChatMessageContent item in internalChat)
        {
            chatHistory.Add(item);
        }

        ChatHistory history = chatHistory;
        string content = await GetMessageAsync(template, arguments).ConfigureAwait(continueOnCapturedContext: false);
        history.Add(new ChatMessageContent(AuthorRole.User, content));
        return (await _service.GetChatMessageContentAsync(history, executionSettings, null, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)).Content ?? string.Empty;
    }

    private string FormatInputTask(IReadOnlyList<ChatMessageContent> inputTask)
    {
        return string.Join("\n", inputTask.Select((ChatMessageContent m) => m.Content ?? ""));
    }
}

internal sealed class MagenticPrompts
{
    public static class Parameters
    {
        public const string Task = "task";

        public const string Team = "team";

        public const string Names = "names";

        public const string Facts = "facts";

        public const string Plan = "plan";

        public const string Ledger = "ledger";
    }

    private static class Templates
    {
        public const string AnalyzeFacts = "Respond to the pre-survey in response the following user request:\n\n{{$task}}\n\nHere is the pre-survey:\n\n    1. Please list any specific facts or figures that are GIVEN in the request itself. It is possible that\n       there are none.\n    2. Please list any facts that may need to be looked up, and WHERE SPECIFICALLY they might be found.\n       In some cases, authoritative sources are mentioned in the request itself.\n    3. Please list any facts that may need to be derived (e.g., via logical deduction, simulation, or computation)\n    4. Please list any facts that are recalled from memory, hunches, well-reasoned guesses, etc.\n\nWhen answering this survey, keep in mind that \"facts\" will typically be specific names, dates, statistics, etc.\n\nYour answer MUST use these headings:\n\n    1. GIVEN OR VERIFIED FACTS\n    2. FACTS TO LOOK UP\n    3. FACTS TO DERIVE\n    4. EDUCATED GUESSES\n\nDO NOT include any other headings or sections in your response. DO NOT list next steps or plans.";

        public const string UpdateFacts = "As a reminder, we are working to solve the following request:\n\n{{$task}}\n\nIt's clear we aren't making as much progress as we would like, but we may have learned something new.\nPlease rewrite the following fact sheet, updating it to include anything new we have learned that may be helpful.\n\nExample edits can include (but are not limited to) adding new guesses, moving educated guesses to verified facts\nif appropriate, etc. Updates may be made to any section of the fact sheet, and more than one section of the fact\nsheet can be edited. This is an especially good time to update educated guesses, so please at least add or update\none educated guess or hunch, and explain your reasoning.\n\nHere is the old fact sheet:\n\n{{$facts}}                ";

        public const string AnalyzePlan = "To address this request we have assembled the following team:\n\n{{$team}}\n\nDefine the most effective plan that addresses the user request.\n\nEnsure that the plan:\n\n- Is formatted as plan as a markdown list of sequential steps with each top-level bullet-point as: \"{Agent Name}: {Actions, goals, or sub-list}\".\n- Resolves any ambiguity or clarification of the user request\n- Only includes the team members that are required to respond to the request.\n- Excludes extra steps that are not necessary and slow down the process.\n- Does not seek final confirmation from the user.";

        public const string UpdatePlan = "Please briefly explain what went wrong on this last run (the root\ncause of the failure), and then come up with a new plan that takes steps and/or includes hints to overcome prior\nchallenges and especially avoids repeating the same mistakes. As before, the new plan should be concise, be expressed\nin bullet-point form, and consider the following team composition (do not involve any other outside people since we\ncannot contact anyone else):\n\n{{$team}}                ";

        public const string GenerateLedger = "We are working to address the following user request:\n\n{{$task}}\n\n\nTo answer this request we have assembled the following team:\n\n{{$team}}\n\n\nHere is an initial fact sheet to consider:\n\n{{$facts}}\n\n\nHere is the plan to follow as best as possible:\n\n{{$plan}}";

        public const string AnalyzeStatus = "Recall we are working on the following request:\n\n{{$task}}\n\nAnd we have assembled the following team:\n\n{{$team}}\n\nTo make progress on the request, please answer the following questions, including necessary reasoning:\n\n    - Is the request fully satisfied?  (True if complete, or False if the original request has yet to be SUCCESSFULLY and FULLY addressed)\n    - Are we in a loop where we are repeating the same requests and / or getting the same responses as before?\n      Loops can span multiple responses.\n    - Are we making forward progress? (True if just starting, or recent messages are adding value.\n      False if recent messages show evidence of being stuck in a loop or if there is evidence of the inability to proceed)\n    - Which team member is needed to respond next? (Select only from: {{$names}}).\n      Always consider then initial plan but you may deviate from this plan as appropriate based on the conversation.\n    - Do not seek final confirmation from the user if the request is fully satisfied.\n    - What direction would you give this team member? (Always phrase in the 2nd person, speaking directly to them, and\n      include any specific information they may need)                    ";

        public const string FinalAnswer = "Synthesize a complete response to the user request using markdown format:\n{{$task}}\n\nThe complete response MUST:\n- Consider the entire conversation without incorporating information that changed or was corrected\n- NEVER include any new information not already present in the conversation\n- Capture verbatim content instead of summarizing\n- Directly address the request without narrating how the conversation progressed\n- Incorporate images specified in conversation responses\n- Include all citations or references\n- Be phrased to directly address the user";
    }

    private static readonly KernelPromptTemplateFactory TemplateFactory = new KernelPromptTemplateFactory
    {
        AllowDangerouslySetContent = true
    };

    public static readonly IPromptTemplate NewFactsTemplate = InitializePrompt("Respond to the pre-survey in response the following user request:\n\n{{$task}}\n\nHere is the pre-survey:\n\n    1. Please list any specific facts or figures that are GIVEN in the request itself. It is possible that\n       there are none.\n    2. Please list any facts that may need to be looked up, and WHERE SPECIFICALLY they might be found.\n       In some cases, authoritative sources are mentioned in the request itself.\n    3. Please list any facts that may need to be derived (e.g., via logical deduction, simulation, or computation)\n    4. Please list any facts that are recalled from memory, hunches, well-reasoned guesses, etc.\n\nWhen answering this survey, keep in mind that \"facts\" will typically be specific names, dates, statistics, etc.\n\nYour answer MUST use these headings:\n\n    1. GIVEN OR VERIFIED FACTS\n    2. FACTS TO LOOK UP\n    3. FACTS TO DERIVE\n    4. EDUCATED GUESSES\n\nDO NOT include any other headings or sections in your response. DO NOT list next steps or plans.");

    public static readonly IPromptTemplate RefreshFactsTemplate = InitializePrompt("Respond to the pre-survey in response the following user request:\n\n{{$task}}\n\nHere is the pre-survey:\n\n    1. Please list any specific facts or figures that are GIVEN in the request itself. It is possible that\n       there are none.\n    2. Please list any facts that may need to be looked up, and WHERE SPECIFICALLY they might be found.\n       In some cases, authoritative sources are mentioned in the request itself.\n    3. Please list any facts that may need to be derived (e.g., via logical deduction, simulation, or computation)\n    4. Please list any facts that are recalled from memory, hunches, well-reasoned guesses, etc.\n\nWhen answering this survey, keep in mind that \"facts\" will typically be specific names, dates, statistics, etc.\n\nYour answer MUST use these headings:\n\n    1. GIVEN OR VERIFIED FACTS\n    2. FACTS TO LOOK UP\n    3. FACTS TO DERIVE\n    4. EDUCATED GUESSES\n\nDO NOT include any other headings or sections in your response. DO NOT list next steps or plans.");

    public static readonly IPromptTemplate NewPlanTemplate = InitializePrompt("To address this request we have assembled the following team:\n\n{{$team}}\n\nDefine the most effective plan that addresses the user request.\n\nEnsure that the plan:\n\n- Is formatted as plan as a markdown list of sequential steps with each top-level bullet-point as: \"{Agent Name}: {Actions, goals, or sub-list}\".\n- Resolves any ambiguity or clarification of the user request\n- Only includes the team members that are required to respond to the request.\n- Excludes extra steps that are not necessary and slow down the process.\n- Does not seek final confirmation from the user.");

    public static readonly IPromptTemplate RefreshPlanTemplate = InitializePrompt("To address this request we have assembled the following team:\n\n{{$team}}\n\nDefine the most effective plan that addresses the user request.\n\nEnsure that the plan:\n\n- Is formatted as plan as a markdown list of sequential steps with each top-level bullet-point as: \"{Agent Name}: {Actions, goals, or sub-list}\".\n- Resolves any ambiguity or clarification of the user request\n- Only includes the team members that are required to respond to the request.\n- Excludes extra steps that are not necessary and slow down the process.\n- Does not seek final confirmation from the user.");

    public static readonly IPromptTemplate LedgerTemplate = InitializePrompt("We are working to address the following user request:\n\n{{$task}}\n\n\nTo answer this request we have assembled the following team:\n\n{{$team}}\n\n\nHere is an initial fact sheet to consider:\n\n{{$facts}}\n\n\nHere is the plan to follow as best as possible:\n\n{{$plan}}");

    public static readonly IPromptTemplate StatusTemplate = InitializePrompt("Recall we are working on the following request:\n\n{{$task}}\n\nAnd we have assembled the following team:\n\n{{$team}}\n\nTo make progress on the request, please answer the following questions, including necessary reasoning:\n\n    - Is the request fully satisfied?  (True if complete, or False if the original request has yet to be SUCCESSFULLY and FULLY addressed)\n    - Are we in a loop where we are repeating the same requests and / or getting the same responses as before?\n      Loops can span multiple responses.\n    - Are we making forward progress? (True if just starting, or recent messages are adding value.\n      False if recent messages show evidence of being stuck in a loop or if there is evidence of the inability to proceed)\n    - Which team member is needed to respond next? (Select only from: {{$names}}).\n      Always consider then initial plan but you may deviate from this plan as appropriate based on the conversation.\n    - Do not seek final confirmation from the user if the request is fully satisfied.\n    - What direction would you give this team member? (Always phrase in the 2nd person, speaking directly to them, and\n      include any specific information they may need)                    ");

    public static readonly IPromptTemplate AnswerTemplate = InitializePrompt("Synthesize a complete response to the user request using markdown format:\n{{$task}}\n\nThe complete response MUST:\n- Consider the entire conversation without incorporating information that changed or was corrected\n- NEVER include any new information not already present in the conversation\n- Capture verbatim content instead of summarizing\n- Directly address the request without narrating how the conversation progressed\n- Incorporate images specified in conversation responses\n- Include all citations or references\n- Be phrased to directly address the user");

    private static IPromptTemplate InitializePrompt(string template)
    {
        PromptTemplateConfig templateConfig = new PromptTemplateConfig
        {
            Template = template
        };
        return TemplateFactory.Create(templateConfig);
    }
}