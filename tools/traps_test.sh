#!/usr/bin/env bash
# App Stein traps test: separate headless processes (host + bots) over localhost, one scenario per trap.
# Usage: tools/traps_test.sh   (env: PORT=7804  LOGDIR=<dir>  MAXWAIT=70  ONLY=<scenario name>)
set -u
cd "$(dirname "$0")/.."
PORT="${PORT:-7804}"; MAXWAIT="${MAXWAIT:-70}"; ONLY="${ONLY:-}"
LOGDIR="${LOGDIR:-$(mktemp -d)}"; mkdir -p "$LOGDIR"
RC=0
echo "== build"
dotnet build AppStein.csproj -nologo -v q 2>&1 | tail -15 > "$LOGDIR/build.txt"
grep -q "0 Error(s)" "$LOGDIR/build.txt" || { cat "$LOGDIR/build.txt"; echo "TRAPS FAIL: build"; exit 1; }
godot --headless --path . --import > "$LOGDIR/import.log" 2>&1

# scenario NAME "host args" alice bob carol minwait ; requirements in REQ ("proc:regex"), forbidden in FORB
scenario() {
  local name="$1" hostargs="$2" alice="$3" bob="$4" carol="$5" minwait="$6"
  if [ -n "$ONLY" ] && [ "$ONLY" != "$name" ]; then return; fi
  local d="$LOGDIR/$name"; mkdir -p "$d"; rm -f "$d"/*.log
  local n=3; [ -n "$carol" ] && n=4
  local pids=()
  godot --headless --path . -- --host --name=Host --port="$PORT" --autostart=$n --preset=debug --bot=idle --notraps --trapdebug $hostargs > "$d/host.log" 2>&1 &
  pids+=($!)
  sleep 4
  godot --headless --path . -- --join=127.0.0.1 --port="$PORT" --name=Alice --bot="$alice" > "$d/alice.log" 2>&1 &
  pids+=($!)
  godot --headless --path . -- --join=127.0.0.1 --port="$PORT" --name=Bob --bot="$bob" > "$d/bob.log" 2>&1 &
  pids+=($!)
  if [ -n "$carol" ]; then
    godot --headless --path . -- --join=127.0.0.1 --port="$PORT" --name=Carol --bot="$carol" > "$d/carol.log" 2>&1 &
    pids+=($!)
  fi
  local start=$(date +%s) ok=0
  while :; do
    local el=$(( $(date +%s) - start ))
    if [ $el -ge "$minwait" ]; then
      ok=1
      for r in "${REQ[@]}"; do grep -qE "${r#*:}" "$d/${r%%:*}.log" 2>/dev/null || ok=0; done
      [ $ok -eq 1 ] && break
    fi
    [ $el -gt "$MAXWAIT" ] && break
    sleep 2
  done
  kill "${pids[@]}" 2>/dev/null; wait "${pids[@]}" 2>/dev/null
  local bad=0
  for r in "${REQ[@]}"; do
    if grep -qE "${r#*:}" "$d/${r%%:*}.log" 2>/dev/null; then echo "PASS: $name ${r%%:*}~${r#*:}"; else echo "FAIL: $name missing ${r%%:*}~${r#*:}"; bad=1; fi
  done
  for r in "${FORB[@]}"; do
    if grep -qE "${r#*:}" "$d/${r%%:*}.log" 2>/dev/null; then echo "FAIL: $name forbidden ${r%%:*}~${r#*:}"; bad=1; fi
  done
  if grep -hE "^FAIL:|ERROR|SCRIPT ERROR|Unhandled exception|Exception:" "$d"/*.log | grep -vE "Unreferenced static string|leaked at exit|Pages in use|non-existing signal|propagate_notification|meta. values|connected_peers" | head -5 | grep -q .; then
    echo "FAIL: $name errors in logs:"; grep -hE "^FAIL:|ERROR|SCRIPT ERROR|Unhandled exception|Exception:" "$d"/*.log | grep -vE "Unreferenced static string|leaked at exit|Pages in use|non-existing signal|propagate_notification|meta. values|connected_peers" | head -5; bad=1
  fi
  if [ $bad -eq 0 ]; then echo "PASS: $name"; else echo "FAIL: $name (logs in $d)"; RC=1; fi
  FORB=()
}
FORB=()
S='TRAPSTATE kind=trap_'

REQ=("host:TRAPDMG kind=spikes victim=Alice cause=spikes" "bob:${S}spikes .*state=click" "bob:${S}spikes .*state=up")
scenario spikes "--spawntrap=spikes@near" trapstand idle "" 0

REQ=("host:TRAPNOISE" "host:TRAPDMG kind=rockfall victim=Alice cause=crush" "host:^PASS: rockfall_ore" "bob:${S}rockfall .*state=rumble" "bob:${S}rockfall .*state=rubble")
scenario rockfall_noise "--spawntrap=rockfall@near --trapfast --trapnoise=14" trapstand idle "" 0

REQ=("host:TRAPDMG kind=gas victim=Alice cause=gas" "bob:${S}gas .*state=warn" "bob:${S}gas .*state=vent")
scenario gas "--spawntrap=gas@near --trapfast" trapstand idle "" 0

REQ=("host:TRAPDMG kind=darts victim=Alice cause=darts" "bob:${S}darts .*state=fire" "bob:${S}darts .*state=volley")
scenario darts "--spawntrap=darts@near" trapwalk idle "" 0

REQ=("host:TRAPSTATE kind=trap_trapdoor .*state=open" "bob:${S}trapdoor .*state=creak" "bob:${S}trapdoor .*state=open" "alice:^PASS: trapdoor_fell" "alice:^PASS: trap_hurt")
scenario trapdoor "--spawntrap=trapdoor@near" trapstand idle "" 0

REQ=("host:^PASS: lever_pulled" "bob:^PASS: gate_blocked" "bob:^PASS: gate_reopened" "carol:${S}gate .*state=down" "carol:${S}gate .*state=up")
scenario lever_gate "--spawntrap=lever_gate@near" trapleverpull trapgate idle 0

REQ=("host:^PASS: lever_pulled" "host:TRAPKILL kind=rockfall victim=Bob cause=crush killer=Alice" "carol:${S}rockfall .*state=rubble")
scenario lever_collapse "--spawntrap=lever_collapse@near" trapleverpull trapstand idle 0

# nothing triggers inside the camp: traps are live (state changes) but nobody is hurt
REQ=("host:${S}gas .*state=vent" "host:${S}rockfall .*state=rubble" "host:${S}spikes .*state=armed")
FORB=("host:TRAPDMG" "alice:^PASS: trap_hurt" "bob:^PASS: trap_hurt" "host:TRAPSTATE kind=trap_(spikes|trapdoor|darts) .*state=(click|creak|fire)")
scenario camp "--spawntrap=spikes@camp,gas@camp,trapdoor@camp,darts@camp,rockfall@camp --trapfast --trapnoise=6" idle idle "" 30

if [ $RC -eq 0 ]; then echo "TRAPS PASS"; else echo "TRAPS FAILED (see $LOGDIR)"; fi
exit $RC
