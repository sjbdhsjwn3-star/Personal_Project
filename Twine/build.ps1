# Gallery_Escape.twee + .js + .css 를 템플릿 HTML에 넣어 Gallery_Escape.html 을 만든다.
# 사용: powershell -ExecutionPolicy Bypass -File build.ps1
$dir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = "C:\Users\MBC-501-04\Downloads\Sugarcube Screenplay UI Template.html"
$twee = Join-Path $dir "Gallery_Escape.twee"
$js   = Join-Path $dir "Gallery_Escape.js"
$css  = Join-Path $dir "Gallery_Escape.css"
$out  = Join-Path $dir "Gallery_Escape.html"
$ifidFile = Join-Path $dir ".ifid"
$utf8 = New-Object System.Text.UTF8Encoding $false

$html = [IO.File]::ReadAllText($src, $utf8)
function Esc($s) { return $s.Replace('&','&amp;').Replace('<','&lt;').Replace('>','&gt;').Replace('"','&quot;').Replace("'",'&#39;') }

# twee 파싱
$passages = [ordered]@{}; $tags = @{}; $name = $null; $buf = New-Object System.Text.StringBuilder
foreach ($line in [IO.File]::ReadAllLines($twee, $utf8)) {
  if ($line -match '^:: (.+?)(?: \[(.*)\])?\s*$') { if ($name) { $passages[$name] = $buf.ToString().Trim() }; $name = $Matches[1]; $tags[$name] = $Matches[2]; $buf = New-Object System.Text.StringBuilder }
  else { [void]$buf.AppendLine($line) } }
if ($name) { $passages[$name] = $buf.ToString().Trim() }

function Replace-Passage([string]$h, [string]$pname, [string]$text, [bool]$append) {
  $pat = '(<tw-passagedata [^>]*name="' + [regex]::Escape($pname) + '"[^>]*>)(.*?)(</tw-passagedata>)'
  $m = [regex]::Match($h, $pat, 'Singleline'); if (-not $m.Success) { throw "passage $pname not found" }
  $newBody = if ($append) { $m.Groups[2].Value + (Esc ("`n`n" + $text)) } else { Esc $text }
  return $h.Substring(0, $m.Index) + $m.Groups[1].Value + $newBody + $m.Groups[3].Value + $h.Substring($m.Index + $m.Length) }

$html = Replace-Passage $html "Start" $passages["Start"] $false
$html = Replace-Passage $html "StoryInit" $passages["StoryInit_Gallery"] $true
$html = Replace-Passage $html "Title" "그림 속 출구" $false
$html = Replace-Passage $html "Tagline" "폐관 후의 미술관, 세 개의 층, 하나의 비밀번호" $false

# 새 패시지
$n = 17; $x = 1200; $y = 0; $sb = New-Object System.Text.StringBuilder
foreach ($k in $passages.Keys) {
  if ($k -in @("Start", "StoryInit_Gallery")) { continue }
  $t = if ($tags[$k]) { $tags[$k] } else { "" }
  [void]$sb.Append('<tw-passagedata pid="' + $n + '" name="' + (Esc $k) + '" tags="' + (Esc $t) + '" position="' + $x + ',' + $y + '" size="100,100">' + (Esc $passages[$k]) + '</tw-passagedata>')
  $n++; $x += 150; if ($x -gt 2400) { $x = 1200; $y += 150 } }
$idx = $html.IndexOf('</tw-storydata>'); $html = $html.Substring(0, $idx) + $sb.ToString() + $html.Substring($idx)

# Story JavaScript / Stylesheet 뒤에 추가 (스크립트·스타일 안의 내용은 이스케이프하지 않음)
$jsText = [IO.File]::ReadAllText($js, $utf8); $cssText = [IO.File]::ReadAllText($css, $utf8)
$sIdx = $html.IndexOf('</script>', $html.IndexOf('id="twine-user-script"')); $html = $html.Substring(0, $sIdx) + "`n" + $jsText + $html.Substring($sIdx)
$cIdx = $html.IndexOf('</style>', $html.IndexOf('id="twine-user-stylesheet"')); $html = $html.Substring(0, $cIdx) + "`n" + $cssText + $html.Substring($cIdx)

# 이름·IFID (빌드할 때마다 같은 IFID 유지 → Twine에서 다시 불러오면 같은 이야기로 교체)
if (-not (Test-Path $ifidFile)) { [IO.File]::WriteAllText($ifidFile, [guid]::NewGuid().ToString().ToUpper()) }
$ifid = [IO.File]::ReadAllText($ifidFile).Trim()
$html = $html.Replace('name="Sugarcube Screenplay UI Template" startnode="9"', 'name="Gallery Escape - 그림 속 출구" startnode="9"')
$html = $html.Replace('ifid="e4fb779b-0396-4ea5-ae22-ce479e8ecb40"', 'ifid="' + $ifid + '"')
$html = $html.Replace('<title>Sugarcube Screenplay UI Template</title>', '<title>그림 속 출구</title>')

[IO.File]::WriteAllText($out, $html, $utf8)
"built: " + $out + " (" + [math]::Round((Get-Item $out).Length / 1KB) + " KB, passages " + ($n - 17) + " added)"
