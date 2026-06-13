# Markdown を HTML 経由で PDF に変換する簡易ツール
# 使い方: powershell -File md2pdf.ps1 入力.md 出力.pdf
param([string]$mdPath, [string]$pdfPath)

$lines = Get-Content $mdPath -Encoding UTF8
$sb = [System.Text.StringBuilder]::new()
$inTable = $false; $inList = $false; $headerDone = $false

function Inline([string]$s) {
    $s = $s -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;'
    $s = [regex]::Replace($s, '\*\*(.+?)\*\*', '<b>$1</b>')
    $s = [regex]::Replace($s, '`(.+?)`', '<code>$1</code>')
    return $s
}
function CloseList { if ($script:inList) { [void]$sb.AppendLine('</ul>'); $script:inList = $false } }
function CloseTable { if ($script:inTable) { [void]$sb.AppendLine('</table>'); $script:inTable = $false; $script:headerDone = $false } }

foreach ($raw in $lines) {
    $line = $raw.TrimEnd()
    if ($line -match '^\|(.+)\|\s*$') {
        CloseList
        $cells = ($line.Trim('|') -split '\|')
        if ($cells[0].Trim() -match '^[-:\s]+$') { continue } # 区切り行はスキップ
        if (-not $inTable) { [void]$sb.AppendLine('<table>'); $script:inTable = $true; $script:headerDone = $false }
        $tag = if (-not $headerDone) { 'th' } else { 'td' }
        $row = '<tr>'
        foreach ($c in $cells) { $row += "<$tag>$(Inline $c.Trim())</$tag>" }
        $row += '</tr>'
        [void]$sb.AppendLine($row)
        $script:headerDone = $true
        continue
    } else { CloseTable }

    if ($line -match '^#{1,4}\s+(.*)') {
        CloseList
        $level = ($line -replace '^(#+).*','$1').Length
        [void]$sb.AppendLine("<h$level>$(Inline ($line -replace '^#+\s+',''))</h$level>")
    }
    elseif ($line -match '^---\s*$') { CloseList; [void]$sb.AppendLine('<hr>') }
    elseif ($line -match '^>\s?(.*)') { CloseList; [void]$sb.AppendLine("<blockquote>$(Inline $matches[1])</blockquote>") }
    elseif ($line -match '^[-*]\s+(.*)') {
        if (-not $inList) { [void]$sb.AppendLine('<ul>'); $script:inList = $true }
        [void]$sb.AppendLine("<li>$(Inline $matches[1])</li>")
    }
    elseif ($line -eq '') { CloseList; [void]$sb.AppendLine('') }
    else { CloseList; [void]$sb.AppendLine("<p>$(Inline $line)</p>") }
}
CloseList; CloseTable

$style = @'
<style>
body{font-family:"Yu Gothic UI","Meiryo",sans-serif;line-height:1.75;color:#222;max-width:900px;margin:0 auto;padding:24px;}
h1{border-bottom:3px solid #8662c7;padding-bottom:8px;color:#4a2d7a;}
h2{border-left:6px solid #8662c7;padding-left:10px;margin-top:34px;color:#4a2d7a;page-break-after:avoid;}
h3{color:#6a4aa0;margin-top:20px;page-break-after:avoid;}
table{border-collapse:collapse;width:100%;margin:10px 0;font-size:12.5px;}
th,td{border:1px solid #bbb;padding:5px 9px;text-align:left;vertical-align:top;}
th{background:#efe8fa;}tr:nth-child(even){background:#f7f4fc;}
code{background:#f0ecf7;padding:1px 5px;border-radius:3px;font-size:12px;}
blockquote{background:#fdf6e3;border-left:5px solid #e0b84a;padding:6px 14px;margin:10px 0;}
hr{border:none;border-top:1px solid #ddd;margin:18px 0;}
ul{margin:6px 0;}
</style>
'@
$html = "<!DOCTYPE html><html lang=`"ja`"><head><meta charset=`"UTF-8`">$style</head><body>$($sb.ToString())</body></html>"
$htmlPath = [System.IO.Path]::ChangeExtension($pdfPath, '.html')
[System.IO.File]::WriteAllText($htmlPath, $html, (New-Object System.Text.UTF8Encoding $true))

$edge = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
& $edge --headless --disable-gpu --no-pdf-header-footer --virtual-time-budget=4000 --print-to-pdf="$pdfPath" "file:///$($htmlPath -replace '\\','/')" 2>$null
Start-Sleep -Seconds 2
if (Test-Path $pdfPath) { "OK: " + [math]::Round((Get-Item $pdfPath).Length/1KB) + " KB" } else { "FAILED" }
