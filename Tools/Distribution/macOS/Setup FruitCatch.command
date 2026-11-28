#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"

if [[ "$(uname -s)" != Darwin ]]; then
    echo 'macOS 上でこのスクリプトを実行してください。' >&2
    exit 1
fi
mac_version="$(/usr/bin/sw_vers -productVersion)"
if [[ "${mac_version%%.*}" -lt 13 ]]; then
    echo 'この配布版の Python ライブラリには macOS 13 以降が必要です。' >&2
    exit 1
fi

architecture=x86_64
if [[ "$(/usr/sbin/sysctl -n hw.optional.arm64 2>/dev/null || true)" == 1 ]]; then
    architecture=arm64
fi

venv_python="$PWD/Tracking/.venv/bin/python"
if [[ ! -x "$venv_python" ]]; then
    python312=''
    for candidate in "${FRUITCATCH_PYTHON312:-}" \
        /Library/Frameworks/Python.framework/Versions/3.12/bin/python3.12 \
        /opt/homebrew/bin/python3.12 /usr/local/bin/python3.12 \
        "$(command -v python3.12 || true)"; do
        if [[ -x "$candidate" ]] && "$candidate" -c \
            'import platform, sys; assert sys.version_info[:2] == (3, 12) and platform.machine() == sys.argv[1]' \
            "$architecture" >/dev/null 2>&1; then
            python312="$candidate"
            break
        fi
    done
    if [[ -z "$python312" ]]; then
        echo "Python 3.12 ($architecture) が見つかりません。python.org の macOS 64-bit universal2 版を入れて再実行してください。" >&2
        exit 1
    fi
    "$python312" -m venv Tracking/.venv
fi

if ! "$venv_python" -c \
    'import platform, sys; assert sys.version_info[:2] == (3, 12) and platform.machine() == sys.argv[1]' \
    "$architecture"; then
    echo 'Tracking/.venv はこの Mac の Python 3.12 用ではありません。フォルダ名を変更してから Setup を再実行してください。' >&2
    exit 1
fi

echo 'カメラとトラッキング用ライブラリを準備します…'
"$venv_python" -m pip install --disable-pip-version-check --only-binary=:all: -r Tracking/requirements-macos.txt
"$venv_python" -X utf8 -c '
import AppKit, cv2, mediapipe, numpy
from cv2_enumerate_cameras import enumerate_cameras
assert hasattr(cv2, "CAP_AVFOUNDATION") and hasattr(cv2, "FaceDetectorYN") and hasattr(cv2, "FaceRecognizerSF")
assert hasattr(mediapipe.tasks.vision, "PoseLandmarker")
'

app="$PWD/FruitCatch.app"
executable=$(/usr/libexec/PlistBuddy -c 'Print CFBundleExecutable' "$app/Contents/Info.plist")
if [[ -z "$executable" || "$executable" == */* || ! -f "$app/Contents/MacOS/$executable" ]]; then
    echo 'FruitCatch.app の実行ファイルが見つかりません。配布フォルダ一式をコピーしてください。' >&2
    exit 1
fi
chmod +x "$app/Contents/MacOS/$executable" "Setup FruitCatch.command" "Start FruitCatch.command"
if [[ -f "$app/Contents/MacOS/UnityCrashHandler" ]]; then
    chmod +x "$app/Contents/MacOS/UnityCrashHandler"
fi
if ! /usr/bin/codesign --verify --deep --strict "$app" 2>/dev/null; then
    echo 'この Mac で動作するよう、FruitCatch.app にローカル署名します。'
    /usr/bin/codesign --force --deep --sign - "$app"
    /usr/bin/codesign --verify --deep --strict "$app"
fi

echo '準備が完了しました。「Start FruitCatch.command」を開いてください。'
