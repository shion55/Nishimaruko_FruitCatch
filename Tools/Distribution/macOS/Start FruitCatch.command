#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"

if [[ "$(uname -s)" != Darwin ]]; then
    echo 'macOS 上でこのスクリプトを実行してください。' >&2
    exit 1
fi
if [[ ! -x Tracking/.venv/bin/python ]]; then
    echo '先に「Setup FruitCatch.command」を実行してください。' >&2
    exit 1
fi
exec Tracking/.venv/bin/python -X utf8 Tracking/fruitcatch_mac_launcher.py
