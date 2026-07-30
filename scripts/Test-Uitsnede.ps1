param(
    [Parameter(Position = 0)]
    [string] $Pdf = "D:\_projects_\PostNL_label_print\VoorbeeldLabels\PostNL\Verzendlabels-9VRGA4L.pdf"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$workerProject = Join-Path $projectRoot "src\PostNL10x15.Worker\PostNL10x15.Worker.csproj"
$outputDirectory = Join-Path $projectRoot "output\pdf"
$previewDirectory = Join-Path $projectRoot "output\preview"
$outputPdf = Join-Path $outputDirectory "PostNL-label-10x15.pdf"
$outputPng = Join-Path $previewDirectory "PostNL-label-203dpi.png"

New-Item -ItemType Directory -Force -Path $outputDirectory, $previewDirectory | Out-Null

dotnet build $workerProject --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "Bouwen is mislukt."
}

$worker = Join-Path $projectRoot "src\PostNL10x15.Worker\bin\Release\net8.0-windows\PostNL10x15.exe"
& $worker crop $Pdf $outputPdf
if ($LASTEXITCODE -ne 0) {
    throw "Uitsnijden is mislukt."
}

& $worker preview $Pdf $outputPng
if ($LASTEXITCODE -ne 0) {
    throw "Voorbeeld maken is mislukt."
}

Write-Host ""
Write-Host "Klaar. Er is niets afgedrukt."
Write-Host "PDF: $outputPdf"
Write-Host "Voorbeeld: $outputPng"

