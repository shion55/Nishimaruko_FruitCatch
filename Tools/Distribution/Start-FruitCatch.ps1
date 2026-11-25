$ErrorActionPreference = 'Stop'

$gamePath = Join-Path $PSScriptRoot 'FruitCatch.exe'
$trackingRoot = Join-Path $PSScriptRoot 'Tracking'
$pythonPath = Join-Path $trackingRoot '.venv\Scripts\python.exe'
$trackerPath = Join-Path $trackingRoot 'face_tracker_registered.py'

$needsSetup = -not (Test-Path -LiteralPath $pythonPath)
if (-not $needsSetup) {
    & $pythonPath -c "import cv2, cv2_enumerate_cameras, mediapipe, numpy; assert hasattr(cv2, 'FaceDetectorYN'); assert (4, 8) <= tuple(map(int, cv2.__version__.split('.')[:2])) < (5, 0)" 2>$null
    $needsSetup = $LASTEXITCODE -ne 0
}
if ($needsSetup) {
    & (Join-Path $PSScriptRoot 'Setup-FruitCatch.ps1')
}
if (-not (Test-Path -LiteralPath $gamePath)) {
    throw "ゲーム本体が見つかりません: $gamePath"
}

$tracker = $null
try {
    $tracker = Start-Process -FilePath $pythonPath -ArgumentList @('face_tracker_registered.py') `
        -WorkingDirectory $trackingRoot -WindowStyle Hidden -PassThru

    $ready = $false
    # カメラと判定モデルの準備を待つ。
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        if ($tracker.HasExited) { throw 'カメラ判定プログラムが途中で終了しました。' }
        try {
            Invoke-WebRequest -Uri 'http://127.0.0.1:8765/frame.jpg' -TimeoutSec 1 -UseBasicParsing | Out-Null
            $ready = $true
            break
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $ready) { throw 'カメラを開始できませんでした。カメラ接続を確認してください。' }

    Start-Process -FilePath $gamePath -WorkingDirectory $PSScriptRoot -Wait
}
finally {
    if ($null -ne $tracker -and -not $tracker.HasExited) {
        Stop-Process -Id $tracker.Id -Force
    }
}
