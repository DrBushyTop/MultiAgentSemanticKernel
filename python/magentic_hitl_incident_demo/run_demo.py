from __future__ import annotations

import argparse
import asyncio
import json
import os
from collections.abc import AsyncIterable
from dataclasses import dataclass
from pathlib import Path
from typing import cast

from agent_framework import (
    Agent,
    AgentResponseUpdate,
    Message,
    WorkflowEvent,
)
from agent_framework.azure import AzureOpenAIChatClient
from agent_framework.orchestrations import (
    MagenticBuilder,
    MagenticPlanReviewRequest,
    MagenticPlanReviewResponse,
    MagenticProgressLedger,
)
from azure.identity import DefaultAzureCredential

# ---------------------------------------------------------------------------
# ANSI helpers — mirrors the style used in Runtime/CliWriter.cs
# ---------------------------------------------------------------------------
_R = "\x1b[0m"  # reset


def _header(text: str) -> None:
    """Bold cyan banner: ═══ text ═══"""
    print(f"\n\x1b[1;36m═══ {text} ═══{_R}\n")


def _info(text: str) -> None:
    """Dim arrow + text for metadata lines."""
    print(f"\x1b[2m→ {text}{_R}")


def _agent_start(name: str) -> None:
    """Dim arrow + bright-cyan agent name header before streaming tokens."""
    print(f"\n\x1b[2m→ \x1b[96m{name}{_R}")


def _agent_token(text: str, *, end: str = "", flush: bool = False) -> None:
    """Streaming token — plain text so agent output stays readable."""
    print(text, end=end, flush=flush)


def _tool_start(agent_name: str, tool_name: str, args: dict[str, str]) -> None:
    """Render tool-call line with the same structure as Runtime/CliWriter.cs."""
    print()
    print("  🔧 ", end="")

    parts = tool_name.split("-", 1)
    if len(parts) == 2:
        print(f"\x1b[96m{parts[0]}{_R}.\x1b[95m{parts[1]}{_R}", end="")
    else:
        print(f"\x1b[95m{tool_name}{_R}", end="")

    print("\x1b[2m by \x1b[0m", end="")
    print(f"\x1b[96m{agent_name}{_R}", end="")
    if args:
        rendered = ", ".join(f"{k}={v}" for k, v in args.items())
        print(f"\x1b[2m ({rendered}){_R}", end="")
    print()


def _runner_result(text: str) -> None:
    """🏁 bright-cyan label + body text."""
    print(f"\n\x1b[96m🏁 Result{_R}\n{text}\n")


def _warn(text: str) -> None:
    """Yellow ⚠ warning."""
    print(f"\x1b[33m⚠ {_R} {text}")


def _turn_separator(label: str) -> None:
    """Dim cyan ─── separator, matching TurnSeparator / IterationSeparator in CliWriter."""
    pad = max(0, 60 - len(label) - 1)
    bar = "─" * pad
    print(f"\n\x1b[2;36m─── {label} {bar}{_R}")


def _plan_review_header() -> None:
    """Dim magenta block header for the plan-review HITL gate."""
    print(f"\n\x1b[2;35m{'─' * 60}{_R}")
    print(f"\x1b[1;35m  Magentic Plan Review — Human Input Required{_R}")
    print(f"\x1b[2;35m{'─' * 60}{_R}")


def _approval_header(title: str) -> None:
    """Bold bright-blue approval gate header."""
    print(f"\n\x1b[1;94m{'═' * 60}{_R}")
    print(f"\x1b[1;94m  {title}{_R}")
    print(f"\x1b[1;94m{'═' * 60}{_R}")


def _user_prompt(prompt: str) -> str:
    """Bold bright-blue input prompt; returns stripped answer."""
    return input(f"\x1b[1;94m👤 {prompt}{_R} ").strip()


@dataclass
class AzureOpenAISettings:
    endpoint: str
    deployment: str


class IncidentTools:
    """Simple in-memory incident tools for the demo scenario.

    State machine:
      - Starts with catalog on v1.32 (broken).
      - v1.31 is the approved stable fallback for immediate rollback.
      - v1.33 is the fix build; it is NOT yet available in the package registry.
      - v1.33 becomes available in the registry only after the v1.31 rollback has
        been executed (simulating the CI pipeline completing while ops stabilises the
        service).
      - The orchestrator is told the final target is v1.33 and that it will be
        available soon — it must figure out the ordering itself.
    """

    def __init__(self) -> None:
        self.service_name = "catalog"
        self.current_version = "1.32"
        self.stable_fallback_version = "1.31"
        self.fix_version = "1.33"
        self.error_rate = 0.112
        self.latency_ms = 420
        self.deployment_history: list[str] = [
            "2026-02-21 08:00 UTC - deployed catalog 1.32",
            "2026-02-14 10:35 UTC - deployed catalog 1.31",
        ]
        # v1.33 becomes available once the rollback to 1.31 has been performed.
        self._rollback_done = False

    def get_service_status(self, service_name: str) -> str:
        """Get current health metrics for a named service."""
        if service_name.lower() != self.service_name:
            return (
                f"Unknown service: {service_name}. Known service: {self.service_name}."
            )
        return (
            f"Service={self.service_name}, version={self.current_version}, "
            f"error_rate={self.error_rate:.3f}, p95_latency_ms={self.latency_ms}."
        )

    def get_recent_deployments(self, service_name: str) -> str:
        """List recent deployment history for a named service."""
        if service_name.lower() != self.service_name:
            return f"No deployments found for {service_name}."
        return "\n".join(self.deployment_history)

    def check_package_availability(self, service_name: str, version: str) -> str:
        """Check whether a specific package version is available in the registry.

        Returns availability status and, if available, a short build manifest summary.
        """
        if service_name.lower() != self.service_name:
            return f"Unknown service: {service_name}."
        if version == self.fix_version:
            if self._rollback_done:
                return (
                    f"catalog {version} is AVAILABLE in the registry. "
                    "Build pipeline completed. Includes fix for error-rate regression "
                    "introduced in 1.32 (null-pointer in product-search path). "
                    "Regression tests passed. Safe to deploy."
                )
            else:
                return (
                    f"catalog {version} is NOT YET AVAILABLE. "
                    "CI pipeline is still running. Check back after the current incident "
                    "has been stabilised."
                )
        if version == self.stable_fallback_version:
            return f"catalog {version} is AVAILABLE (known stable release)."
        return f"catalog {version} is not found in the registry."

    def deploy_service(self, service_name: str, target_version: str) -> str:
        """Deploy a specific version of a service.

        Works for both rollbacks (to stable fallback) and forward deploys (to fix version).
        The package must be available in the registry before deployment is permitted.
        """
        if service_name.lower() != self.service_name:
            return f"Deploy failed: unknown service {service_name}."

        # Check availability first
        availability = self.check_package_availability(service_name, target_version)
        if "NOT YET AVAILABLE" in availability:
            return (
                f"Deploy blocked: {service_name} {target_version} is not yet available "
                "in the package registry. Please check availability and retry."
            )
        if "not found" in availability:
            return f"Deploy blocked: {service_name} {target_version} not found in registry."

        previous = self.current_version
        self.current_version = target_version

        if target_version == self.stable_fallback_version:
            # Rollback to stable: service recovers partially — still slightly elevated
            # but well within acceptable bounds.
            self.error_rate = 0.014
            self.latency_ms = 205
            self._rollback_done = True
            self.deployment_history.insert(
                0,
                f"2026-02-21 08:30 UTC - rollback {self.service_name} "
                f"from {previous} to {target_version}",
            )
            return (
                f"Rollback completed: {self.service_name} is now on {self.current_version}. "
                f"error_rate={self.error_rate:.3f}, p95_latency_ms={self.latency_ms}. "
                "Service stable. v1.33 pipeline should complete shortly."
            )
        elif target_version == self.fix_version:
            # Forward deploy to fix build: full recovery + fix.
            self.error_rate = 0.008
            self.latency_ms = 190
            self.deployment_history.insert(
                0,
                f"2026-02-21 09:15 UTC - deployed {self.service_name} {target_version} (fix build)",
            )
            return (
                f"Deployment completed: {self.service_name} is now on {self.current_version}. "
                f"error_rate={self.error_rate:.3f}, p95_latency_ms={self.latency_ms}. "
                "Fix build deployed successfully. Incident fully resolved."
            )
        else:
            # Generic deploy (should not happen in this scenario).
            self.deployment_history.insert(
                0,
                f"2026-02-21 - deployed {self.service_name} {target_version}",
            )
            return f"Deployment completed: {self.service_name} is now on {self.current_version}."


def _read_appsettings(repo_root: Path) -> AzureOpenAISettings:
    appsettings_path = repo_root / "appsettings.json"
    data = json.loads(appsettings_path.read_text(encoding="utf-8"))

    azure = data.get("AzureOpenAI", {})
    deployments = azure.get("Deployments", {})

    endpoint = (
        os.getenv("MASKE_AzureOpenAI__Endpoint")
        or os.getenv("AZURE_OPENAI_ENDPOINT")
        or azure.get("Endpoint")
    )
    deployment = (
        os.getenv("MASKE_AzureOpenAI__Deployments__Llm")
        or os.getenv("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME")
        or deployments.get("Llm")
    )

    if not endpoint or not deployment:
        raise RuntimeError(
            "Missing Azure OpenAI configuration. Ensure appsettings.json contains "
            "AzureOpenAI.Endpoint and AzureOpenAI.Deployments.Llm."
        )

    return AzureOpenAISettings(endpoint=endpoint, deployment=deployment)


def _build_chat_client(
    settings: AzureOpenAISettings, api_key: str | None
) -> AzureOpenAIChatClient:
    if api_key:
        return AzureOpenAIChatClient(
            endpoint=settings.endpoint,
            deployment_name=settings.deployment,
            api_key=api_key,
        )
    return AzureOpenAIChatClient(
        endpoint=settings.endpoint,
        deployment_name=settings.deployment,
        credential=DefaultAzureCredential(),
    )


def _build_workflow(chat_client: AzureOpenAIChatClient, tools: IncidentTools):
    inspector_agent = Agent(
        name="DeployInspector",
        description="Inspects service health, deployment history, and package availability.",
        instructions=(
            "You are an SRE incident investigator. You MUST use your tools to gather all facts."
            " Never assume or invent data — always call the appropriate tool."
            " Use get_service_status to check current health metrics."
            " Use get_recent_deployments to review deployment history."
            " Use check_package_availability to determine whether a package version is in the registry."
            " Report only facts returned by tool calls, citing the exact values."
        ),
        client=chat_client,
        tools=[
            tools.get_service_status,
            tools.get_recent_deployments,
            tools.check_package_availability,
        ],
    )

    mitigator_agent = Agent(
        name="Deployer",
        description="Deploys service versions, including rollbacks and forward deploys.",
        instructions=(
            "You execute service deployments using your tools. You MUST call tools — never describe"
            " what you would do as prose. The ONLY way to deploy or roll back a service is to call"
            " deploy_service(service_name, target_version). Before calling deploy_service, always"
            " call check_package_availability to confirm the version is in the registry."
            " After any deployment, call get_service_status to verify the result."
            " If check_package_availability returns 'NOT YET AVAILABLE', report that back and wait."
            " Do not invent deployment results — use the tool output as your answer."
        ),
        client=chat_client,
        tools=[
            tools.check_package_availability,
            tools.deploy_service,
            tools.get_service_status,
        ],
    )

    executive_agent = Agent(
        name="ExecComms",
        description="Produces executive-facing incident communication.",
        instructions=(
            "You write a C-level root cause analysis memo with sections:"
            " impact, timeline, root cause, mitigation steps (including all deployments),"
            " business risk, and next actions. Keep it concise and decision-ready."
            " Base the content on facts reported by DeployInspector and Deployer."
        ),
        client=chat_client,
    )

    manager_agent = Agent(
        name="IncidentManager",
        description="Coordinates the incident team using Magentic planning.",
        instructions=(
            "You are the incident commander. Coordinate DeployInspector, Deployer, and ExecComms."
            " The goal is: (1) investigate the incident,"
            " (2) stabilise service immediately via Deployer,"
            " (3) once stable, have Deployer poll check_package_availability the next version and deploy"
            " it as soon as it becomes available,"
            " (4) verify full recovery via DeployInspector after next version is deployed,"
            " then (5) have ExecComms produce the final C-level RCA for leadership approval."
            " The dev team is working on the next version as we speak, make sure it gets deployed"
            " Trust the tool return values — do not second-guess them."
        ),
        client=chat_client,
    )

    return MagenticBuilder(
        participants=[inspector_agent, mitigator_agent, executive_agent],
        manager_agent=manager_agent,
        enable_plan_review=True,
        intermediate_outputs=True,
        max_round_count=15,
        max_stall_count=3,
        max_reset_count=2,
    ).build()


# Tracks the last streamed response ID to avoid reprinting the author header on each token.
_last_response_id: str | None = None
_last_author_name: str | None = None


async def _process_stream(
    stream: AsyncIterable[WorkflowEvent],
) -> tuple[dict[str, MagenticPlanReviewResponse] | None, str]:
    """Consume a workflow event stream.

    Returns:
        (responses, final_rca): responses is non-None when plan review is pending;
        final_rca is the ExecComms output once the workflow completes.
    """
    global _last_response_id
    global _last_author_name

    plan_review_requests: dict[str, MagenticPlanReviewRequest] = {}
    pending_tool_calls: dict[str, dict[str, str]] = {}
    final_rca = ""

    async for event in stream:
        # ---- plan review pause ----
        if (
            event.type == "request_info"
            and event.request_type is MagenticPlanReviewRequest
        ):
            plan_review_requests[event.request_id] = cast(
                MagenticPlanReviewRequest, event.data
            )

        # ---- streaming agent token ----
        elif event.type == "output" and isinstance(event.data, AgentResponseUpdate):
            update = event.data
            response_changed = update.response_id != _last_response_id
            if response_changed and _last_response_id is not None:
                _flush_pending_tool_call(pending_tool_calls, _last_response_id)

            if update.author_name != _last_author_name:
                if _last_response_id is not None:
                    print()  # end the previous agent's line
                _agent_start(update.author_name)
                _last_author_name = update.author_name

            _last_response_id = update.response_id

            contents = getattr(update, "contents", None)
            if contents:
                for content in contents:
                    content_type = getattr(content, "type", None)
                    if content_type == "function_call":
                        name = getattr(content, "name", None)
                        arguments = getattr(content, "arguments", None)

                        pending = pending_tool_calls.get(update.response_id)
                        if name:
                            if pending and pending.get("name"):
                                _tool_start(
                                    update.author_name,
                                    pending["name"],
                                    _parse_tool_arguments(pending.get("arguments", "")),
                                )
                            pending_tool_calls[update.response_id] = {
                                "agent_name": update.author_name,
                                "name": str(name),
                                "arguments": "",
                            }
                            if arguments not in (None, ""):
                                pending_tool_calls[update.response_id]["arguments"] += (
                                    _arguments_to_text(arguments)
                                )
                        elif pending and arguments not in (None, ""):
                            pending["arguments"] += _arguments_to_text(arguments)

                    elif content_type == "function_result":
                        pending = pending_tool_calls.get(update.response_id)
                        if pending and pending.get("name"):
                            _tool_start(
                                update.author_name,
                                pending["name"],
                                _parse_tool_arguments(pending.get("arguments", "")),
                            )
                            pending_tool_calls.pop(update.response_id, None)

            _agent_token(update.text, end="", flush=True)

        # ---- final workflow output (list[Message]) ----
        elif event.type == "output":
            if _last_response_id is not None:
                _flush_pending_tool_call(pending_tool_calls, _last_response_id)
            print()  # close any open streaming line
            _turn_separator("Workflow Complete")
            outputs = cast(list[Message], event.data)
            for msg in outputs:
                speaker = msg.author_name or msg.role
                _info(f"[{speaker}]")
                print(msg.text)
            # Grab the last message as the RCA
            if outputs:
                final_rca = (outputs[-1].text or "").strip()

        # ---- magentic orchestrator events ----
        elif event.type == "magentic_orchestrator":
            evt_name = event.data.event_type.name
            _turn_separator(f"Magentic: {evt_name}")
            if isinstance(event.data.content, MagenticProgressLedger):
                ledger_json = json.dumps(event.data.content.to_dict(), indent=2)
                print(f"\x1b[2m{ledger_json}{_R}")

    for response_id, pending in list(pending_tool_calls.items()):
        if pending.get("name"):
            _tool_start(
                pending.get("agent_name", "unknown-agent"),
                pending["name"],
                _parse_tool_arguments(pending.get("arguments", "")),
            )
        pending_tool_calls.pop(response_id, None)

    # After stream ends: collect human responses for any plan review requests
    _last_response_id = None
    _last_author_name = None

    responses: dict[str, MagenticPlanReviewResponse] = {}
    for request_id, request in plan_review_requests.items():
        _plan_review_header()
        if request.current_progress is not None:
            print(f"\x1b[2mCurrent Progress:{_R}")
            print(
                f"\x1b[2m{json.dumps(request.current_progress.to_dict(), indent=2)}{_R}"
            )
            print()
        print(f"\x1b[1mProposed Plan:{_R}\n{request.plan.text}\n")
        reply = _user_prompt("Plan feedback (Enter = approve, type to revise):")
        if not reply:
            _info("Plan approved.")
            responses[request_id] = request.approve()
        else:
            _warn(f"Plan revised: {reply}")
            responses[request_id] = request.revise(reply)

    return (responses if responses else None), final_rca


def _arguments_to_text(arguments: object) -> str:
    if isinstance(arguments, str):
        return arguments
    try:
        return json.dumps(arguments, ensure_ascii=False)
    except TypeError:
        return str(arguments)


def _parse_tool_arguments(arguments: str) -> dict[str, str]:
    raw = arguments.strip()
    if not raw:
        return {}

    try:
        parsed = json.loads(raw)
    except json.JSONDecodeError:
        return {"arguments": raw}

    if isinstance(parsed, dict):
        return {k: _render_arg_value(v) for k, v in parsed.items()}

    return {"arguments": _render_arg_value(parsed)}


def _render_arg_value(value: object) -> str:
    if isinstance(value, str):
        return value
    try:
        return json.dumps(value, ensure_ascii=False)
    except TypeError:
        return str(value)


def _flush_pending_tool_call(
    pending_tool_calls: dict[str, dict[str, str]], response_id: str
) -> None:
    pending = pending_tool_calls.get(response_id)
    if not pending or not pending.get("name"):
        return
    _tool_start(
        pending.get("agent_name", "unknown-agent"),
        pending["name"],
        _parse_tool_arguments(pending.get("arguments", "")),
    )
    pending_tool_calls.pop(response_id, None)


def _require_exec_approval(final_rca: str) -> None:
    _approval_header("C-LEVEL RCA — PENDING HUMAN APPROVAL")
    print(final_rca)
    print()

    reply = _user_prompt("Approve and send this RCA to leadership? [y/N]:")
    approved = reply.lower() == "y"
    if approved:
        _runner_result("RCA approved. Ready to send.")
        return

    revision_feedback = _user_prompt(
        "Enter revision guidance for a rerun (or Enter to skip):"
    )
    if revision_feedback:
        _warn("RCA not approved. Captured feedback for rerun:")
        print(f"  \x1b[2m{revision_feedback}{_R}")
    else:
        _warn("RCA rejected without additional feedback.")


async def _main_async(args: argparse.Namespace) -> None:
    repo_root = Path(__file__).resolve().parents[2]
    settings = _read_appsettings(repo_root)
    api_key = args.api_key or os.getenv("AZURE_OPENAI_API_KEY")

    chat_client = _build_chat_client(settings, api_key=api_key)
    tools = IncidentTools()
    workflow = _build_workflow(chat_client, tools)

    task = args.task or (
        "The catalog service has elevated error rates after a recent deployment. "
        "Investigate the incident, stabilise the service immediately, "
        "and then deploy version 1.33 (the fix build currently being prepared by the dev team) "
        "once it becomes available. "
        "When the service is fully restored on v1.33, produce a C-level root cause analysis "
        "memo for leadership approval."
    )

    _header("Magentic: Ops Incident Response")
    _info(f"Endpoint  : {settings.endpoint}")
    _info(f"Deployment: {settings.deployment}")
    _info(f"Task      : {task}")
    print()

    # Initial run
    stream = workflow.run(task, stream=True)
    pending_responses, final_rca = await _process_stream(stream)

    # Resume loop for plan review rounds
    while pending_responses is not None:
        stream = workflow.run(responses=pending_responses, stream=True)
        pending_responses, final_rca = await _process_stream(stream)

    _require_exec_approval(final_rca)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Magentic orchestration incident response demo with HITL"
    )
    parser.add_argument("task", nargs="?", help="Optional incident prompt")
    parser.add_argument(
        "--api-key", help="Azure OpenAI API key (optional if using Azure identity)"
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    asyncio.run(_main_async(args))


if __name__ == "__main__":
    main()
