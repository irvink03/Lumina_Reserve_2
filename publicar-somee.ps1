# =====================================================================
# Publica Lumina Reserve para Somee (Windows / IIS) en la carpeta .\publicar
# y genera .\publicar-somee.zip listo para subir.
#
# Uso (desde la carpeta Lumina_Reserve_2):
#   .\publicar-somee.ps1                 # requiere .NET 10 instalado en el servidor
#   .\publicar-somee.ps1 -Autonomo       # incluye el runtime de .NET (si Somee da error 500.31)
#
# Antes de publicar, llena appsettings.Production.json con la cadena de conexión de Somee.
# =====================================================================
param([switch]$Autonomo)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$salida = Join-Path $PSScriptRoot "publicar"

$prod = Get-Content "appsettings.Production.json" -Raw -ErrorAction SilentlyContinue
if (-not $prod -or $prod -match "PEGA_AQUI") {
    Write-Warning "appsettings.Production.json todavía no tiene la cadena de conexión de Somee. Se publicará igual, pero la app no podrá conectarse a la BD."
}

if (Test-Path $salida) { Remove-Item $salida -Recurse -Force }

if ($Autonomo) {
    # Incluye el runtime de .NET 10 (x86, el modo por defecto de los grupos de IIS compartidos).
    dotnet publish -c Release -o $salida -r win-x86 --self-contained true -p:PublishReadyToRun=false
} else {
    dotnet publish -c Release -o $salida
}
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló." }

# 1. Librerías nativas que no sirven en un servidor Windows (QuestPDF/SkiaSharp traen de Linux y Mac).
$runtimes = Join-Path $salida "runtimes"
if (Test-Path $runtimes) {
    Get-ChildItem $runtimes -Directory |
        Where-Object { $_.Name -notin @("win", "win-x86", "win-x64") } |
        Remove-Item -Recurse -Force
}

# 2. Bootstrap local: el layout lo carga desde CDN.
$bootstrap = Join-Path $salida "wwwroot\lib\bootstrap"
if (Test-Path $bootstrap) { Remove-Item $bootstrap -Recurse -Force }

# 3. Nunca subir la configuración ni las fotos de perfil de tu PC.
Remove-Item (Join-Path $salida "appsettings.Development.json") -ErrorAction SilentlyContinue
Get-ChildItem (Join-Path $salida "wwwroot\img\perfiles") -File -ErrorAction SilentlyContinue | Remove-Item -Force

# 4. Carpetas que la app necesita en el servidor.
New-Item -ItemType Directory -Force (Join-Path $salida "App_Data\keys") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $salida "logs") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $salida "wwwroot\img\perfiles") | Out-Null

# 5. Registro de errores de arranque en .\logs (útil en la primera publicación; puedes apagarlo después
#    cambiando stdoutLogEnabled a "false" en el web.config del servidor).
$webConfig = Join-Path $salida "web.config"
[xml]$xml = Get-Content $webConfig
$core = $xml.SelectSingleNode("//aspNetCore")
$core.SetAttribute("stdoutLogEnabled", "true")
$core.SetAttribute("stdoutLogFile", ".\logs\stdout")
$xml.Save($webConfig)

# 6. ZIP para subir con el File Manager de Somee.
$zip = Join-Path $PSScriptRoot "publicar-somee.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $salida "*") -DestinationPath $zip

$mb = [math]::Round(((Get-ChildItem $salida -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ""
Write-Host "Listo. Carpeta: $salida ($mb MB)  ·  ZIP: $zip ($zipMb MB)" -ForegroundColor Green
Write-Host "Límite de Somee gratis: 150 MB de disco." -ForegroundColor DarkGray
