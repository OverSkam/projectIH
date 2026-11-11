#!/usr/bin/env bash
# App Stein smoke test: builds, then runs 1 headless host (idle bot) + 2 headless bot clients on localhost
# with the debug preset, and checks PASS:/FAIL: lines plus runtime errors in the logs.
# Usage: tools/smoke_test.sh        (env: PORT=7777  LOGDIR=<dir>  TIMEOUT=240)
set -u
cd "$(dirname "$0")/.."
PORT="${PORT:-7777}"
TIMEOUT="${TIMEOUT:-240}"
LOGDIR="${LOGDIR:-$(mktemp -d)}"
mkdir -p "$LOGDIR"
rm -f "$LOGDIR"/*.log
RC=0
fail() { echo "SMOKE FAIL: $*"; RC=1; }

echo "== build"
dotnet build AppStein.csproj -nologo -v q 2>&1 | tail -15 > "$LOGDIR/build.txt"
if ! grep -q "0 Error(s)" "$LOGDIR/build.txt"; then cat "$LOGDIR/build.txt"; echo "SMOKE FAIL: build"; exit 1; fi
echo "== import"
godot --headless --path . --import > "$LOGDIR/import.log" 2>&1

echo "== run (logs in $LOGDIR)"
PIDS=()
godot --headless --path . -- --host --name=Host --port="$PORT" --autostart=3 --preset=debug --bot=idle --nomonsters --notraps --timeout="$TIMEOUT" > "$LOGDIR/host.log" 2>&1 &
PIDS+=($!)
sleep 4
godot --headless --path . -- --join=127.0.0.1 --port="$PORT" --name=Alice --bot=miner --timeout="$TIMEOUT" > "$LOGDIR/alice.log" 2>&1 &
PIDS+=($!)
godot --headless --path . -- --join=127.0.0.1 --port="$PORT" --name=Bob --bot=killer --timeout="$TIMEOUT" > "$LOGDIR/bob.log" 2>&1 &
PIDS+=($!)

start=$(date +%s)
while :; do
  alive=0
  for p in "${PIDS[@]}"; do kill -0 "$p" 2>/dev/null && alive=1; done
  [ $alive -eq 0 ] && break
  if [ $(( $(date +%s) - start )) -gt "$TIMEOUT" ]; then fail "timeout, killing processes"; kill "${PIDS[@]}" 2>/dev/null; break; fi
  sleep 1
done
# exit codes
i=0
for n in host alice bob; do
  wait "${PIDS[$i]}" 2>/dev/null; code=$?
  [ $code -ne 0 ] && fail "$n exited with code $code"
  i=$((i+1))
done

ALL=$(cat "$LOGDIR"/host.log "$LOGDIR"/alice.log "$LOGDIR"/bob.log)
echo "== results"
for want in connected peers_connected round_started ore_mined ore_picked_up deposit_server deposit_credited \
            no_foreign_state damage_applied death_bag_dropped ghost_after_death round_summary_shown \
            respawned_next_round match_end winner_correct; do
  if grep -q "^PASS: $want" <<<"$ALL"; then echo "PASS: $want"; else fail "missing PASS: $want"; fi
done
grep -h "^FAIL:" "$LOGDIR"/*.log && fail "FAIL lines present"
grep -hE "ERROR|SCRIPT ERROR|Unhandled exception|Exception:|Traceback" "$LOGDIR"/host.log "$LOGDIR"/alice.log "$LOGDIR"/bob.log && fail "runtime errors in logs"

# Scenario assertions
[ "$(grep -c '^PASS: round_started' "$LOGDIR/host.log")" -ge 2 ] || fail "host did not start 2 rounds"
[ "$(grep -c '^PASS: ghost_after_death' <<<"$ALL")" -ge 2 ] || fail "expected >=2 ghost transitions (collapse death + PvP death)"
# deposits credited only to Alice, and only Alice's client saw money
if grep '^PASS: deposit_server' "$LOGDIR/host.log" | grep -qv 'name=Alice'; then fail "deposit credited to someone other than Alice"; fi
grep -q '^PASS: deposit_credited' "$LOGDIR/alice.log" || fail "Alice never saw her deposit"
grep -q '^PASS: deposit_credited' "$LOGDIR/bob.log" && fail "Bob saw a money increase"
grep -q '^PASS: deposit_credited' "$LOGDIR/host.log" && fail "Host saw a money increase"
# Hidden information: no client ever logged another peer's inventory/money, and final balances match the server
grep -q 'OWNSTATE money=[1-9]' "$LOGDIR/bob.log" && fail "Bob's own state shows money he never earned"
grep -q 'victim=Alice' "$LOGDIR/host.log" || fail "Alice was never damaged"
grep -q '^WINNER=Alice' "$LOGDIR/host.log" || fail "winner is not Alice"
for n in Host Alice Bob; do
  lc=$(echo "$n" | tr 'A-Z' 'a-z')
  srv=$(grep "FINAL: peer=.* name=$n " "$LOGDIR/host.log" | sed 's/.*money=//')
  cli=$(grep "FINAL own_money=" "$LOGDIR/$lc.log" | sed 's/.*own_money=\([0-9-]*\).*/\1/')
  [ -n "$srv" ] && [ "$srv" = "$cli" ] || fail "final money mismatch for $n (server=$srv client=$cli)"
done

if [ $RC -eq 0 ]; then echo "SMOKE PASS"; else echo "SMOKE FAILED (see $LOGDIR)"; fi
exit $RC
