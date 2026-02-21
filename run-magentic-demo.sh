#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# run-magentic-demo.sh
# One-shot launcher for the Python Magentic orchestration demo.
# Reads Azure OpenAI settings from appsettings.json (same as the .NET runner).
# ---------------------------------------------------------------------------
set -euo pipefail

DEMO_DIR="python/magentic_hitl_incident_demo"
VENV_DIR=".venv-magentic"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo ""
echo "===  Magentic Incident Response Demo (Python)  ==="
echo ""

# ---- 1. Ensure a virtual environment exists --------------------------------
if [ ! -d "$VENV_DIR" ]; then
  echo "Creating virtual environment in $VENV_DIR ..."
  python3 -m venv "$VENV_DIR"
fi

# shellcheck disable=SC1091
source "$VENV_DIR/bin/activate"

# ---- 2. Install / upgrade dependencies ------------------------------------
echo "Installing dependencies ..."
pip install --quiet --upgrade pip
pip install --quiet -r "$DEMO_DIR/requirements.txt"

echo ""
echo "Dependencies ready."
echo ""

# ---- 3. Run the demo -------------------------------------------------------
exec python "$DEMO_DIR/run_demo.py" "$@"
