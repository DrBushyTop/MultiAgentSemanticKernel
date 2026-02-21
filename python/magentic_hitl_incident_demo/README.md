# Magentic Incident Demo (Python)

This demo implements a **Magentic orchestration** in Python using the same Azure OpenAI settings from this repository's `appsettings.json`.

Scenario: an incident manager coordinates specialists to investigate a production incident, perform mitigation, and produce a **C-level root cause analysis (RCA)**. The flow includes two human checkpoints:

1. **Plan review in the Magentic loop** (approve or revise the generated plan)
2. **Final RCA approval gate** before leadership communication

## What It Uses from `appsettings.json`

The script reads:

- `AzureOpenAI:Endpoint`
- `AzureOpenAI:Deployments:Llm`

It also supports environment overrides compatible with this repo:

- `MASKE_AzureOpenAI__Endpoint`
- `MASKE_AzureOpenAI__Deployments__Llm`

And standard Azure env vars:

- `AZURE_OPENAI_ENDPOINT`
- `AZURE_OPENAI_CHAT_DEPLOYMENT_NAME`
- `AZURE_OPENAI_API_KEY` (optional)

## Prerequisites

- Python 3.10+
- Azure authentication (`az login`) if you are not using an API key

## Install

From repository root:

```bash
python -m venv .venv
source .venv/bin/activate
pip install -r python/magentic_hitl_incident_demo/requirements.txt
```

## Run

From repository root:

```bash
python python/magentic_hitl_incident_demo/run_demo.py
```

Optional custom task:

```bash
python python/magentic_hitl_incident_demo/run_demo.py "The checkout service is timing out after a release. Investigate and prepare an executive RCA."
```

Optional API key override:

```bash
python python/magentic_hitl_incident_demo/run_demo.py --api-key "<your-azure-openai-key>"
```

## Human-in-the-loop behavior

- During orchestration, you will see a **Magentic Plan Review** prompt:
  - Press Enter to approve.
  - Type feedback to revise the plan.
- At the end, you must explicitly approve the final executive RCA draft.

## One-shot launcher

A convenience script at the repo root sets up the venv, installs dependencies, and runs the demo:

```bash
bash run-magentic-demo.sh
```

## Agents

| Agent | Role |
|---|---|
| IncidentManager | Magentic manager — coordinates the other agents |
| DeployInspector | SRE investigator — checks service status and deployment history |
| Deployer | Executes rollback/mitigation and verifies results |
| ExecComms | Writes the C-level RCA memo |

## Notes

- This demo is intentionally self-contained in `python/magentic_hitl_incident_demo`.
- Tool state is in-memory (`IncidentTools`) to give the agents realistic data without external dependencies.
- `intermediate_outputs=True` on `MagenticBuilder` enables streaming agent tokens to the terminal.
- The demo has been verified end-to-end against Azure OpenAI (gpt-4.1).
