# RaiDrive AdGuard

屏蔽 RaiDrive Mount 开机广告弹窗。

RaiDrive Mount 每次启动约 15 秒后，会在屏幕右下角弹出一个无边框广告面板。这是一个约 20 KB 的守候程序，在它显示出来之前就关掉。不碰 RaiDrive 的任何文件，不破坏数字签名，应用升级后依然有效。

```
之前:  [ ads.raidrive.com 336x280 广告面板 ]
之后:  什么都没弹
```

[English](README.md) | 简体中文

## 弹窗特征

以下数据实测于 RaiDrive Mount 2026.6.25：

| | |
|---|---|
| 属主进程 | `RaiDrive.Mount.exe`（它自己的 WPF 窗口，不是独立的推广 exe） |
| 窗口类名 | `HwndWrapper[RaiDrive.Mount;;<guid>]` |
| 窗口标题 | 空 |
| 内容宿主 | 内嵌 WebView2（Edge） |
| 广告位尺寸 | `336x280` |
| 页面标题 | `STANDARD 2026.6.25 \| RaiDrive` |
| 内容来源 | `ads.raidrive.com` — `/alt/250x250` `/alt/336x280` `/alt/728x90` `/standard/u/widget_202309` |
| 托管代码 | `RaiDrive.Center.ViewModels.AdvertisingViewModel`（`VisibleAdvertisement`、`DoNotice`、`NoticeKey`） |
| 出现时机 | 启动后约 15 秒 |

程序集经过 ReleaseManagedObf 混淆，用户设置存在 `HKCU\Software\OpenBoxLab\RaiDrive Mount\Users\{guid}` 下的加密二进制块里，没有明文开关可以改。

### 为什么常见办法没用

在 `hosts` 里屏蔽 `ads.raidrive.com` 不管用。请求确实失败了，但 RaiDrive 照样把 WebView2 窗口建起来，广告变成了一个空白框。弹窗没消失，只是变难看了。

## 工作原理

一个常驻的窗口守候。窗口正要显示的时候先把它隐藏，再确认是不是广告外壳，是就关掉。关窗口不影响 RaiDrive，主程序照常运行，挂载也不会断。

### 匹配条件

五条必须同时满足，所以 RaiDrive 的正常窗口不会被误伤：

1. 顶层窗口（没有父窗口）
2. 类名以 `HwndWrapper[RaiDrive` 开头
3. **窗口标题为空**——真正的界面窗口（`RaiDrive Mount`、`Hidden Window`）都有标题
4. 属主进程是 `RaiDrive.Mount`
5. 存在 `Chrome_*` 子窗口（WebView2 宿主）

### 双保险

- **WinEvent 钩子**——监听 `EVENT_OBJECT_CREATE` / `EVENT_OBJECT_SHOW`。命中后立刻 `SW_HIDE`，然后异步确认：WebView2 宿主出现了就发 `WM_CLOSE`，没出现就用 `SW_SHOW` 还原。
- **20ms 巡检**——最高优先级线程轮询顶层窗口，兜住钩子来得晚的那一两拍。

两条路径都是先隐藏再关闭，所以即使钩子晚到，画面也不会闪出来。

## 安装

需要 Windows 10/11 x64。不用编译，复制二进制再注册自启即可。

```powershell
$dst = "$env:LOCALAPPDATA\RaiDriveAdGuard"
New-Item -ItemType Directory -Force -Path $dst | Out-Null
Copy-Item .\RaiDriveAdGuard.exe $dst

Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
  -Name 'RaiDriveAdGuard' -Value "$dst\RaiDriveAdGuard.exe" -Type String

Start-Process "$dst\RaiDriveAdGuard.exe"
```

或者直接跑仓库里的 `install.ps1`，普通权限的 PowerShell 就行，不用管理员。

## 验证

重启 RaiDrive，盯着右下角：

```powershell
Get-Process RaiDrive.Mount | Stop-Process -Force
Start-Process 'C:\Program Files\OpenBoxLab\RaiDrive Mount\RaiDrive.Mount.exe' -ArgumentList '/background'
```

窗口不应该出现。exe 旁边的 `guard.log` 会记录每一次拦截：

```
2026-11-16 17:49:52.382 === start pid=10528 ===
2026-11-16 17:49:52.421 hooks create=80151785 show=60294571
2026-11-16 17:50:08.240 closed hwnd=2098474 159x27 via sweep
```

守候程序与 RaiDrive 同时冷启动三轮，每轮采样 60 秒、间隔 200ms：

| 轮次 | 可见广告帧数 | 主程序存活 |
|---|---|---|
| 1 | 0 | 是 |
| 2 | 0 | 是 |
| 3 | 0 | 是 |

映射盘和 `RaiDriveMountService` 均不受影响。

## 用法

```powershell
# 关掉当前已经显示在屏幕上的广告框
& "$env:LOCALAPPDATA\RaiDriveAdGuard\RaiDriveAdGuard.exe" --scan
```

## 卸载

```powershell
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'RaiDriveAdGuard'
Get-Process RaiDriveAdGuard -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item "$env:LOCALAPPDATA\RaiDriveAdGuard" -Recurse -Force
```

RaiDrive 本身没有任何改动，广告恢复如初。

## 编译

不需要装 SDK，Windows 自带的 .NET Framework 编译器就够了。先停掉正在运行的实例，否则 exe 被占用。

```
csc.exe -nologo -target:winexe -optimize+ -platform:x64 -out:RaiDriveAdGuard.exe AdGuard.cs
```

`csc.exe` 位于 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`。它只支持 C# 5，所以源码避开了较新的语法。

> `AdGuard.cs` 必须保持纯 ASCII。中文环境下 .NET Framework 编译器按 ANSI 解析源码，遇到任何非 ASCII 字节都会报"未终止的字符串"这种莫名其妙的错误。

## 已知限制

- 守候程序要比 RaiDrive 先起来或同时起来。实测同时自启是干净的，但越早起越稳。
- 如果 RaiDrive 大版本改了窗口结构（换类名、去掉 WebView2），匹配会失效。届时看 `guard.log`，然后调整 `IsShellCandidate` 和 `HasWebView`。
- 只处理这一种弹窗形态。日后若出现别的推广窗，需要另加规则。

## 许可

MIT，见 [LICENSE](LICENSE)。

与 OpenBoxLab 无关。只做窗口匹配，不包含也不分发 RaiDrive 的任何代码。
