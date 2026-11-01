param(
    [string]$Configuration = "Release",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
# 优先用仓库内的便携 SDK；没有则回退到 PATH 里的 dotnet
$dotnet = Join-Path $root ".tools\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet.exe" }
try { & $dotnet --version | Out-Null } catch { throw "未找到 .NET SDK：请安装 .NET 8 SDK，或把便携 SDK 解压到 .tools\dotnet" }
if ($LASTEXITCODE -ne 0) { throw "未找到 .NET SDK：请安装 .NET 8 SDK，或把便携 SDK 解压到 .tools\dotnet" }

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_UI_LANGUAGE = "en"

$proj = Join-Path $root "src\MotionPhotoStudio.csproj"
$out = Join-Path $root "build"
$dist = Join-Path $root "dist"

Write-Host "==> 还原依赖" -ForegroundColor Cyan
& $dotnet restore $proj
if ($LASTEXITCODE -ne 0) { throw "restore 失败" }

Write-Host "==> 编译" -ForegroundColor Cyan
& $dotnet build $proj -c $Configuration -o $out --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw "build 失败" }

$exe = Join-Path $out "MotionPhotoStudio.exe"
if (-not (Test-Path $exe)) { throw "未生成 exe" }
$size = [math]::Round((Get-Item $exe).Length / 1MB, 2)
Write-Host ("==> 生成 {0}（{1} MB）" -f $exe, $size) -ForegroundColor Green

$folder = Join-Path $dist "MotionPhotoStudio-win-x64"
if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
New-Item -ItemType Directory -Force -Path $folder | Out-Null
Copy-Item $exe (Join-Path $folder "MotionPhotoStudio.exe") -Force
Copy-Item (Join-Path $root "docs\使用说明.txt") (Join-Path $folder "使用说明.txt") -Force

if (-not $SkipZip) {
    $zip = Join-Path $dist "MotionPhotoStudio-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $folder "*") -DestinationPath $zip -CompressionLevel Optimal
    Write-Host ("==> 打包 {0}" -f $zip) -ForegroundColor Green
}

Write-Host "完成。" -ForegroundColor Green
