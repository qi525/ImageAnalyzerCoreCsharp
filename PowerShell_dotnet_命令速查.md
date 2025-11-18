# PowerShell 与 dotnet 命令速查（详解）

说明：本文档汇总了在开发、构建、调试和打包 .NET 项目时常用的 PowerShell 命令与 dotnet 命令，包含会话中实际使用到的组合命令与示例，方便灵活运用。

---

**文件路径**：`c:\个人数据\C#Code\ImageAnalyzerCore\PowerShell_dotnet_命令速查.md`

更新时间：2025-11-18

---

## 目录

- 基本 dotnet 命令（简要）
- 常用 PowerShell 原语与命令
- 会话中常用组合示例（逐段说明）
- 常见问题与处理模式
- 常用脚本片段（可直接复制运行）

---

## 一、常用 dotnet 命令（快照）

- `dotnet build -c Release`：以 Release 配置构建（启用优化）。
- `dotnet run`：编译并运行控制台应用（Debug 默认为 -c Debug）。
- `dotnet run -c Release`：以 Release 配置运行。
- `dotnet publish -c Release -o ./publish`：发布到 `./publish`（框架依赖）。
- `dotnet publish -c Release --self-contained -r win-x64 -o ./publish`：自包含发布（包含 Runtime）。
- `dotnet publish -c Release -p:PublishSingleFile=true -r win-x64`：单文件发布（Windows x64）。
- `dotnet clean`：删除 `bin`/`obj` 输出。
- `dotnet restore`：还原 NuGet 依赖。
- `dotnet pack -c Release`：为库生成 `.nupkg`。

说明：**`-c` / `--configuration` 指定构建配置（Debug/Release）**。

---

## 二、常用 PowerShell 原语与命令（逐条）

- `cd <path>`：切换目录。
  - 示例：`cd 'c:\个人数据\C#Code\ImageAnalyzerCore'`

- `Stop-Process -Name <procName> -Force -ErrorAction SilentlyContinue`：结束同名进程（强制），并忽略错误（例如进程不存在时）。
  - 会话示例：`Stop-Process -Name ImageAnalyzerCore -Force -ErrorAction SilentlyContinue`
  - 注意：优先使用 `-Name` 或者通过 PID（`-Id <pid>`）更精确地结束进程。

- `Get-Process -Name <procName>`：列出进程。
  - 示例：`Get-Process -Name ImageAnalyzerCore`

- `Start-Sleep -Seconds <n>`：暂停脚本执行（等待进程释放文件句柄等）。
  - 示例：`Start-Sleep -Seconds 2`

- `echo "text" | <command>`：将字符串通过管道传给命令（PowerShell 中 `echo` 是 `Write-Output` 的别名）。
  - 示例：`echo "9" | dotnet run`（将文本 9 输入到交互式程序）

- 管道与选择输出：
  - `| Select-Object -First N`：取前 N 行（类似 Linux 的 `head`）。
  - `| Select-Object -Last N`：取最后 N 行（类似 `tail`）。
  - 示例：`dotnet build 2>&1 | Select-Object -Last 5`

- 错误/标准输出重定向：`2>&1` 把标准错误合并到标准输出，方便统一处理。
  - 示例：`dotnet build 2>&1 | Select-Object -Last 10`

- `Select-String`：在输出或文件中搜索字符串（类似 `grep`）。
  - 示例：`dotnet build 2>&1 | Select-String "error"`

- `Compress-Archive -Path <src> -DestinationPath <dest.zip>`：压缩文件（内置）。
  - 示例：
    ```powershell
    Compress-Archive -Path ./publish -DestinationPath ./ImageAnalyzerCore_v1.0.zip
    ```

- `7z`（第三方，需要安装 7-Zip）：高压缩率/多选项。
  - 示例：`7z a ImageAnalyzerCore_v1.0.7z ./publish`

- `Test-Path <path>`：判断路径是否存在，返回布尔。
  - 示例：`if (Test-Path $path) { ... }`

- `Get-ChildItem` (`gci`/`ls`)：列出目录内容。
  - 示例：`Get-ChildItem -Path . -Recurse -Filter "*.jpg"`

- `Remove-Item -Path <path> -Recurse -Force`：删除文件/目录。

- `Out-File` / `Set-Content`：将字符串写入文件。
  - 示例：`"content" | Out-File -FilePath ./log.txt -Encoding utf8`

- `Start-Process`：以独立进程启动程序，可指定 `-NoNewWindow`、`-Wait` 等。
  - 示例：`Start-Process -FilePath "notepad.exe" -ArgumentList "file.txt"

- `Write-Host` / `Write-Output`：输出信息到控制台。

---

## 三、会话中常见组合命令（逐段解释）

下面列出会话中实际出现过的组合命令，解释每一段的含义与用途。

### 示例 A：停止旧进程、等待、构建

```powershell
Stop-Process -Name ImageAnalyzerCore -Force -ErrorAction SilentlyContinue ; Start-Sleep -Seconds 2 ; cd 'c:\个人数据\C#Code\ImageAnalyzerCore' ; dotnet build 2>&1 | Select-Object -Last 5
```

解释：
- `Stop-Process -Name ImageAnalyzerCore -Force -ErrorAction SilentlyContinue`：尝试结束名为 `ImageAnalyzerCore` 的进程，若不存在则静默继续。
- `;`：PowerShell 中的命令分隔符（与 Linux 中的 `&&` 不同，`;` 不会因为前一条失败而停止）。
- `Start-Sleep -Seconds 2`：等待 2 秒，确保进程退出，释放锁定的文件句柄。
- `cd '...path...'`：切到项目目录。
- `dotnet build 2>&1`：构建项目，同时将错误流重定向到标准输出。
- `| Select-Object -Last 5`：仅显示输出的最后 5 行（通常是构建摘要），便于快速查看。

用途：当构建失败提示“文件被另一个进程占用”时，这是一个常见的解决步骤。

---

### 示例 B：用管道传入交互输入并运行

```powershell
echo "9" | dotnet run 2>&1 | Select-Object -First 50
```

解释：
- `echo "9" | dotnet run`：把字符串 `9` 通过标准输入传给 `dotnet run`（适用于程序在启动后等待用户输入菜单选择时自动选择）。
- `2>&1`：把错误流也合并到标准输出。
- `| Select-Object -First 50`：只显示前 50 行输出，避免终端被大量日志刷屏。

注意：部分交互式程序对标准输入行为敏感，管道输入适用于简单菜单或单次输入场景。

---

### 示例 C：查看构建错误（搜索）

```powershell
dotnet build 2>&1 | Select-String "error"
```

解释：把构建输出中的所有错误信息筛选出来，便于快速定位。

---

### 示例 D：发布并压缩

```powershell
# 发布为自包含应用
dotnet publish -c Release --self-contained -r win-x64 -o ./publish
# 压缩发布目录
Compress-Archive -Path ./publish -DestinationPath ./ImageAnalyzerCore_v1.0.zip
```

说明：用于生成可直接分发的压缩包。

---

## 四、常见问题与处理模式

1. 文件被锁定（MSB3021）：
   - 原因：某个运行中的进程（通常是上一次运行的程序）占用了输出文件。
   - 处理模式：
     ```powershell
     Stop-Process -Name ImageAnalyzerCore -Force -ErrorAction SilentlyContinue
     Start-Sleep -Seconds 2
     dotnet build
     ```
   - 或者：通过 PID 精确结束进程：`Stop-Process -Id 12345 -Force`

2. 只查看构建摘要而不看全部日志：
   - `dotnet build 2>&1 | Select-Object -Last 10`

3. 在 CI 或脚本中保证顺序执行并在失败时退出：
   - PowerShell 中可用 `;` 链接命令，但 `;` 不会在前一命令失败时停止。若希望在失败时退出脚本，可使用 `if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }` 或 `-and` 等逻辑构造在复杂脚本中处理。

4. 需要持续观察输出但又要截断：
   - 使用 `Select-Object -First/Last N`，或将输出重定向到文件 `dotnet build 2>&1 | Tee-Object -FilePath build.log`。

---

## 五、常用脚本片段（拷贝即可运行）

1) 安全重建脚本（停止进程 -> 构建）

```powershell
# 停止旧进程（如果存在），等待并构建
Stop-Process -Name ImageAnalyzerCore -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Set-Location 'c:\个人数据\C#Code\ImageAnalyzerCore'
# 构建并仅显示摘要
dotnet build 2>&1 | Select-Object -Last 20

if ($LASTEXITCODE -ne 0) { Write-Error "Build failed with exit code $LASTEXITCODE"; exit $LASTEXITCODE }
```

2) 自动化运行特定菜单选项（将输入通过管道传入）

```powershell
# 将菜单选项 9 传入程序，并取前 80 行输出
echo "9" | dotnet run 2>&1 | Select-Object -First 80
```

3) 发布为自包含单文件并压缩

```powershell
# 发布
dotnet publish -c Release -p:PublishSingleFile=true -p:SelfContained=true -r win-x64 -o ./publish
# 压缩
Compress-Archive -Path ./publish -DestinationPath ./ImageAnalyzerCore_release.zip
```

4) 清理 NuGet 缓存并还原

```powershell
dotnet clean
dotnet nuget locals all --clear
dotnet restore -v m
```

5) 在 CI 中只输出错误

```powershell
# 获取构建中的错误字符串
dotnet build 2>&1 | Select-String "error"
```

---

## 六、PowerShell 与跨平台注意事项

- `;` 是 PowerShell 的命令分隔符（不像 Bash 的 `&&` 会在前面失败时停止），如果需要根据返回码决定是否继续，请用 `$LASTEXITCODE` 或 `if` 判断。
- 在 Windows PowerShell / PowerShell Core 中大多数命令相同，但 Linux/macOS 下没有内置 `Compress-Archive`（可使用 `tar` 或 `zip`）。
- `head`、`tail` 等 GNU 工具在 PowerShell 原始环境下不可用（会话中出现的 `head` 报错即为例证），请用 `Select-Object -First/Last` 代替。

---

## 七、快速参考表（常用命令一览）

- 结束进程：`Stop-Process -Name <name> -Force`
- 等待：`Start-Sleep -Seconds <n>`
- 构建：`dotnet build -c Release`
- 运行：`dotnet run -c Release`
- 发布：`dotnet publish -c Release -o ./publish`
- 自包含发布：`dotnet publish -c Release --self-contained -r win-x64 -o ./publish`
- 单文件：`dotnet publish -c Release -p:PublishSingleFile=true -r <RID>`
- 压缩：`Compress-Archive -Path ./publish -DestinationPath ./app.zip`
- 查看最后若干行：`dotnet build 2>&1 | Select-Object -Last 10`
- 搜错误：`dotnet build 2>&1 | Select-String "error"`

---

如果你希望我把这些片段再整理成一个可执行的 PowerShell 脚本（例如 `build_and_publish.ps1`），或者需要针对 Linux/macOS 的等价脚本，我可以接着生成并测试。下一步我会把 TODO（id=2）标为已完成并把第3条标记为完成（保存文件）。