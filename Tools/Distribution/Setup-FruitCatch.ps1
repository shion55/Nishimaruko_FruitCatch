$ErrorActionPreference = 'Stop'
$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $OutputEncoding

$trackingRoot = Join-Path $PSScriptRoot 'Tracking'
$venvRoot = Join-Path $trackingRoot '.venv'
$venvPython = Join-Path $venvRoot 'Scripts\python.exe'
$requirements = Join-Path $trackingRoot 'requirements.txt'

function Find-SystemPython {
    $commands = @('python', 'py')
    foreach ($command in $commands) {
        $found = Get-Command $command -ErrorAction SilentlyContinue
        if ($null -eq $found) { continue }
        try {
            if ($command -eq 'py') {
                & $found.Source -3 -c "import sys, tkinter; assert sys.version_info >= (3,10)" 2>$null
                if ($LASTEXITCODE -eq 0) { return @($found.Source, '-3') }
            } else {
                & $found.Source -c "import sys, tkinter; assert sys.version_info >= (3,10)" 2>$null
                if ($LASTEXITCODE -eq 0) { return @($found.Source) }
            }
        } catch { }
    }
    return $null
}

$pythonCommand = @(Find-SystemPython)
if ($pythonCommand.Count -eq 0) {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($null -eq $winget) {
        throw 'Tkinterが利用できるPython 3.10以上がありません。Python 3.12をインストールしてから、もう一度実行してください。'
    }

    Write-Host 'Python 3.12をこのユーザー用に導入します…'
    & $winget.Source install --exact --id Python.Python.3.12 --scope user --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { throw 'Pythonの導入に失敗しました。' }

    $installedPython = Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'
    if (-not (Test-Path -LiteralPath $installedPython)) {
        throw '導入したPythonが見つかりません。PCを再起動してから、もう一度実行してください。'
    }
    $pythonCommand = @($installedPython)
}

if (-not (Test-Path -LiteralPath $venvPython)) {
    Write-Host 'FruitCatch専用のPython環境を作成します…'
    $executable = $pythonCommand[0]
    $arguments = @()
    if ($pythonCommand.Count -gt 1) { $arguments += $pythonCommand[1..($pythonCommand.Count - 1)] }
    $arguments += @('-m', 'venv', $venvRoot)
    & $executable @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Python環境の作成に失敗しました。' }
}

Write-Host 'カメラ・顔・姿勢判定ライブラリを準備します…'
& $venvPython -m pip install --disable-pip-version-check --upgrade pip
if ($LASTEXITCODE -ne 0) { throw 'pipの更新に失敗しました。' }
& $venvPython -m pip install --disable-pip-version-check -r $requirements
if ($LASTEXITCODE -ne 0) { throw '必要ライブラリの導入に失敗しました。インターネット接続を確認してください。' }
& $venvPython -X utf8 -c "import cv2, tkinter, cv2_enumerate_cameras, mediapipe; assert hasattr(cv2, 'FaceDetectorYN'); assert hasattr(mediapipe.tasks.vision, 'PoseLandmarker')"
if ($LASTEXITCODE -ne 0) { throw 'カメラ判定かTkinterを読み込めません。Python 3.12の環境を確認してください。' }

Write-Host ''
Write-Host '準備が完了しました。「Start FruitCatch.bat」から起動できます。' -ForegroundColor Green
