$src = [System.IO.File]::ReadAllText('E:\temp_sdk_fix.cs', [System.Text.Encoding]::UTF8)

# Find and fix the garbled strings in the Open() method
# Line 459: garbled "鎵撳紑"
# Pattern: dhEventInfo("  ...  ");
$lines = $src -split "`r`n"
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'dhEventInfo\(".*相机' -or $lines[$i] -match 'dhEventInfo\(".*' -and $lines[$i] -match 'GrabLoop') {
        if ($lines[$i] -match 'opened|相机已打开|鎵撳紑|宸叉墦') {
            $lines[$i] = $lines[$i] -replace 'dhEventInfo\(".*"\)', 'dhEventInfo("Camera opened.")'
        }
        if ($lines[$i] -match 'started|已开启|宸插紑|娴佺爜宸') {
            $lines[$i] = $lines[$i] -replace 'dhEventInfo\(".*"\)', 'dhEventInfo("GrabLoopThread started.")'
        }
    }
    if ($lines[$i] -match 'dhEventError\(".*' -and $lines[$i] -match 'failed|失败|け璐') {
        $lines[$i] = $lines[$i] -replace 'dhEventError\(".*"\)', 'dhEventError("GrabLoopThread failed.")'
    }
}

$src = [string]::Join("`r`n", $lines)
[System.IO.File]::WriteAllText('E:\temp_sdk_fix2.cs', $src, [System.Text.Encoding]::UTF8)
Write-Host "Fixed garbled strings"
