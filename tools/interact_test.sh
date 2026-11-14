#!/usr/bin/env bash
# Player-to-player interaction test (v2.3): a scripted headless host (Host) plus bot clients Alice and Bob as separate processes on localhost (see src/Social/InteractTest.cs).
#  front shove moves the victim and spills nothing; back shove spills exactly the top stone as a pickup a third miner can take; shove cooldown; shove into a spike plate and off a ledge hurts;
#  pickpocket moves exactly one stone (victim told 1.5 s late, nothing leaks to others); pickpocket fails when the victim turns (loud, 6 s cooldown); coarse sack step public, exact values not;
#  a downed miner: no pickpocket, hold E revives, C robs (most valuable first), hold RMB drags; shove disabled in the weighing; shove at the fuse logged.
# Usage: tools/interact_test.sh   (env: PORT=7931 LOGDIR=<dir> TIMEOUT=240)
set -u
cd "$(dirname "$0")/.."
PORT="${PORT:-7931}"
TIMEOUT="${TIMEOUT:-240}"
LOGDIR="${LOGDIR:-$(mktemp -d)}"
mkdir -p "$LOGDIR"; rm -f "${LOGDIR:?}"/*.log
RC=0
fail() { echo "INTERACT FAIL: $*"; RC=1; }

if [ -z "${SKIP_BUILD:-}" ]; then
  dotnet build AppStein.csproj -nologo -v q 2>&1 | tail -15 > "$LOGDIR/build.txt"
  grep -q "0 Error(s)" "$LOGDIR/build.txt" || { cat "$LOGDIR/build.txt"; echo "INTERACT FAIL: build"; exit 1; }
  godot --headless --path . --import > "$LOGDIR/import.log" 2>&1
fi

PIDS=()
godot --headless --path . -- --host --name=Host --port="$PORT" --autostart=3 --preset=debug --roundsecs=75 --itest=interact --give=stein:regen --bot=i_host --nomonsters --notraps --timeout="$TIMEOUT" > "$LOGDIR/host.log" 2>&1 &
PIDS+=($!)
sleep 4
for c in Alice Bob; do
  lc=$(echo "$c" | tr 'A-Z' 'a-z')
  godot --headless --path . -- --join=127.0.0.1 --port="$PORT" --name="$c" --bot=i_"$lc" --timeout="$TIMEOUT" > "$LOGDIR/$lc.log" 2>&1 &
  PIDS+=($!)
done
start=$(date +%s)
while :; do
  alive=0
  for p in "${PIDS[@]}"; do kill -0 "$p" 2>/dev/null && alive=1; done
  [ $alive -eq 0 ] && break
  if [ $(( $(date +%s) - start )) -gt "$TIMEOUT" ]; then fail "timeout"; kill "${PIDS[@]}" 2>/dev/null; break; fi
  sleep 1
done
ALL=$(cat "$LOGDIR"/*.log)
grep -h "^FAIL:" "$LOGDIR"/*.log && fail "FAIL lines"
grep -hE "ERROR|SCRIPT ERROR|Unhandled exception|Exception:" "$LOGDIR"/*.log | grep -vE "leaked at exit|Pages in use|RID allocations|Unreferenced static string" | grep . && fail "runtime errors"
want() { if grep -q "^PASS: $1" <<<"$ALL"; then echo "PASS: $1"; else fail "missing PASS: $1"; fi; }

for n in front_shove_moved front_shove_no_spill back_shove_spills_top_stone back_shove_moved spilled_stone_taken_by_third_miner shove_cooldown spike_plate_spawned shove_into_trap_hurts shove_off_ledge_hurts \
         pickpocket_moved_one_stone pickpocket_success pickpocket_victim_notified pickpocket_failed_loud pickpocket_cooldown_blocks sack_steps_server sack_step_seen sack_exact_not_public \
         downed_cannot_be_pickpocketed revive_works revive_done rob_takes_most_valuable_first drag_moves_downed_miner drag_started drag_ended \
         shove_disabled_in_weighing shove_at_fuse no_foreign_state; do want $n; done
grep -q "^TRAPDMG kind=spikes victim=Bob" "$LOGDIR/host.log" || fail "the spike plate did not hurt Bob"
grep -q "^PASS: shoved_received" "$LOGDIR/bob.log" || fail "Bob's client never received a shove"
# the victim of the pickpocket hears about it late, and ONLY the victim does
grep -q "^PASS: stolen_notice" "$LOGDIR/bob.log" || fail "Bob did not get the stolen notice"
grep -q "^PASS: stolen_notice" "$LOGDIR/alice.log" && fail "the thief received a stolen notice"
d=$(grep "^PASS: pickpocket_victim_notified victim=Bob" "$LOGDIR/host.log" | head -1 | sed -E 's/.*delay=([0-9.]+).*/\1/')
awk -v d="${d:-0}" 'BEGIN{exit !(d>=1.45 && d<=1.9)}' || fail "victim notified after ${d}s (want 1.5 s)"
# the downed overlay data reaches only the downed miner (Host)
grep -q "^PASS: downed_actor_seen mode=1 by=Alice" "$LOGDIR/host.log" || fail "downed Host did not see Alice helping"
grep -q "^PASS: downed_actor_seen mode=2 by=Bob" "$LOGDIR/host.log" || fail "downed Host did not see Bob robbing"
grep -q "^PASS: downed_actor_seen mode=3 by=Alice" "$LOGDIR/host.log" || fail "downed Host did not see Alice dragging"
grep -q "^PASS: dragged_client by=Alice" "$LOGDIR/host.log" || fail "Host's client was not dragged"
grep -q "^PASS: downed_actor_seen" "$LOGDIR/alice.log" "$LOGDIR/bob.log" && fail "a standing miner saw the downed overlay data"
grep -q "^PASS: shove_at_fuse victim=Bob by=Alice" "$LOGDIR/host.log" || fail "the last-second shove on Bob was not logged"
if [ $RC -eq 0 ]; then echo "INTERACT PASS"; else echo "INTERACT FAILED (see $LOGDIR)"; fi
exit $RC
