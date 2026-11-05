param([string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim")

dotnet build "$PSScriptRoot\ValheimSpeakerMode.csproj" -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Built: $PSScriptRoot\bin\Release\netstandard2.1\ValheimSpeakerMode.dll"
