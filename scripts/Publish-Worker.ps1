param(
    [string] $Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "src\PostNL10x15.Worker\PostNL10x15.Worker.csproj"
$destination = Join-Path $projectRoot "artifacts\worker-$Runtime"

dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $destination `
    -p:PublishSingleFile=false

if ($LASTEXITCODE -ne 0) {
    throw "Publiceren is mislukt."
}

Write-Host "Zelfstandige worker: $destination"

