#!/usr/bin/env bash
# Drive the Windows Unity build machine over SSH.
# Config (gitignored): tools/win.env  →  WIN_HOST=USER@host  [WIN_PROJ, WIN_CI, WIN_UNITY, WIN_TARGET]
#
#   tools/win.sh init                 one-time: turn WIN_PROJ into a git checkout (keeps Library/), clone WIN_CI
#   tools/win.sh sync                 git fetch + reset --hard origin/main on both checkouts, print HEAD
#   tools/win.sh batch <Class.Method> Unity -batchmode -quit -executeMethod on WIN_CI (short names get ObjectAR.EditorTools.)
#   tools/win.sh compile              open+quit WIN_CI in batchmode, report compile errors
#   tools/win.sh test                 EditMode tests on WIN_CI, print summary + failures
#   tools/win.sh apk [dest]           BuildScript.BuildAndroid → Builds/ObjectAR.apk (optionally scp to dest)
#   tools/win.sh log [n]              tail of the last batch log (default 80)
#   tools/win.sh pull-meta            copy new/changed files under Assets/ ProjectSettings/ Packages/ from WIN_CI to this repo
#   tools/win.sh editor-log [n]       tail of the user's interactive Editor.log
#   tools/win.sh trigger <cmd>        write ObjectAR.trigger (setup|scene|refresh) into WIN_PROJ (AutoRefresh runs it)
#   tools/win.sh ps                   run a PowerShell script from stdin on the Windows host
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(dirname "$DIR")"
[[ -f "$DIR/win.env" ]] || { echo "missing $DIR/win.env (WIN_HOST=USER@host)" >&2; exit 2; }
# shellcheck disable=SC1091
source "$DIR/win.env"
: "${WIN_HOST:?WIN_HOST not set in win.env}"
WIN_PROJ="${WIN_PROJ:-D:\\Dev\\UnityProject\\Test_AR\\TT_TGOSHI}"
WIN_CI="${WIN_CI:-D:\\Dev\\UnityProject\\Test_AR\\TT_TGOSHI_ci}"
WIN_UNITY="${WIN_UNITY:-C:\\Program Files\\Unity\\Hub\\Editor\\2022.3.17f1\\Editor\\Unity.exe}"
WIN_TARGET="${WIN_TARGET:-Android}"
REMOTE_URL="${REMOTE_URL:-$(git -C "$REPO" remote get-url origin)}"

SSH_OPTS=(-o BatchMode=yes -o ConnectTimeout=15 -o ServerAliveInterval=30 -o ServerAliveCountMax=6)

# Run a PowerShell script (stdin) remotely; exit code propagates.
remote_ps() {
  {
    cat <<EOF
\$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
\$OutputEncoding = [Text.Encoding]::UTF8
\$PROJ = '$WIN_PROJ'
\$CI = '$WIN_CI'
\$UNITY = '$WIN_UNITY'
\$TARGET = '$WIN_TARGET'
\$REMOTE = '$REMOTE_URL'
\$LOGDIR = Join-Path \$CI 'Logs'
New-Item -ItemType Directory -Force -Path \$LOGDIR | Out-Null
function Show-LogSummary([string]\$log, [int]\$n = 60) {
  if (-not (Test-Path \$log)) { Write-Output "(no log at \$log)"; return }
  Write-Output "----- tail \$n of \$log -----"
  Get-Content \$log -Tail \$n -Encoding UTF8
  \$hits = Select-String -Path \$log -Encoding UTF8 -Pattern 'error CS\d+|Exception|\[ObjectAR\].*(FAILED|Error)|BuildFailedException|Compilation failed' |
          Select-Object -ExpandProperty Line | Select-Object -Unique -First 80
  if (\$hits) { Write-Output "----- errors / exceptions -----"; \$hits }
}
function Invoke-Unity([string]\$name, [string[]]\$unityArgs) {
  \$log = Join-Path \$LOGDIR "ci-\$name.log"
  Set-Content -Path (Join-Path \$LOGDIR 'ci-last.txt') -Value \$log
  if (Test-Path \$log) { Remove-Item \$log -Force }
  \$all = @('-batchmode','-nographics','-projectPath', "\`"\$CI\`"", '-buildTarget', \$TARGET, '-logFile', "\`"\$log\`"") + \$unityArgs
  Write-Output ("Unity " + (\$all -join ' '))
  \$sw = [Diagnostics.Stopwatch]::StartNew()
  \$p = Start-Process -FilePath \$UNITY -ArgumentList \$all -PassThru -NoNewWindow
  \$null = \$p.Handle
  \$p.WaitForExit()
  \$global:UnityRC = \$p.ExitCode
  Write-Output ("Unity exit code {0} after {1:N0}s" -f \$global:UnityRC, \$sw.Elapsed.TotalSeconds)
  Show-LogSummary \$log 60
}
EOF
    cat
  } | ssh "${SSH_OPTS[@]}" "$WIN_HOST" 'powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$s=[Console]::In.ReadToEnd(); & ([scriptblock]::Create($s)); exit $LASTEXITCODE"'
}

cmd="${1:-help}"; shift || true
case "$cmd" in
  init)
    remote_ps <<'EOF'
# User checkout: convert in place (editor may be open — never touch Library/, never kill Unity)
if (-not (Test-Path (Join-Path $PROJ '.git'))) {
  git -C $PROJ init -q -b main
  git -C $PROJ remote add origin $REMOTE
  Write-Output "initialized git in $PROJ"
}
git -C $PROJ config core.autocrlf false
git -C $PROJ fetch -q origin
git -C $PROJ reset -q --hard origin/main
git -C $PROJ branch -q --set-upstream-to=origin/main main 2>$null
# CI checkout
if (-not (Test-Path (Join-Path $CI '.git'))) {
  git -c core.autocrlf=false clone -q $REMOTE $CI
  git -C $CI config core.autocrlf false
  Write-Output "cloned $CI"
}
foreach ($p in @($PROJ, $CI)) { Write-Output ("{0}: {1}" -f $p, (git -C $p log -1 --oneline)) }
EOF
    ;;
  sync)
    remote_ps <<'EOF'
$rc = 0
foreach ($p in @($PROJ, $CI)) {
  if (-not (Test-Path (Join-Path $p '.git'))) { Write-Output "$p is not a git checkout (run: tools/win.sh init)"; $rc = 1; continue }
  git -C $p fetch -q origin
  if ($LASTEXITCODE -ne 0) { $rc = 1; continue }
  git -C $p reset -q --hard origin/main
  if ($LASTEXITCODE -ne 0) { $rc = 1 }
  Write-Output ("{0}: {1}" -f $p, (git -C $p log -1 --oneline))
}
exit $rc
EOF
    ;;
  batch)
    m="${1:?usage: win.sh batch <Class.Method>}"
    [[ "$m" == *.*.* ]] || m="ObjectAR.EditorTools.$m"
    remote_ps <<EOF
Invoke-Unity '${m##*.}' @('-quit', '-executeMethod', '$m')
exit \$global:UnityRC
EOF
    ;;
  compile)
    remote_ps <<'EOF'
Invoke-Unity 'compile' @('-quit')
exit $global:UnityRC
EOF
    ;;
  test)
    remote_ps <<'EOF'
$xml = Join-Path $LOGDIR 'editmode-results.xml'
if (Test-Path $xml) { Remove-Item $xml -Force }
Invoke-Unity 'test' @('-runTests', '-testPlatform', 'EditMode', '-testResults', "`"$xml`"")
if (Test-Path $xml) {
  [xml]$r = Get-Content $xml -Encoding UTF8
  $run = $r.'test-run'
  Write-Output ("----- tests: total={0} passed={1} failed={2} skipped={3} result={4}" -f $run.total, $run.passed, $run.failed, $run.skipped, $run.result)
  foreach ($tc in $r.SelectNodes("//test-case[@result='Failed']")) {
    Write-Output ("FAIL {0}`n  {1}" -f $tc.fullname, ($tc.failure.message.'#cdata-section' -replace "`n", "`n  "))
  }
} else { Write-Output "no test results xml (compile error?)" }
exit $global:UnityRC
EOF
    ;;
  apk)
    dest="${1:-}"
    remote_ps <<'EOF'
Invoke-Unity 'apk' @('-quit', '-executeMethod', 'ObjectAR.EditorTools.BuildScript.BuildAndroid')
$apk = Join-Path $CI 'Builds\ObjectAR.apk'
if (Test-Path $apk) { Get-Item $apk | ForEach-Object { Write-Output ("APK: {0} {1:N1} MB {2}" -f $_.FullName, ($_.Length/1MB), $_.LastWriteTime) } }
exit $global:UnityRC
EOF
    if [[ -n "$dest" ]]; then
      scp "${SSH_OPTS[@]}" "$WIN_HOST:$(echo "$WIN_CI" | tr '\\' '/')/Builds/ObjectAR.apk" "$dest"
      echo "copied to $dest"
    fi
    ;;
  log)
    n="${1:-80}"
    remote_ps <<EOF
\$f = Join-Path \$LOGDIR 'ci-last.txt'
if (Test-Path \$f) { Show-LogSummary (Get-Content \$f | Select-Object -First 1) $n } else { Write-Output 'no batch log yet' }
EOF
    ;;
  editor-log)
    n="${1:-80}"
    remote_ps <<EOF
\$log = Join-Path \$env:LOCALAPPDATA 'Unity\Editor\Editor.log'
Get-Content \$log -Tail $n -Encoding UTF8
EOF
    ;;
  trigger)
    c="${1:?usage: win.sh trigger setup|scene|refresh}"
    [[ "$c" =~ ^(setup|scene|refresh)$ ]] || { echo "unknown trigger '$c'" >&2; exit 2; }
    remote_ps <<EOF
[IO.File]::WriteAllText((Join-Path \$PROJ 'ObjectAR.trigger'), '$c')
Write-Output "wrote ObjectAR.trigger ($c) — runs when the open editor ticks (AutoRefresh)"
EOF
    ;;
  pull-meta)
    list="$(remote_ps <<'EOF'
$lines = git -C $CI -c core.quotepath=false status --porcelain -uall -- Assets ProjectSettings Packages
$files = @()
foreach ($l in $lines) {
  $st = $l.Substring(0, 2); $path = $l.Substring(3).Trim('"')
  if ($path -match ' -> ') { $path = ($path -split ' -> ')[1] }
  if ($st -match 'D') { [Console]::Error.WriteLine("deleted on CI (not synced): $path"); continue }
  $files += $path
}
[IO.File]::WriteAllLines((Join-Path $LOGDIR 'pull-list.txt'), [string[]]$files)
$files
EOF
)"
    list="$(printf '%s\n' "$list" | tr -d '\r' | sed '/^$/d')"
    if [[ -z "$list" ]]; then echo "nothing new/changed on CI"; exit 0; fi
    stage="$(mktemp -d)"; trap 'rm -rf "$stage"' EXIT
    ci_fwd="$(echo "$WIN_CI" | tr '\\' '/')"
    ssh "${SSH_OPTS[@]}" "$WIN_HOST" "tar -cf - -C \"$ci_fwd\" -T \"$ci_fwd/Logs/pull-list.txt\"" | tar -xf - -C "$stage"
    copied=0; skipped=0
    while IFS= read -r f; do
      [[ -f "$stage/$f" ]] || continue
      if [[ -f "$REPO/$f" ]] && ! cmp -s "$stage/$f" "$REPO/$f" && git -C "$REPO" ls-files --error-unmatch -- "$f" >/dev/null 2>&1 \
         && ! git -C "$REPO" diff --quiet -- "$f"; then
        echo "SKIP (locally modified on Mac): $f"; skipped=$((skipped+1)); continue
      fi
      if [[ -f "$REPO/$f" ]] && cmp -s "$stage/$f" "$REPO/$f"; then continue; fi
      mkdir -p "$(dirname "$REPO/$f")"; cp "$stage/$f" "$REPO/$f"; echo "pulled: $f"; copied=$((copied+1))
    done <<<"$list"
    echo "pull-meta: $copied file(s) copied, $skipped skipped"
    ;;
  ps)
    remote_ps
    ;;
  help|-h|--help|*)
    sed -n '2,16p' "$0" | sed 's/^# \{0,1\}//'
    [[ "$cmd" =~ ^(help|-h|--help)$ ]] || exit 2
    ;;
esac
