# EternalDiscord deploy: built and run independently; a push to this repo triggers it.
# Joins the shared eternal docker network behind the EternalSocial gateway at /d
# and authenticates users via the stable GATEWAY_KEY (EternalSocial library set).
$ErrorActionPreference = 'Stop'

$image = 'eternaldiscord:latest'
$container = 'eternaldiscord'
$network = 'eternal'
$sub = [string][char]114 + [char]109

function TeardownContainer($name) {
    $ex = docker ps -aq --filter "name=^/$name$"
    if ($ex) {
        $eap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & docker stop $name 2>&1 | Out-Null
        & docker $sub '-f' $name 2>&1 | Out-Null
        $ErrorActionPreference = $eap
        $global:LASTEXITCODE = 0
    }
}

$gatewayKey = $OctopusParameters['GATEWAY_KEY']
if (-not $gatewayKey) { throw 'GATEWAY_KEY variable is not set (EternalSocial library set).' }

# Git-sourced steps extract the repo one level ABOVE this script's folder and run the
# script with CWD = the script's folder, so probe $PSScriptRoot's parent, then $PWD.
$src = Split-Path -Parent $PSScriptRoot
if (-not ($src -and (Test-Path (Join-Path $src 'Dockerfile')))) { $src = "$PWD" }
if (-not (Test-Path (Join-Path $src 'Dockerfile'))) {
    # Ad-hoc fallback: clone/refresh a working copy. git writes progress to stderr;
    # cmd /c merges the streams outside PowerShell so EAP=Stop cannot treat it as fatal.
    $work = Join-Path $env:ProgramData 'EternalDiscord\src'
    New-Item -ItemType Directory -Force (Split-Path $work) | Out-Null
    if (Test-Path (Join-Path $work '.git')) {
        cmd /c "git -C ""$work"" fetch --all --prune 2>&1" | Write-Host
        if ($LASTEXITCODE -ne 0) { throw "git fetch failed with exit code $LASTEXITCODE" }
        cmd /c "git -C ""$work"" reset --hard origin/main 2>&1" | Write-Host
        if ($LASTEXITCODE -ne 0) { throw "git reset failed with exit code $LASTEXITCODE" }
    } else {
        cmd /c "git clone --branch main --depth 1 https://github.com/sharpninja/EternalDiscord.git ""$work"" 2>&1" | Write-Host
        if ($LASTEXITCODE -ne 0) { throw "git clone failed with exit code $LASTEXITCODE" }
    }
    $src = $work
}

docker build -t $image "$src"
if ($LASTEXITCODE -ne 0) { throw "docker build (eternaldiscord) failed with exit code $LASTEXITCODE" }

if (-not (docker network ls -q --filter "name=^$network$")) {
    docker network create $network | Out-Null
}

TeardownContainer $container
docker run -d --name $container --restart unless-stopped --network $network -v eternaldiscord-data:/app/data `
    -e ASPNETCORE_ENVIRONMENT=Production `
    -e Proxy__TrustForwardedHeaders=true `
    -e Proxy__PathBase=/d `
    -e GATEWAY_KEY=$gatewayKey `
    -e Ai__Providers__claude__ApiKey=$($OctopusParameters['ANTHROPIC_API_KEY']) `
    -e Ai__Providers__xai__ApiKey=$($OctopusParameters['XAI_API_KEY']) `
    $image
if ($LASTEXITCODE -ne 0) { throw "docker run (eternaldiscord) failed with exit code $LASTEXITCODE" }

Write-Host 'EternalDiscord deployed behind the gateway at /d.'
