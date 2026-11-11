#!/usr/bin/env bash
# Monster tests: for each scenario runs a headless host (idle bot, scripted by --montest=NAME) plus bot client(s) as separate
# processes on localhost (debug preset, --nomonsters so only the scenario's monsters exist), and checks PASS lines.
# Usage: tools/monsters_test.sh   (env: PORT=7777 LOGDIR=<dir> TIMEOUT=120)
set -u
cd "$(dirname "$0")/.."
PORT="${PORT:-7777}"
TIMEOUT="${TIMEOUT:-120}"
LOGDIR="${LOGDIR:-$(mktemp -d)}"
mkdir -p "$LOGDIR"; rm -f "$LOGDIR"/*.log
RC=0
fail() { echo "MONSTERS FAIL: $*"; RC=1; }

echo "== build"
dotnet build AppStein.csproj -nologo -v q 2>&1 | tail -15 > "$LOGDIR/build.txt"
if ! grep -q "0 Error(s)" "$LOGDIR/build.txt"; then cat "$LOGDIR/build.txt"; echo "MONSTERS FAIL: build"; exit 1; fi
godot --headless --path . --import > "$LOGDIR/import.log" 2>&1

# run_scenario <name> <autostart> <host-pass-list> <alice-pass-list> <bob? 0|1> <alice extra args>
run_scenario() {
  local name="$1" autostart="$2" hostpass="$3" alicepass="$4" bob="$5"; shift 5
  local L="$LOGDIR/$name"; mkdir -p "$L"
  echo "== scenario $name"
  local PIDS=()
  godot --headless --path . -- --cave=greybox --host --name=Host --port="$PORT" --autostart="$autostart" --preset=debug --bot=idle --nomonsters --montest="$name" --timeout="$TIMEOUT" > "$L/host.log" 2>&1 &
  PIDS+=($!)
  sleep 4
  godot --headless --path . -- --cave=greybox --join=127.0.0.1 --port="$PORT" --name=Alice --bot=mon "$@" --timeout="$TIMEOUT" > "$L/alice.log" 2>&1 &
  PIDS+=($!)
  if [ "$bob" = "1" ]; then
    godot --headless --path . -- --cave=greybox --join=127.0.0.1 --port="$PORT" --name=Bob --bot=mon --monpos=40:-2 --monface=38:-2 --timeout="$TIMEOUT" > "$L/bob.log" 2>&1 &
    PIDS+=($!)
  fi
  local start=$(date +%s)
  while :; do
    local alive=0
    for p in "${PIDS[@]}"; do kill -0 "$p" 2>/dev/null && alive=1; done
    [ $alive -eq 0 ] && break
    if [ $(( $(date +%s) - start )) -gt "$TIMEOUT" ]; then fail "$name: timeout, killing"; kill "${PIDS[@]}" 2>/dev/null; break; fi
    sleep 1
  done
  local i=0
  for p in "${PIDS[@]}"; do wait "$p" 2>/dev/null; code=$?; [ $code -ne 0 ] && fail "$name: process $i exited with code $code"; i=$((i+1)); done
  for w in $hostpass; do
    if grep -q "^PASS: $w" "$L/host.log"; then echo "PASS: $name/$w"; else fail "$name: missing PASS $w (host)"; fi
  done
  for w in $alicepass; do
    if grep -q "^PASS: $w" "$L/alice.log"; then echo "PASS: $name/$w (client)"; else fail "$name: missing PASS $w (Alice client)"; fi
  done
  grep -h "^FAIL:" "$L"/*.log && fail "$name: FAIL lines present"
  grep -hE "ERROR|SCRIPT ERROR|Unhandled exception|Exception:" "$L"/*.log && fail "$name: runtime errors in logs"
}

run_scenario golem 2 \
  "alice_in_position golem_hears_noise golem_approaches golem_damages_miner golem_front_armoured golem_back_hit_hurts golem_dies_from_behind golem_drops_loot montest_done" \
  "monster_replicated monster_moved_on_client miner_hurt_seen" 0 --monpos=36:-2

run_scenario golemhit 2 \
  "alice_in_position golem_front_hit_registered golem_front_real_hits_barely_hurt golem_back_real_hit_hurts golem_killed_by_bot golem_loot_dropped montest_done" \
  "monster_replicated" 0 --monpos=36:-2 --monhit

run_scenario ember 2 \
  "alice_in_position ember_dies_in_water ember_drops_ruby ember_burns_miner ember_burn_damage_over_time ember_weak_to_melee ember_dies_chasing_into_water montest_done" \
  "monster_replicated miner_hurt_seen" 0 --monpos=36:-2 --monface=36:5

run_scenario wisp 3 \
  "bots_in_position wisp_attracted_to_noise wisp_damages_miner wisp_gasimmune_safe wisp_unkillable_by_melee wisp_dispersed_harmless wisp_reforms montest_done" \
  "monster_replicated miner_hurt_seen" 1 --monpos=36:-2 --monface=38:-2

run_scenario dragon 2 \
  "alice_in_lair dragon_hoard_spawned dragon_sleeps_when_quiet dragon_stage dragon_woke dragon_wakes_from_noise dragon_breath_damages_miner dragon_stays_in_lair dragon_unkillable montest_done" \
  "monster_replicated miner_hurt_seen" 0 --monpos=36:36

if [ $RC -eq 0 ]; then echo "MONSTERS PASS"; else echo "MONSTERS FAILED (see $LOGDIR)"; fi
exit $RC
