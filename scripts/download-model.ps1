# ============================================================
# Скачивает ONNX-модель multilingual-e5-small (235 МБ) из GitHub Release.
#
# Модель НЕ хранится в git (превышает лимит GitHub 100 MiB на файл):
# файл model_O4.onnx публикуется как asset релиза репозитория
#   https://github.com/lunatik2810/otus-projectwork-rag
# и скачивается по постоянному URL releases/latest/download/model_O4.onnx.
#
# Запуск (Windows PowerShell):
#   powershell -ExecutionPolicy Bypass -File scripts\download-model.ps1
#
# Результат: Resources\multilingual-e5-small\model_O4.onnx
# ============================================================
$ErrorActionPreference = 'Stop'

$ModelUrl = 'https://github.com/lunatik2810/otus-projectwork-rag/releases/latest/download/model_O4.onnx'
$TargetDir = Join-Path $PSScriptRoot '..\Resources\multilingual-e5-small'
$TargetFile = Join-Path $TargetDir 'model_O4.onnx'

New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

if (Test-Path $TargetFile) {
    $size = (Get-Item $TargetFile).Length
    if ($size -gt 100MB) {
        Write-Host "Модель уже есть ($([math]::Round($size / 1MB)) МБ): $TargetFile"
        exit 0
    }
    Write-Host "Файл $TargetFile повреждён или неполон (${size} байт), скачиваю заново..."
}

Write-Host "Скачивание модели ($ModelUrl)..."
Invoke-WebRequest -Uri $ModelUrl -OutFile $TargetFile
Write-Host "Готово: $TargetFile"