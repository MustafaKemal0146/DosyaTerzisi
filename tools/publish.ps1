# Teslim paketini üretir: release\DosyaTerzisi-<sürüm>.zip
# Kullanım:  powershell -ExecutionPolicy Bypass -File tools\publish.ps1
# Gerekenler: .NET 8 SDK ve Node.js (arayüz derlenirken). Çıkan exe tek dosyadır; .NET kurulumu gerekmez.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$version = ([xml](Get-Content "$root\src\DosyaTerzisi.Shell\DosyaTerzisi.Shell.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$stage = "$root\release\DosyaTerzisi"
$zip = "$root\release\DosyaTerzisi-$version.zip"

if (Test-Path "$root\release") { Remove-Item "$root\release" -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

dotnet publish "$root\src\DosyaTerzisi.Shell" -c Release -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Yayın başarısız.' }

# Gereksiz dosyalar: hata ayıklama bilgisi ve belge dosyaları.
Get-ChildItem $stage -Include *.pdb, *.xml -Recurse | Remove-Item -Force
Copy-Item "$root\docs\KULLANIM.md" "$stage\KULLANIM.md"

Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Hazır: $zip ($size MB)"
Get-ChildItem $stage | Select-Object Name, @{n='MB'; e={[math]::Round($_.Length / 1MB, 2)}}
