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

## Download

Get the latest pre-built release from the [Releases](https://github.com/bimone/DustyAutoCAD/releases) page.

---

## Install

1. Download `DustyAutoCAD-v1.0.0.zip` from the latest release.
2. Extract the zip. You will get a folder called `DustyAutoCAD.bundle`.
3. Copy the entire `DustyAutoCAD.bundle` folder to:

```
%APPDATA%\Autodesk\ApplicationPlugins\
```

The final structure should look like this:

```
%APPDATA%\Autodesk\ApplicationPlugins\
  DustyAutoCAD.bundle\
    PackageContents.xml
    Contents\
      DustyAutoCAD.dll
```

4. Restart AutoCAD. A **BIM One** tab will appear in the ribbon.

---

## Requirements

- AutoCAD 2024 or later (x64)
- Windows 10/11 x64

---

## Build from Source

> **Note:** Only needed if you want to modify or contribute to the add-in. For regular use, download from the Releases page above.

If you downloaded the zip from GitHub, the source files extract into a nested folder (`DustyAutoCAD-main\DustyAutoCAD-main`). Run all commands from the inner folder where `DustyAutoCAD.csproj` lives.

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download).
2. Open a terminal in the folder containing `DustyAutoCAD.csproj`.
3. Build for Release:

```
dotnet build -c Release
```

If your AutoCAD is not installed at `C:\Program Files\Autodesk\AutoCAD 2026`, pass your version:

```
dotnet build -c Release -p:AutoCADInstallDir="C:\Program Files\Autodesk\AutoCAD 2025"
```

The built DLL will be at `bin\Release\net8.0-windows\DustyAutoCAD.dll`. Copy it into the bundle folder structure shown in the Install section above.

---

## License

MIT License. See [LICENSE](LICENSE).

---

## About

Made by [BIM One](https://bimone.com), official [Dusty Robotics partner](https://www.dustyrobotics.com/partners).
