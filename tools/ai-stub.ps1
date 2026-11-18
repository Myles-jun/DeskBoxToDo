param(
    [int]$Port = 8765,
    [string]$OutDir = 'C:\Users\Myles\Documents\deepseek-harness\default-workspace\DeskBoxToDo'
)

# Minimal OpenAI-compatible stub over a raw TCP listener (no URL ACL needed).
$logPath = Join-Path $OutDir '_stub.log'
Add-Content -Path $logPath -Value ("start " + (Get-Date).ToString('HH:mm:ss'))

$listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $Port)
try {
    $listener.Start()
} catch {
    Add-Content -Path $logPath -Value ("listen failed: " + $_.Exception.Message)
    exit 1
}

$content = '{"t":"AI 解析标题","s":["AI 子项 1","AI 子项 2"],"d":"2026-11-21T08:00:00+08:00","i":true}'
$escaped = $content.Replace('\', '\\').Replace('"', '\"')
$payload = '{"id":"stub","object":"chat.completion","choices":[{"index":0,"message":{"role":"assistant","content":"' + $escaped + '"},"finish_reason":"stop"}],"usage":{"prompt_tokens":111,"completion_tokens":22,"total_tokens":133}}'

$deadline = (Get-Date).AddMinutes(20)
while ((Get-Date) -lt $deadline) {
    if (-not $listener.Pending()) {
        Start-Sleep -Milliseconds 100
        continue
    }

    $client = $listener.AcceptTcpClient()
    $stream = $client.GetStream()
    $stream.ReadTimeout = 3000

    $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::ASCII, $false, 1024, $true)
    $raw = New-Object System.Text.StringBuilder
    $buffer = New-Object char[] 4096

    # Read headers
    while ($true) {
        $read = $reader.Read($buffer, 0, $buffer.Length)
        if ($read -le 0) { break }
        [void]$raw.Append((New-Object string ($buffer, 0, $read)))
        if ($raw.ToString() -match "\r\n\r\n") { break }
    }

    $headers = $raw.ToString()
    $length = 0
    if ($headers -match '(?im)^Content-Length:\s*(\d+)') { $length = [int]$Matches[1] }

    $bodyLength = 0
    $headerEnd = $headers.IndexOf("`r`n`r`n")
    if ($headerEnd -ge 0) { $bodyLength = $headers.Length - ($headerEnd + 4) }

    $remaining = $length - $bodyLength
    while ($remaining -gt 0) {
        $read = $reader.Read($buffer, 0, [Math]::Min($buffer.Length, $remaining))
        if ($read -le 0) { break }
        $remaining -= $read
    }

    Add-Content -Path $logPath -Value ((Get-Date).ToString('HH:mm:ss') + " request " + $length + " bytes")

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $head = "HTTP/1.1 200 OK`r`nContent-Type: application/json; charset=utf-8`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n"
    $headBytes = [System.Text.Encoding]::ASCII.GetBytes($head)
    $stream.Write($headBytes, 0, $headBytes.Length)
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
    $client.Close()
}

$listener.Stop()
