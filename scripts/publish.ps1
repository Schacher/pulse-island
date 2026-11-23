# Builds the portable NativeAOT release: dist\PulseIsland-<version>-<arch>\ and a .zip next to it.
param([ValidateSet('x64', 'arm64')][string]$Arch = 'x64')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
# NativeAOT's linker step calls vswhere.exe by name.
$env:PATH += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"

dotnet test --project "$root\tests\PulseIsland.Core.Tests"
if ($LASTEXITCODE) { throw 'tests failed' }

$version = ([xml](Get-Content "$root\src\PulseIsland.App\PulseIsland.App.csproj")).Project.PropertyGroup.Version | Select-Object -First 1
$out = "$root\dist\PulseIsland-$version-$Arch"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish "$root\src\PulseIsland.App" -c Release -r "win-$Arch" -o $out
if ($LASTEXITCODE) { throw 'publish failed' }
Get-ChildItem $out -Filter *.pdb -Recurse | Remove-Item
Copy-Item "$root\LICENSE", "$root\README.md" $out -ErrorAction SilentlyContinue

Compress-Archive -Path "$out\*" -DestinationPath "$out.zip" -Force
$size = (Get-ChildItem $out -Recurse | Measure-Object Length -Sum).Sum / 1MB
"published $out ({0:N1} MB) and $out.zip" -f $size
