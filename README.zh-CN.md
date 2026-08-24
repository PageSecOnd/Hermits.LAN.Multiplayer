# SlayTheSpire2.LAN.Multiplayer.Reforged

![LAN Multiplayer](mod_image.png)

这是一个面向 **《杀戮尖塔 2》** 的 **SlayTheSpire2.LAN.Multiplayer** 社区维护分支。

本项目旨在让原版 LAN Multiplayer Mod 继续兼容新版《杀戮尖塔 2》，同时尽可能保留原有的游戏体验与网络联机机制。

该 Mod 使用直接的 ENet 网络连接替代标准在线传输路径，使位于同一局域网或虚拟局域网中的玩家，可以在不依赖 Steam 房间或外部匹配服务的情况下创建和加入多人游戏。

> 本项目为非官方社区项目，与 Mega Crit 无任何关联。

* [English](README.md)
* [简体中文](README.zh-CN.md)

---

> [!WARNING]
> 请勿同时启用原版 `SlayTheSpire2.LAN.Multiplayer` 和 LAN Multiplayer: Reforged。安装本分支前，请先删除或禁用原版 Mod。

## 功能特性

* 通过本地局域网创建多人游戏
* 通过 IP 地址和端口加入多人游戏
* 支持标准模式、每日挑战和自定义模式
* 支持继续已有的局域网多人存档
* 使用独立的局域网多人存档数据
* 显示可用的本地地址和公网地址
* 点击地址即可复制
* 支持实体局域网和虚拟局域网
* 使用游戏原有的多人同步系统
* 包含已翻译的局域网设置与界面文本
* 安装 ModConfig 后可通过其界面进行配置

---

## 运行要求与兼容性

必需：

* 《杀戮尖塔 2》
* Windows
* BaseLib
* 所有玩家使用相同的游戏版本
* 所有玩家使用相同版本的 LAN Multiplayer: Reforged
* 所有玩家使用相同的内容型 Mod、Mod 版本和加载顺序

可选：

* ModConfig

已测试环境：

```text
《杀戮尖塔 2》正式版（2026-08-24 提供的程序集）
《杀戮尖塔 2》Beta 版（2026-08-24 提供的程序集）
Godot 4.5.1 Mono
.NET 9
```

2.0 版对正式版与 Beta 版只发布一个 DLL。已知 API 差异会在运行时探测并适配，各 Harmony Patch 组也会独立加载；因此，某个无关的游戏内部接口发生变化时，不会再导致整个 Mod 启动失败。

房间人数有意限制为游戏原生支持的四个槽位。这样可以完全沿用原版多人协议，避免重新改写大厅封包所带来的崩溃、不同步和跨版本失效问题。

BaseLib 是本 Mod 的必需依赖。如果 BaseLib 未安装、未启用或版本不兼容，本 Mod 将无法正常加载。

由于《杀戮尖塔 2》仍在持续更新，未来版本可能修改游戏内部 API，因此本 Mod 可能需要重新编译或进行兼容性更新。

---

## 安装方法

1. 安装并启用 BaseLib。
2. 下载最新版 LAN Multiplayer: Reforged。
3. 解压下载的压缩包。
4. 将压缩包中的 `mods` 文件夹复制到《杀戮尖塔 2》安装目录。

示例：

```text
Slay the Spire 2/
├─ SlayTheSpire2.exe
└─ mods/
   ├─ BaseLib/
   └─ SlayTheSpire2.LAN.Multiplayer.Reforged/
      ├─ mod_manifest.json
      ├─ SlayTheSpire2.LAN.Multiplayer.Reforged.dll
      └─ SlayTheSpire2.LAN.Multiplayer.Reforged.pck
```

5. 启动游戏。
6. 确认 BaseLib 和 LAN Multiplayer: Reforged 均已成功加载。
7. 确认游戏显示当前处于 Mod 模式。

请勿同时保留并启用原版 LAN Multiplayer 和 Reforged 版本。

---

## 创建局域网游戏

1. 启动《杀戮尖塔 2》。
2. 打开多人游戏菜单。
3. 选择局域网创建房间选项。
4. 选择标准模式、每日挑战或自定义模式。
5. 将显示的地址和端口发送给其他玩家。

主机必须在其他玩家连接期间保持游戏开启，并在整个联机过程中维持当前游戏会话。

---

## 加入局域网游戏

1. 启动《杀戮尖塔 2》。
2. 打开多人游戏加入界面。
3. 找到局域网连接区域。
4. 输入主机地址。
5. 点击局域网加入按钮。

支持的地址格式：

```text
192.168.1.100
192.168.1.100:33771
localhost
```

如果没有填写端口，则会使用设置中的默认局域网端口。

---

## 通过虚拟局域网联机

只要所有玩家都能够直接访问主机的虚拟 IP 地址，本 Mod 也可以配合虚拟局域网软件使用。

常见选择包括：

* Tailscale
* ZeroTier
* Radmin VPN
* Hamachi

在加入界面中输入主机的虚拟局域网地址即可，使用方式与普通局域网 IP 地址相同。

需要注意的是，虚拟局域网连接成功并不代表游戏端一定能够正常通信。防火墙规则、网络类型和路由设置仍可能需要手动调整。

---

## 配置

BaseLib 是本 Mod 的必需依赖。

ModConfig 为可选依赖。安装 ModConfig 后，可以通过其配置界面调整以下局域网选项：

* 默认局域网端口
* 最大玩家数量
* 连接超时时间
* 默认服务器地址
* 是否记住上次加入的地址
* 玩家名称
* 首选 Net ID

未安装 ModConfig 时，LAN Multiplayer: Reforged 会继续使用原有的游戏内设置界面。

无论通过 ModConfig 还是原有设置界面进行修改，配置值都会继续使用本 Mod 原有的局域网设置存储系统。

ModConfig 本身可能存在额外依赖和安装要求。

---

## Mod 兼容性

所有玩家应尽量使用完全相同的：

* 游戏版本
* LAN Multiplayer: Reforged 版本
* BaseLib 版本
* 内容型 Mod
* Mod 版本
* Mod 加载顺序

纯外观类差异可能不会造成问题，但仍然强烈建议所有玩家使用相同的 Mod 环境。

卡牌、遗物、角色、事件、敌人、章节或联机同步相关 Patch 存在差异时，可能导致连接失败、游戏不同步或中途断开连接。

排查问题时，建议先只启用：

```text
BaseLib
LAN Multiplayer: Reforged
```

确认基础联机功能正常后，再逐步恢复其他 Mod。

---

## 本地化

Reforged 资源包包含局域网多人菜单和设置界面的本地化文本。

本地化资源路径和翻译键已迁移至 Reforged 项目标识：

```text
SlayTheSpire2.LAN.Multiplayer.Reforged
```

附带的 PCK 资源包使用 Godot 4.5.1 Pack V3 格式。

如果游戏显示的是原始本地化键，而不是正常翻译文本，请确认 Reforged PCK 与 DLL 位于同一目录，并检查是否仍然加载了旧版 LAN Multiplayer PCK。

---

## 从源代码构建

### 前置要求

* .NET 9 SDK
* Git
* 已安装的《杀戮尖塔 2》
* BaseLib 或构建所需的 BaseLib 程序集引用
* 来自本地《杀戮尖塔 2》安装目录的游戏程序集

克隆仓库：

```powershell
git clone https://github.com/PageSecOnd/SlayTheSpire2.LAN.Multiplayer.Reforged.git
cd SlayTheSpire2.LAN.Multiplayer.Reforged
```

运行以下脚本构建项目：

```bat
build.bat
```

如果游戏不在项目默认路径，请将 `STS2_DIR` 设置为包含 `sts2.dll`、`GodotSharp.dll` 与 `0Harmony.dll` 的目录。维护者可以使用下列命令同时验证两个游戏渠道：

```powershell
./scripts/verify-dual-version.ps1 -StableDir <正式版数据目录> -BetaDir <Beta版数据目录>
```

适配后续游戏更新前，请先阅读[兼容架构说明](docs/COMPATIBILITY.md)。

编译后的 DLL 通常位于：

```text
SlayTheSpire2.LAN.Multiplayer.Reforged/bin/Release/net9.0/
```

本项目会引用本地《杀戮尖塔 2》安装目录中的程序集。这些游戏程序集不会包含在仓库中，也不得进行重新分发。

---

## 故障排除

### 游戏中没有显示本 Mod

请确认：

* Mod 文件夹直接位于游戏的 `mods` 目录中
* Mod 文件夹中包含 `mod_manifest.json`
* BaseLib 已安装并启用
* DLL 和 PCK 文件均存在
* 原版 LAN Multiplayer 已被禁用

### 游戏提示缺少依赖

请确认 BaseLib 已安装、已启用，并且与当前游戏版本兼容。

BaseLib 是必需依赖。LAN Multiplayer: Reforged 无法在缺少 BaseLib 的情况下独立运行。

### 设置界面显示未翻译的本地化键

例如：

```text
settings_ui.SlayTheSpire2.LAN.Multiplayer.Reforged.HOST_PORT
```

请确认：

* 已安装 `SlayTheSpire2.LAN.Multiplayer.Reforged.pck`
* PCK 与 Mod DLL 位于同一目录
* PCK 使用 Godot 4.5.1 Pack V3 格式
* 游戏没有加载旧版 LAN Multiplayer PCK
* DLL 和 PCK 来自同一个发布版本

### 客户端无法连接

请确认：

* 主机已经创建局域网房间
* IP 地址正确
* 端口正确
* 所有玩家位于同一局域网或虚拟局域网
* Windows 防火墙已允许游戏通信
* 主机和客户端使用相同的游戏版本
* 主机和客户端使用相同的 Mod 版本
* 主机和客户端使用兼容的 BaseLib 版本

### 连接超时

连接超时通常意味着客户端无法访问主机。

请检查：

* 主机是否仍停留在多人游戏房间中
* 主机地址是否可以访问
* 当前端口是否被防火墙阻止
* 虚拟局域网连接是否正常
* VPN 是否将流量错误地路由到其他网络接口
* 连接超时时间是否设置得过短

### 玩家断开连接或游戏不同步

请暂时禁用其他所有会修改游戏内容的 Mod，然后重新测试。

如果问题消失，请分批恢复其他 Mod，直到找到存在兼容性问题的 Mod。

所有参与者应使用相同的游戏版本、Mod 版本、BaseLib 版本和 Mod 加载顺序。

---

## 截图

![截图 1](Screenshot/screenshot1.png)

![截图 2](Screenshot/screenshot2.png)

![截图 3](Screenshot/screenshot3.png)

![截图 4](Screenshot/screenshot4.png)

![截图 5](Screenshot/screenshot5.png)

![截图 6](Screenshot/screenshot6.png)

---

## 贡献

欢迎提交错误报告和 Pull Request。

报告问题时，请尽量提供：

* 游戏版本
* LAN Multiplayer: Reforged 版本
* BaseLib 版本
* ModConfig 版本（如已安装）
* 完整游戏日志
* 主机和客户端操作系统
* 已安装 Mod 列表
* 可稳定复现问题的步骤

源代码仓库：

https://github.com/PageSecOnd/SlayTheSpire2.LAN.Multiplayer.Reforged

---

## 致谢

本项目是 **kmyuhkyuk** 创建的 **SlayTheSpire2.LAN.Multiplayer** 社区维护分支。

原项目仓库：

https://github.com/kmyuhkyuk/SlayTheSpire2.LAN.Multiplayer

原版局域网多人联机功能由 kmyuhkyuk 实现。

本分支在其基础上增加并维护了新版游戏兼容性修复、BaseLib 集成、可选 ModConfig 支持，以及迁移后的 Reforged 本地化资源。

原项目采用 GPL-3.0 协议发布，本分支继续采用相同许可证。

---

## 许可证

本项目采用 GNU General Public License v3.0 协议发布。

详情请参阅 [LICENSE](LICENSE)。
