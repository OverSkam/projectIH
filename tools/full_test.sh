#!/usr/bin/env bash
# App Stein full-game soak test: 1 headless host + 3 explorer bots in generated (ModularCave) caves with monsters AND traps,
# (all four miners, host included, are explorer bots)
# v2.1 rules (bottomless sack, burden, tribute rings), short rounds (preset soak: 105 s rounds; 4 miners = 3 rounds, the match must run to a winner). The explorer bots collect stones, walk home early enough for their burden
# and stand on their ring inside the Hall when the timer ends; they also keep their lantern oil topped up.
# Usage: tools/full_test.sh   (env: PORT=7777  LOGDIR=<dir>  TIMEOUT=600  SEED=11)
set -u
cd "$(dirname "$0")/.."
PORT="${PORT:-7777}"; TIMEOUT="${TIMEOUT:-900}"; SEED="${SEED:-11}"; ROUNDS=3
LOGDIR="${LOGDIR:-$(mktemp -d)}"; mkdir -p "$LOGDIR"; rm -f "$LOGDIR"/*.log
RC=0
fail() { echo "FULL FAIL: $*"; RC=1; }
echo "== build"
dotnet build AppStein.csproj -nologo -v q 2>&1 | tail -15 > "$LOGDIR/build.txt"
grep -q "0 Error(s)" "$LOGDIR/build.txt" || { cat "$LOGDIR/build.txt"; echo "FULL FAIL: build"; exit 1; }
godot --headless --path . --import > "$LOGDIR/import.log" 2>&1
echo "== run seedbase=$SEED rounds<=$ROUNDS (logs in $LOGDIR)"
COMMON="--preset=soak --seedbase=$SEED --soak --timeout=$TIMEOUT"
HOSTARGS="--give=Alice=stein:ethereal --giveeach"   # Alice can phase through thin walls: bots reach the nook chests
PIDS=()
godot --headless --path . -- --host --name=Host --port="$PORT" --autostart=4 --bot=explorer $COMMON $HOSTARGS > "$LOGDIR/host.log" 2>&1 & PIDS+=($!)
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
[ "$(grep -c '^ROUND: [0-9]* start' "$H")" -ge 2 ] || fail "host started fewer than 2 rounds"
[ "$(grep -c '^PASS: soak_audit' "$H")" -ge 2 ] || fail "soak audit did not run every round"
[ "$(grep -c '^PASS: miner_eliminated' "$H")" -ge 1 ] || fail "nobody was eliminated by the Scale"
[ "$(grep -c '^PASS: weigh_reveal' "$H")" -ge 4 ] || fail "weighings did not reveal enough piles"
for n in alice bob carol; do grep -q '^PASS: weigh_verdict' "$LOGDIR/$n.log" || fail "$n never saw a weighing verdict"; done
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
need stones_counted_in_hall '^PASS: tribute_counted .*stones=[1-9].*in_hall=True'
need bots_home '^PASS: explorer_home'
need sacks_weighed '^PASS: palm_number'
if grep -qh '^PASS: explorer_lantern_toggle' "$LOGDIR"/alice.log "$LOGDIR"/bob.log "$LOGDIR"/carol.log "$H"; then echo "PASS: lantern_managed"; else fail "missing: bots never switched their lantern"; fi
if grep -qh 'oil_refilled' "$H"; then echo "PASS: oil_refilled"; else echo "note: no bot refilled at an oil drum this run"; fi
need chest_opened '^PASS: chest_opened'
if [ "$(osum trap_lever)" -gt 0 ]; then need lever_pulled '^PASS: lever_pulled'; fi
need monster_moved 'SOAKMON kind=[a-z]+ id=[0-9]+ dist=([1-9][0-9]|[0-9]{3,}) '
need monster_hit_miner 'SOAKDMG cause=(monster|fire) '
grep -q '^PASS: monster_died' "$H" && fail "a monster died (monsters are unkillable)"
if [ "$trp" -gt 0 ]; then need trap_hit_miner 'TRAPDMG kind='; fi
enc_bad=$(grep -h '^SOAKENC' "$H" | grep -c 'first=-1')
echo "encounters: $(grep -h '^SOAKENC' "$H" | tr '\n' ' ')"
[ "$(grep -c '^SOAKENC' "$H")" -ge 1 ] || fail "no SOAKENC lines (soak director did not report)"
[ "$enc_bad" -eq 0 ] || fail "a round passed without any miner having a monster within 15 m"
echo "stuck monster events: $(grep -c SOAKSTUCK "$H")"
[ "$(grep -c SOAKSTUCK "$H")" -le 6 ] || fail "too many stuck monsters"
grep -hE "^PASS: (shrine_taken|shrine_trial_started|chest_opened|lever_pulled|monster_died)" "$H" | cut -d' ' -f2 | sort | uniq -c | tr '\n' ' '; echo
grep -hE "^(WEIGH|ROUND)" "$H" | head -40
grep -E "^(ROUND|WINNER)" "$H"
if [ $RC -eq 0 ]; then echo "FULL PASS"; else echo "FULL FAILED (see $LOGDIR)"; fi
exit $RC
