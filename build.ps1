# Builds ConnectObsidian.exe and embeds the official tunnel-client release.
# tunnel-client and its bundled cloudflared are Apache-2.0 software from
# their upstream projects. This script downloads them and copies their
# LICENSE and NOTICE into dist\ so a shared exe stays attributable.

$ErrorActionPreference = "Stop"

$Version = "v0.0.18"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Vendor = Join-Path $Root "vendor"
$Dist = Join-Path $Root "dist"
$ZipName = "tunnel-client-$Version-windows-amd64.zip"
$ZipPath = Join-Path $Vendor $ZipName
$Url = "https://github.com/openai/tunnel-client/releases/download/$Version/$ZipName"
$Extracted = Join-Path $Vendor "tunnel-client"

New-Item -ItemType Directory -Force -Path $Vendor, $Dist | Out-Null

if (-not (Test-Path -LiteralPath $ZipPath)) {
    Write-Output "Downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $ZipPath
}

if (Test-Path -LiteralPath $Extracted) {
    Remove-Item -LiteralPath $Extracted -Recurse -Force
}
Expand-Archive -LiteralPath $ZipPath -DestinationPath $Extracted -Force

function Find-One([string]$Name) {
    $found = Get-ChildItem -LiteralPath $Extracted -Recurse -File -Filter $Name | Select-Object -First 1
    if (-not $found) {
        throw "Could not find $Name in the tunnel-client release."
    }
    return $found.FullName
}

$TunnelClient = Find-One "tunnel-client.exe"
$Cloudflared = Find-One "cloudflared.exe"
$Manifest = Find-One "cloudflared-manifest.json"
$UpstreamLicense = Find-One "LICENSE"
$UpstreamNotice = Find-One "NOTICE"

$Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $Csc)) {
    $Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path -LiteralPath $Csc)) {
    throw "The .NET Framework C# compiler was not found."
}

$Out = Join-Path $Dist "ConnectObsidian.exe"
& $Csc /nologo /optimize+ /target:winexe `
    /win32manifest:"$(Join-Path $Root 'src\app.manifest')" `
    /win32icon:"$(Join-Path $Root 'src\app.ico')" `
    /resource:"$TunnelClient,ObsidianChatGpt.tunnel-client.exe" `
    /resource:"$Cloudflared,ObsidianChatGpt.cloudflared.exe" `
    /resource:"$Manifest,ObsidianChatGpt.cloudflared-manifest.json" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll `
    /out:"$Out" `
    "$(Join-Path $Root 'src\ConnectObsidian.cs')"

if ($LASTEXITCODE -ne 0) {
    throw "Compile failed with exit code $LASTEXITCODE."
}

Copy-Item -Force -LiteralPath $UpstreamLicense -Destination (Join-Path $Dist "LICENSE.tunnel-client")
Copy-Item -Force -LiteralPath $UpstreamNotice -Destination (Join-Path $Dist "NOTICE.tunnel-client")
Copy-Item -Force -LiteralPath (Join-Path $Root "LICENSE") -Destination (Join-Path $Dist "LICENSE")

Write-Output "Built $Out"
