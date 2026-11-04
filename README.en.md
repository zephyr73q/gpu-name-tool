# GPU Name Tool

[简体中文](README.md) | **English**

A small Windows desktop utility for changing a display adapter's device description, with automatic backup and exact-value restore.

**This changes a name, not the GPU.** It does not change the hardware ID, VRAM, driver capabilities, or performance. Applications that query the driver or hardware directly may continue to show the actual model. The current graphical interface is in Simplified Chinese.

## Features

- List currently connected display adapters, including systems with multiple GPUs.
- Enter a custom name or choose `NVIDIA GeForce GTX 1050 Ti` / `Radeon RX 560`.
- Save the first backup before changing a description; later changes keep that backup.
- Restore the original value and registry type, including a value that was originally absent.
- Verify the device and driver identity and read back each write.
- Display errors for invalid input, damaged backups, and denied registry access.
- Run locally without network calls, a password, or third-party runtime packages.

## Requirements

- 64-bit Windows 10 or Windows 11 with .NET Framework 4.x; use .NET Framework 4.8 or later.
- Administrator rights to modify or restore a device description. Viewing devices and creating backups can run without elevation.

## Build and run

From a local checkout, run in Windows PowerShell or PowerShell:

```powershell
.\scripts\build.ps1
.\bin\GpuNameTool.exe
```

The script uses the .NET Framework C# compiler installed with Windows. It does not download dependencies. If local script execution is restricted, follow your machine's normal script policy; the project does not change that policy.

For a prebuilt portable package, open a successful **Build and test** run on the repository's **Actions** page and download the `gpu-name-tool-windows-x64` artifact. Extract the artifact and its inner ZIP, then run `GpuNameTool.exe`. Builds are unsigned.

## Usage

1. Open the application and select a display adapter.
2. Click **管理员模式** (Administrator mode), or run the EXE as administrator.
3. Enter a new name or choose a preset.
4. Click **备份并修改** (Back up and change), review the adapter and name, and confirm.
5. To undo the change, select the same adapter and click **恢复原始备份** (Restore original backup).

Opening the application does not change device settings. It does not restart Windows automatically. Some interfaces need to be reopened or Windows restarted manually before the new description appears.

## How it works

The application uses Windows SetupAPI to enumerate present display adapters, then reads or writes only the `DeviceDesc` value under the selected device instance:

```text
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\<device-instance-id>
```

New descriptions use `REG_SZ`. Restore preserves the backed-up `REG_SZ` or `REG_EXPAND_SZ` type and unexpanded original text. If `DeviceDesc` did not exist, restore removes the value.

The tool does not modify `FriendlyName`, hardware IDs, firmware, driver files, registry permissions, boot settings, or Windows security settings. Windows or a driver can deny or replace the change. Some interfaces prefer `FriendlyName`; a name change is not guaranteed to affect any particular game or application.

Microsoft references: [device description property](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-devicedesc) and [SetupDiGetClassDevs](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetclassdevsw).

## Backups and recovery

Backups are stored locally in:

```text
%LOCALAPPDATA%\GpuNameTool\Backups
```

Use **打开备份目录** (Open backup folder) to find them. Each JSON record contains the device instance ID, a machine/device/driver fingerprint, a UTC timestamp, the original value, its registry type, and whether it existed. Keep these files private when sharing logs or screenshots.

Backups are separated by machine, device, and driver identity. A damaged or mismatched backup blocks the operation. If saving a backup fails, the tool does not write the new description. Restoring keeps the backup file.

“Original” means the value at the time this tool first backed it up. If another tool had already renamed the GPU, that name is what gets backed up. This tool cannot infer the factory value and does not import another program's text backups. Driver changes or a Windows reinstall may produce a different backup identity.

## Tests and validation

```powershell
.\scripts\test.ps1
```

The 10 isolated tests cover Unicode names, immutable first backups, exact restore, originally absent values, invalid names, damaged or mismatched backups, backup failures, write denials, missing backups, driver identity changes, and malformed device paths. They use an in-memory device store and temporary files; they do not write real GPU settings.

Local validation covered read-only detection of two display adapters and the Chinese UI. **The user has verified a successful description change on a real GPU and confirmed that the in-game identifier changed as well.** This result applies to that local environment; behavior with other hardware, drivers, and games still needs separate validation. Restoring a real GPU's value has not been separately validated.

To build, test, and create a portable ZIP with a checksum:

```powershell
.\scripts\package.ps1
```

Output: `dist/gpu-name-tool-windows-x64.zip` and `dist/SHA256SUMS.txt`. GitHub Actions runs the same script.

## Project layout

```text
src/                         Application, device access, and manifest
tests/                       Isolated test runner
scripts/build.ps1            Build the Windows executable
scripts/test.ps1             Compile and run isolated tests
scripts/package.ps1          Build, test, and package
.github/workflows/build.yml  Windows CI
README.md                    Chinese documentation
README.en.md                 English documentation
```

Generated executables, packages, backups, and local diagnostics are excluded from Git. This repository contains the independently written implementation; it does not include the third-party optimization bundle or its executables.
