# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build

- Open `VisionMeasure/VisionMeasure.sln` in Visual Studio (2017+)
- Build solution in Debug or Release — all projects are **x64 only** (no AnyCPU configs), output to `../bin/`
- NuGet packages need restore before first build
- CLI build works when VS/MSBuild is installed (e.g. VS18 at `D:\Program Files\Microsoft Visual Studio\18\...`). From Git Bash:
  ```bash
  MSYS_NO_PATHCONV=1 "/d/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" \
    "VisionMeasure/VisionMeasure.sln" /t:Rebuild /p:Configuration=Release /p:Platform=x64 /m /restore
  ```
  Notes: `MSYS_NO_PATHCONV=1` prevents Git Bash from mangling `/t:` `/p:` flags into paths; `Platform=x64` is required (without it MSBuild defaults to AnyCPU and errors with "BaseOutputPath 未设置"); when piping output use `set -o pipefail` or the pipeline masks MSBuild's exit code. Main WinExe project file is `VisionMeasure/视觉模板.csproj`.

## Architecture

.NET Framework 4.7.2 WinForms machine vision inspection system for Colgate toothpaste tubes. 5 cameras on a rotating fixture inspect front/back characters and tube quality.

**Solution projects (`VisionMeasure.sln`):**

| Project | Role |
|---|---|
| `VisionMeasure` | Main WinExe. Entry point: `Program.cs` → `MainFrm`. Owns camera lifecycle, PLC comm, AI inference, and the main detection pipeline |
| `CommonLib` | Shared library. Singleton config (`Class_Config` reads/writes `setup.ini`), global state (`GlobalVar`), Cognex VisionPro load/save (`Vision`), image save queues, Zmotion wrappers, SQLite helper |
| `产品管理` | Product/SKU management dialog |
| `用户管理` | Login and user management dialog |
| `相机设置` | Camera parameter configuration |
| `系统设置` | System-level settings |
| `算法调试` | Vision algorithm (Cognex VPP) debugging tool |
| `PLC监控` | Manual PLC I/O monitoring & motion control (S7-1500 via HslCommunication, Zmotion) |
| `选项卡` | Simple tab launcher form for module navigation |
| `AIsdk` | SmartMore ViMo AI wrapper — OCR, segmentation, classification inference |

**Core flow**: PLC triggers → `MainFrm` reads camera → ViMo AI inference → Cognex VisionPro inspection → results sent back to PLC → images optionally saved via `SaveImageQueues*` → production data recorded to SQLite via `AsyncDatabaseRecorder`.

**Config**: `Class_Config` is a thread-safe singleton; all persistent settings read/write `setup.ini` via P/Invoke INI API (`IniAPI`). Modules communicate through `IMainListener` / `IFormPlugin` interfaces.

## Key external dependencies (not NuGet)

These DLLs are referenced from external paths and must be present in `bin/`:
- `CLIDelegate.dll` — 大华 camera SDK
- `HslCommunication.dll` — Siemens S7 / Modbus comm
- `MT.Camera.SDK.dll` — Camera SDK wrapper
- `MyPictureBox.dll` — Custom picturebox control
- `System.Data.SQLite.dll` — SQLite
- `XL.Tool.dll`, `XL.UsbDog.dll`, `XL.Controls.dll`, `UIControl.dll` — Custom toolkit libs
- `SplashScreen.dll`
