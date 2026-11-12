#!/usr/bin/env bash
# App Stein full-game soak test: 1 headless host + 3 explorer bots in generated (ModularCave) caves with monsters AND traps,
# normal rules, short rounds (preset soak: 3 rounds, round 3 has the dragon lair).
# Usage: tools/full_test.sh   (env: PORT=7777  LOGDIR=<dir>  TIMEOUT=600  SEED=11  ROUNDS=3)
set -u
cd "$(dirname "$0")/.."
PORT="${PORT:-7777}"; TIMEOUT="${TIMEOUT:-600}"; SEED="${SEED:-11}"; ROUNDS="${ROUNDS:-3}"
LOGDIR="${LOGDIR:-$(mktemp -d)}"; mkdir -p "$LOGDIR"; rm -f "$LOGDIR"/*.log
RC=0
fail() { echo "FULL FAIL: $*"; RC=1; }
echo "== build"
dotnet build AppStein.csproj -nologo -v q 2>&1 | tail -15 > "$LOGDIR/build.txt"
grep -q "0 Error(s)" "$LOGDIR/build.txt" || { cat "$LOGDIR/build.txt"; echo "FULL FAIL: build"; exit 1; }
godot --headless --path . --import > "$LOGDIR/import.log" 2>&1
echo "== run seedbase=$SEED rounds=$ROUNDS (logs in $LOGDIR)"
COMMON="--preset=soak --rounds=$ROUNDS --seedbase=$SEED --soak --timeout=$TIMEOUT"
HOSTARGS="--give=Alice=stein:ethereal --giveeach"   # Alice can phase through thin walls: bots reach the nook chests
PIDS=()
godot --headless --path . -- --host --name=Host --port="$PORT" --autostart=4 --bot=idle $COMMON $HOSTARGS > "$LOGDIR/host.log" 2>&1 & PIDS+=($!)
sleep 4
godot --headless --path . -- --join=127.0.0.1 --port="$PORT" --name=Alice --bot=explorer $COMMON > "$LOGDIR/alice.log" 2>&1 & PIDS+=($!)
godot --headless --path . -- --join=127.0.0.1 --port="$PORT" --name=Bob --bot=explorer_brave $COMMON > "$LOGDIR/bob.log" 2>&1 & PIDS+=($!)
godot --headless --path . -- --join=127.0.0.1 --port="$PORT" --name=Carol --bot=explorer_brave $COMMON > "$LOGDIR/carol.log" 2>&1 & PIDS+=($!)
start=$(date +%s)
while :; do
  alive=0; for p in "${PIDS[@]}"; do kill -0 "$p" 2>/dev/null && alive=1; done
  [ $alive -eq 0 ] && break
  if [ $(( $(date +%s) - start )) -gt "$TIMEOUT" ]; then fail "timeout, killing processes"; kill "${PIDS[@]}" 2>/dev/null; break; fi
  sleep 1
done
i=0; for n in host alice bob carol; do wait "${PIDS[$i]}" 2>/dev/null; code=$?; [ $code -ne 0 ] && fail "$n exited with code $code"; i=$((i+1)); done

H="$LOGDIR/host.log"
IGN="csharp_lang|Leaked unsafe reference|Unreferenced static string|leaked at exit|Pages in use|non-existing signal|propagate_notification|meta. values|connected_peers"
echo "== results"
if grep -hE "^FAIL:|ERROR|SCRIPT ERROR|Unhandled exception|Exception:" "$LOGDIR"/host.log "$LOGDIR"/alice.log "$LOGDIR"/bob.log "$LOGDIR"/carol.log | grep -vE "$IGN" | head -20 | grep .; then fail "errors / FAIL lines in logs"; fi
[ "$(grep -c '^ROUND: [0-9]* start' "$H")" -ge "$ROUNDS" ] || fail "host did not start $ROUNDS rounds"
[ "$(grep -c '^PASS: soak_audit' "$H")" -ge "$ROUNDS" ] || fail "soak audit did not run every round"
grep -q '^WINNER=.\+' "$H" || fail "no match winner"
grep -q '^PASS: match_end' "$H" || fail "host never saw match_end"
for n in alice bob carol host; do grep -q '^PASS: no_foreign_state' "$LOGDIR/$n.log" || fail "$n: no_foreign_state missing"; done
# marker kinds -> objects, summed over the run
sum() { grep -h '^SOAKROUND' "$H" | grep -oE " $1=[0-9]+" | sed 's/.*=//' | awk '{s+=$1} END{print s+0}'; }
msum() { grep -h '^SOAKROUND' "$H" | grep -oE "markers\[[^]]*\]" | grep -oE "(^|[,\[])$1=[0-9]+" | sed 's/.*=//' | awk '{s+=$1} END{print s+0}'; }
osum() { grep -h '^SOAKROUND' "$H" | grep -oE "objects\[[^]]*\]" | grep -oE "(^|[,\[])$1=[0-9]+" | sed 's/.*=//' | awk '{s+=$1} END{print s+0}'; }
for pair in "vein:vein" "ore:pickup" "chest:chest" "shrine:shrine" "dragon:dragon" "lever:trap_lever"; do
  mk=${pair%%:*}; ob=${pair##*:}; m=$(msum $mk); o=$(osum $ob)
  echo "markers $mk=$m -> objects $ob=$o"
  if [ "$m" -gt 0 ] && [ "$o" -eq 0 ]; then fail "marker kind $mk produced no $ob objects"; fi
done
mon=$(sum monsters); trp=$(sum traps); echo "monsters=$mon traps=$trp levers=$(sum levers)"
if [ "$(msum monster)" -gt 0 ] && [ "$mon" -eq 0 ]; then fail "monster markers produced no monsters"; fi
if [ "$(msum trap)" -gt 0 ] && [ "$trp" -eq 0 ]; then fail "trap markers produced no traps"; fi
need() { if grep -qE "$2" "$H"; then echo "PASS: $1"; else fail "missing: $1 (/$2/)"; fi; }
need ore_mined '^PASS: ore_mined'
need ore_deposited '^PASS: deposit_server'
need chest_opened '^PASS: chest_opened'
if [ "$(osum trap_lever)" -gt 0 ]; then need lever_pulled '^PASS: lever_pulled'; fi
need monster_moved 'SOAKMON kind=[a-z]+ id=[0-9]+ dist=([1-9][0-9]|[0-9]{3,}) '
need monster_hit_miner 'SOAKDMG cause=(monster|fire) '
if [ "$trp" -gt 0 ]; then need trap_hit_miner 'TRAPDMG kind='; fi
echo "stuck monster events: $(grep -c SOAKSTUCK "$H")"
[ "$(grep -c SOAKSTUCK "$H")" -le 6 ] || fail "too many stuck monsters"
grep -hE "^PASS: (shrine_taken|shrine_trial_started|chest_opened|lever_pulled|monster_died)" "$H" | cut -d' ' -f2 | sort | uniq -c | tr '\n' ' '; echo
for n in Host Alice Bob Carol; do
  lc=$(echo "$n" | tr 'A-Z' 'a-z')
  srv=$(grep "FINAL: peer=.* name=$n " "$H" | sed 's/.*money=//')
  cli=$(grep "FINAL own_money=" "$LOGDIR/$lc.log" | sed 's/.*own_money=\([0-9-]*\).*/\1/')
  [ -n "$srv" ] && [ "$srv" = "$cli" ] || fail "final money mismatch for $n (server=$srv client=$cli)"
done
grep -E "^(ROUND|WINNER)" "$H"
if [ $RC -eq 0 ]; then echo "FULL PASS"; else echo "FULL FAILED (see $LOGDIR)"; fi
exit $RC
