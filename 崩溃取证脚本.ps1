# ============================================================
# VisionMeasure 崩溃取证脚本 —— 在【现场设备电脑】上运行
# 用法：右键"使用 PowerShell 运行"，或命令行:
#   powershell -ExecutionPolicy Bypass -File 崩溃取证脚本.ps1
# 程序目录不在 D:\bin 时指定: powershell -ExecutionPolicy Bypass -File 崩溃取证脚本.ps1 -ExeDir "D:\bin"
# 结果：桌面生成"崩溃取证_日期时间"文件夹，整个文件夹拷回给开发即可
# ============================================================
param(
    [string]$ExeDir = ""   # 程序所在目录；留空则自动搜索 C:\ D:\ 下的 VisionMeasure.exe
)

$ErrorActionPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$ts = Get-Date -Format "yyyyMMdd_HHmmss"
$outRoot = Join-Path ([Environment]::GetFolderPath('Desktop')) "崩溃取证_$ts"
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null
$report = Join-Path $outRoot "取证报告.txt"

function Log($msg) {
    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $msg
    Add-Content -Path $report -Value $line -Encoding UTF8
    Write-Host $line
}

Log "===== VisionMeasure 崩溃取证开始 ====="
Log "本机名: $env:COMPUTERNAME  系统: $((Get-CimInstance Win32_OperatingSystem).Caption)"

# 1. 定位程序目录
if ($ExeDir -eq "") {
    $found = Get-ChildItem -Path "C:\","D:\" -Filter "VisionMeasure.exe" -Recurse -Depth 4 -ErrorAction SilentlyContinue | Select-Object -First 3
    foreach ($f in $found) { Log "发现程序: $($f.FullName)  (修改时间 $($f.LastWriteTime))" }
    $ExeDir = if ($found) { $found[0].DirectoryName } else { "" }
}
if ($ExeDir -eq "") {
    Log "!! 未找到 VisionMeasure.exe，请用参数指定目录: -ExeDir D:\bin"
} else {
    Log "程序目录: $ExeDir"
    # 2. session.lock —— 判断上次是否正常退出 + 死亡时刻
    $lock = Join-Path $ExeDir "session.lock"
    if (Test-Path $lock) {
        $info = Get-Item $lock
        Log ">>> session.lock 存在（上次会话【未】正常退出！疑似崩溃/强杀/断电）"
        Log "    内容: $(Get-Content $lock -Raw)"
        Log "    文件最后写入时间(≈进程死亡时刻,误差30秒内): $($info.LastWriteTime)"
        Copy-Item $lock $outRoot -Force
    } else {
        Log "session.lock 不存在（上次会话是正常关闭流程退出的）"
    }
    # 3. 拷贝日志目录
    $logDir = Join-Path $ExeDir "Logs"
    if (Test-Path $logDir) {
        $destLog = Join-Path $outRoot "Logs"
        Copy-Item $logDir $destLog -Recurse -Force
        $crashFiles = Get-ChildItem $logDir -Filter "crash_*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime
        Log ">>> 崩溃日志 crash_*.log 共 $($crashFiles.Count) 个:"
        foreach ($c in $crashFiles) {
            Log "  --- $($c.Name) ($($c.LastWriteTime)) ---"
            Get-Content $c.FullName -ErrorAction SilentlyContinue | ForEach-Object { Log "  $_" }
        }
        $appLogs = Get-ChildItem $logDir -Filter "app_*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 2
        foreach ($a in $appLogs) {
            Log "  === 最新运行日志 $($a.Name) ($($a.LastWriteTime)) 最后80行 ==="
            Get-Content $a.FullName -Tail 80 -ErrorAction SilentlyContinue | ForEach-Object { Log "  $_" }
        }
        $dumps = Get-ChildItem (Join-Path $logDir "Dumps") -Filter "*.dmp" -ErrorAction SilentlyContinue
        Log ">>> 转储文件 .dmp 共 $($dumps.Count) 个（如有，务必一起拷回，用 VS 打开即可定位崩溃点）"
    } else {
        Log "!! 程序目录下没有 Logs 文件夹"
    }
}

# 4. Windows 事件日志：程序崩溃记录（最近7天）
Log "===== 事件日志：Application Error / .NET Runtime (最近7天) ====="
$evts = Get-WinEvent -FilterHashtable @{LogName='Application'; Id=1000,1026; StartTime=(Get-Date).AddDays(-7)} -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -match 'VisionMeasure' -or $_.ProviderName -match '\.NET Runtime' }
if ($evts) {
    foreach ($e in $evts) { Log "  [$($e.TimeCreated)] $($e.ProviderName) Id=$($e.Id)`n$($e.Message)" }
} else { Log "  无 VisionMeasure 相关崩溃记录" }

# 5. 事件日志：磁盘故障（磁盘问题是"进程凭空消失"的另一大嫌疑）
Log "===== 事件日志：磁盘错误 (最近7天, System日志) ====="
$diskevts = Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='disk','Ntfs','volmgr'; StartTime=(Get-Date).AddDays(-7)} -ErrorAction SilentlyContinue |
            Where-Object { $_.Id -in 7,51,52,55,98,153,157 -or $_.LevelDisplayName -match '错误|Error' }
$cnt = 0
foreach ($e in $diskevts) { $cnt++; if ($cnt -le 10) { Log "  [$($e.TimeCreated)] $($e.ProviderName) Id=$($e.Id) $($e.Message.Split([char]10)[0])" } }
Log "  磁盘错误事件总数: $cnt"

# 6. 系统概况：内存/磁盘空间（排查 OOM 与盘满）
Log "===== 系统概况 ====="
$os = Get-CimInstance Win32_OperatingSystem
Log "内存: 总$([math]::Round($os.TotalVisibleMemorySize/1MB,1))GB 可用$([math]::Round($os.FreePhysicalMemory/1MB,1))GB"
Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" | ForEach-Object { Log "磁盘 $($_.DeviceID) 总$([math]::Round($_.Size/1GB,1))GB 剩余$([math]::Round($_.FreeSpace/1GB,1))GB" }

# 7. 当前是否正在运行 + 运行时长
$proc = Get-Process VisionMeasure -ErrorAction SilentlyContinue
if ($proc) { Log "程序当前正在运行 PID=$($proc.Id) 已运行$([math]::Round(((Get-Date)-$proc.StartTime).TotalHours,2))小时 内存$([math]::Round($proc.WorkingSet64/1MB))MB" }
else { Log "程序当前未运行" }

Log "===== 取证完成：请把整个 [$outRoot] 文件夹拷回开发电脑 ====="
