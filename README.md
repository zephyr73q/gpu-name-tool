# GPU Name Tool · 显卡名称工具

**简体中文** | [English](README.en.md)

一个轻量的 Windows 桌面工具，用于修改显示适配器的设备描述，支持修改前自动备份与原始值恢复。

**修改的是名称，不是显卡硬件。** 工具不会改变硬件 ID、显存、驱动能力或性能。直接读取驱动或硬件的应用可能继续显示真实型号。当前图形界面为简体中文。

## 功能

- 列出当前连接的显示适配器，支持多显卡。
- 自定义名称，或选择 `NVIDIA GeForce GTX 1050 Ti`、`Radeon RX 560` 预设。
- 修改前保存首次备份，后续修改不会覆盖它。
- 恢复原始值与注册表类型，包括恢复为原本不存在的状态。
- 校验设备与驱动身份，并在写入后读取核对。
- 对无效输入、备份损坏、权限不足等情况显示错误提示。
- 本地运行，无联网调用、无密码、无第三方运行时包。

## 运行要求

- 64 位 Windows 10 或 Windows 11，安装 .NET Framework 4.x；建议使用 .NET Framework 4.8 或更高版本。
- 修改和恢复设备描述需要管理员权限；查看设备和创建备份可使用普通权限。

## 编译与运行

在本地项目目录中，通过 Windows PowerShell 或 PowerShell 执行：

```powershell
.\scripts\build.ps1
.\bin\GpuNameTool.exe
```

构建脚本使用 Windows 随附的 .NET Framework C# 编译器，不下载依赖。如果电脑限制本地脚本运行，请遵循现有脚本执行策略；项目不会修改该策略。

需要预编译的便携包时，可进入仓库 **Actions** 页面，打开一次成功的 **Build and test** 运行，下载 `gpu-name-tool-windows-x64` 构建产物。解压下载内容及其中的 ZIP 后，运行 `GpuNameTool.exe`。构建文件未进行数字签名。

## 使用方法

1. 打开工具，选择需要修改的显示适配器。
2. 点击“管理员模式”，或右键 EXE 选择“以管理员身份运行”。
3. 输入新名称，或选择预设。
4. 点击“备份并修改”，核对显卡和名称后确认。
5. 需要还原时，选择同一显卡，点击“恢复原始备份”。

仅打开程序不会修改设备设置。工具不会自动重启电脑。部分界面需要关闭重开，或手动重启 Windows 后，才会显示更新后的描述。

## 实现原理

程序通过 Windows SetupAPI 枚举当前连接的显示适配器，再读取或写入选中设备实例下的 `DeviceDesc` 值：

```text
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\<设备实例 ID>
```

新名称使用 `REG_SZ` 类型。恢复时会保留备份中的 `REG_SZ` 或 `REG_EXPAND_SZ` 类型以及未经展开的原始文本；如果原本不存在 `DeviceDesc`，恢复操作会删除该值。

工具不会修改 `FriendlyName`、硬件 ID、固件、驱动文件、注册表权限、启动设置或 Windows 安全设置。Windows 或驱动可能拒绝或覆盖这项修改。部分界面优先显示 `FriendlyName`；不能保证名称变化会影响某个游戏或应用。

微软参考：[设备描述属性](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-devicedesc)、[SetupDiGetClassDevs](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetclassdevsw)。

## 备份与恢复

备份仅保存在本机：

```text
%LOCALAPPDATA%\GpuNameTool\Backups
```

点击“打开备份目录”即可查看。每个 JSON 文件包含设备实例 ID、机器/设备/驱动指纹、UTC 时间、原始值、注册表类型及原值是否存在。分享日志或截图时，请保留这些文件的私密性。

不同机器、设备与驱动身份分别保存备份。备份损坏或身份不匹配时会阻止操作；备份保存失败时，不会写入新描述。恢复后会保留备份文件。

“原始值”指本工具首次备份时的值。如果此前已经被其他程序改名，备份的就是当时的名称。工具无法自动推断出厂值，也不会导入其他程序的文本备份。更换驱动或重装系统后，备份身份可能发生变化。

## 测试与验证范围

```powershell
.\scripts\test.ps1
```

10 项隔离测试覆盖中文名称、首次备份保留、精确恢复、原值不存在、无效名称、损坏或错配备份、备份保存失败、设备写入被拒绝、备份缺失、驱动身份变化及非法设备路径。测试使用内存中的模拟设备与临时文件，不写入真实显卡设置。

本地已验证两个显示适配器的只读检测和中文界面。**用户已在本机验证真实显卡描述修改成功，并确认游戏内标识随之变化。** 这是该本机环境中的验证结果，其他硬件、驱动和游戏中的效果仍需分别确认；真实显卡上的恢复操作尚未单独验证。

构建、测试并生成带校验和的便携 ZIP：

```powershell
.\scripts\package.ps1
```

输出文件为 `dist/gpu-name-tool-windows-x64.zip` 和 `dist/SHA256SUMS.txt`。GitHub Actions 使用相同脚本进行自动构建。

## 项目结构

```text
src/                         程序、设备访问逻辑和清单
tests/                       隔离测试
scripts/build.ps1            编译 Windows 程序
scripts/test.ps1             编译并运行隔离测试
scripts/package.ps1          构建、测试与打包
.github/workflows/build.yml  Windows 自动构建
README.md                    中文文档
README.en.md                 英文文档
```

生成的 EXE、压缩包、设备备份和本机诊断记录均不提交到 Git。本仓库包含独立编写的实现，不包含第三方优化包及其可执行文件。
