# DustyAutoCAD

AutoCAD add-in for [Dusty Robotics](https://www.dustyrobotics.com) workflows, built by [BIM One](https://bimone.com).

Adds a **BIM One** ribbon tab in AutoCAD with four tools purpose-built for preparing and validating DWG files before sending them to a Dusty FieldPrinter.

---

## Tools

### Layer Manager (`DUSTYLAYERMGR`)
Map existing DWG layers to Dusty Robotics `DR-` naming conventions, assign linestyles, and manage intent (printable / reference / obstacle). Save and reload presets across projects.

### Overlap Checker (`DUSTYCHECK`)
Scans model space for collinear, overlapping line segments. Overlapping lines can corrupt Dusty linestyle rendering on site. Reports every pair, shows the overlap length and location, and can auto-fix fully-contained duplicates.

### Control Points (`DUSTYCP` / `DUSTYPLACE`)
Place `DUSTY_CP` marker blocks interactively with name and description attributes, then export them as a Dusty-standard CSV file (Point Name, X, Y, Z, Description).

### Layer Visibility (`DUSTYLAYERS`)
Quick on/off toggle panel for all layers in the current drawing, useful for isolating what gets printed.

---

## Requirements

- AutoCAD 2024 or later (x64)
- .NET 8 SDK (for building from source)
- Windows 10/11 x64

---

## Build

> **Note:** If you downloaded the zip from GitHub, the files extract into a nested folder (`DustyAutoCAD-main\DustyAutoCAD-main`). Run all commands from the inner folder where `DustyAutoCAD.csproj` lives.

1. Clone or download the repo.
2. Open a terminal in the folder containing `DustyAutoCAD.csproj`.
3. Build for Release:

```
dotnet build -c Release
```

If your AutoCAD is not in the default `C:\Program Files\Autodesk\AutoCAD 2026` location, pass the install directory:

```
dotnet build -c Release -p:AutoCADInstallDir="C:\Program Files\Autodesk\AutoCAD 2025"
```

---

## Install

After building, run the install script from the folder containing `install.ps1`:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

This copies `DustyAutoCAD.dll` into your AutoCAD bundle folder (`%APPDATA%\Autodesk\ApplicationPlugins\DustyAutoCAD.bundle`) and creates `PackageContents.xml` if it does not exist. Restart AutoCAD to load the add-in.

> **Note:** Windows blocks unsigned scripts downloaded from the internet by default. The `-ExecutionPolicy Bypass` flag tells Windows to run this specific script anyway. It applies only to this one command and does not change any system-wide settings or disable any other security features.

---

## License

MIT License. See [LICENSE](LICENSE).

---

## About

Made by [BIM One](https://bimone.com), official [Dusty Robotics partner](https://www.dustyrobotics.com/partners).
