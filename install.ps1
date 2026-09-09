# BIM One - DustyAutoCAD install script
# Run after building Release to push the DLL to the AutoCAD bundle location.

$src     = "$PSScriptRoot\bin\Release\net8.0-windows\DustyAutoCAD.dll"
$bundle  = "$env:APPDATA\Autodesk\ApplicationPlugins\DustyAutoCAD.bundle"
$dst     = "$bundle\Contents\DustyAutoCAD.dll"

if (-not (Test-Path $bundle\Contents)) {
    New-Item -ItemType Directory -Path "$bundle\Contents" -Force | Out-Null
}

if (-not (Test-Path "$bundle\PackageContents.xml")) {
    @'
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" Version="1.0" AppVersion="1.0.0"
    Name="DustyAutoCAD"
    Description="BIM One Dusty Robotics AutoCAD Add-in"
    Author="BIM One">
  <CompanyDetails Name="BIM One"/>
  <RuntimeRequirements OS="Win64" Platform="AutoCAD" SeriesMin="R26.0" SeriesMax="*"/>
  <Components>
    <ComponentEntry AppName="DustyAutoCAD" Version="1.0.0"
                    ModuleName="./Contents/DustyAutoCAD.dll"
                    LoadOnCommandInvocation="False"
                    LoadOnAutoCADStartup="True"/>
  </Components>
</ApplicationPackage>
'@ | Set-Content "$bundle\PackageContents.xml" -Encoding UTF8
}

Copy-Item $src $dst -Force
Write-Host "Installed: $dst"
