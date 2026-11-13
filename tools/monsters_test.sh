#!/usr/bin/env bash
# Monster tests (monsters are unkillable: golem back crystal / dynamite / bear traps only stun): for each scenario runs a headless host (idle bot, scripted by --montest=NAME) plus bot client(s) as separate
# processes on localhost (debug preset, --nomonsters so only the scenario's monsters exist), and checks PASS lines.
# Usage: tools/monsters_test.sh   (env: PORT=7777 LOGDIR=<dir> TIMEOUT=120)
set -u
cd "$(dirname "$0")/.."
PORT="${PORT:-7777}"
TIMEOUT="${TIMEOUT:-120}"
LOGDIR="${LOGDIR:-$(mktemp -d)}"
mkdir -p "$LOGDIR"; rm -f "${LOGDIR:?}"/*.log
RC=0
CAVE="--cave=greybox"
fail() { echo "MONSTERS FAIL: $*"; RC=1; }

echo "== build"
dotnet build AppStein.csproj -nologo -v q 2>&1 | tail -15 > "$LOGDIR/build.txt"
if ! grep -q "0 Error(s)" "$LOGDIR/build.txt"; then cat "$LOGDIR/build.txt"; echo "MONSTERS FAIL: build"; exit 1; fi
godot --headless --path . --import > "$LOGDIR/import.log" 2>&1

# run_scenario <name> <autostart> <host-pass-list> <alice-pass-list> <bob? 0|1> <alice extra args>
run_scenario() {
  local name="$1" autostart="$2" hostpass="$3" alicepass="$4" bob="$5"; shift 5
  if [ -n "${ONLY:-}" ] && ! echo " $ONLY " | grep -q " $name "; then return; fi
  local L="$LOGDIR/$name"; mkdir -p "$L"
  echo "== scenario $name"
  local PIDS=()
  godot --headless --path . -- $CAVE --host --name=Host --port="$PORT" --autostart="$autostart" --preset=debug --bot=idle --nomonsters --montest="$name" ${HOSTEXTRA:-} --timeout="$TIMEOUT" > "$L/host.log" 2>&1 &
  PIDS+=($!)
  sleep 4
  godot --headless --path . -- $CAVE --join=127.0.0.1 --port="$PORT" --name=Alice --bot=mon "$@" --timeout="$TIMEOUT" > "$L/alice.log" 2>&1 &
  PIDS+=($!)
  if [ "$bob" = "1" ]; then
    godot --headless --path . -- $CAVE --join=127.0.0.1 --port="$PORT" --name=Bob --bot=mon ${BOBARGS:---monpos=40:-2 --monface=38:-2} --timeout="$TIMEOUT" > "$L/bob.log" 2>&1 &
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
    pat="$w"; case "$w" in *:*) pat="${w%%:*} kind=${w#*:}";; esac
    if grep -q "^PASS: $pat" "$L/alice.log"; then echo "PASS: $name/$w (client)"; else fail "$name: missing PASS $w (Alice client)"; fi
  done
  grep -h "^FAIL:" "$L"/*.log && fail "$name: FAIL lines present"
  grep -hE "ERROR|SCRIPT ERROR|Unhandled exception|Exception:" "$L"/*.log | grep -vE "Leaked unsafe|RID allocations|BUG: Unreferenced|Pages in use|leaked at exit" | grep -q . && fail "$name: runtime errors in logs"
}

run_scenario golem 2 \
  "alice_in_position golem_hears_noise golem_approaches golem_damages_miner golem_front_hit_ignored golem_stunned_by_back_hit golem_stunned_stays_put golem_recovers_after_stun golem_unkillable_no_loot golem_stunned_by_blast golem_blast_stun_ends golem_stunned_by_beartrap montest_done" \
  "monster_replicated monster_moved_on_client miner_hurt_seen" 0 --monpos=36:-2

run_scenario golemhit 2 \
  "alice_in_position golem_front_real_hits_do_nothing golem_back_real_hit_stuns golem_survives_no_loot montest_done" \
  "monster_replicated" 0 --monpos=36:-2 --monhit

run_scenario ember 2 \
  "alice_in_position ember_doused_in_water ember_burns_miner ember_burn_damage_over_time ember_unkillable_by_melee ember_doused_chasing_into_water montest_done" \
  "monster_replicated miner_hurt_seen" 0 --monpos=36:-2 --monface=36:5

run_scenario wisp 3 \
  "bots_in_position wisp_attracted_to_noise wisp_damages_miner wisp_gasimmune_safe wisp_unkillable_by_melee wisp_dispersed_harmless wisp_reforms montest_done" \
  "monster_replicated miner_hurt_seen" 1 --monpos=36:-2 --monface=38:-2

run_scenario dragon 2 \
  "alice_in_lair dragon_hoard_spawned dragon_sleeps_when_quiet dragon_stage dragon_woke dragon_wakes_from_noise dragon_breath_damages_miner dragon_stays_in_lair dragon_unaffected_by_hits_blasts_traps montest_done" \
  "monster_replicated miner_hurt_seen" 0 --monpos=36:36

# ---- v2.1: roaming, hunting, new monsters (generated cave; --nomonsters keeps random spawns off, scenarios place their own)
CAVE=""
HOSTEXTRA="--roundsecs=600 --seedbase=${SEEDBASE:-5}"
TIMEOUT=240
run_scenario roam 2 "plan_has_variety alice_in_position roam_hears_noise_far_away roam_golem_reaches_noisy_lit_miner roam_patrols_whole_cave montest_done" "monster_replicated" 0 --monpos=deep:70
run_scenario dark 2 "alice_dark_and_still dark_still_miner_ignored_at_4m dark_still_miner_seen_at_1_5m montest_done" "" 0 --monpos=deep:60 --monlantern=off
run_scenario hall 2 "alice_in_hall hall_never_entered monster_stops_at_hall_edge montest_done" "" 0
run_scenario lamp 2 "alice_in_position lamplighter_ignores_miner_beyond_3m lamplighter_snuffs_when_attacked lamplighter_grabs_attacker lamplighter_deals_40_damage lamplighter_lets_go_and_walks_away lamplighter_walks_away_far lamplighter_relights_and_stands lamplighter_triggers_within_3m lamplighter_second_grab montest_done" "miner_hurt_seen" 0 --monpos=deep:60
HOSTEXTRA="--roundsecs=600 --seedbase=${SEEDBASE:-5} --give=Alice=ore:diamond,Alice=ore:coal,Bob=ore:gold"
BOBARGS="--monpos=deep:60:1"
run_scenario assessor 3 "bots_in_position assessor_stalks_highest_tribute assessor_catches_target assessor_still_holding_during_pin assessor_pin_ends assessor_takes_the_single_highest_stone assessor_credits_nobody assessor_burden_follows_the_sack assessor_retreats assessor_never_runs montest_done" "" 1 --monpos=deep:60
BOBARGS=""
HOSTEXTRA="--roundsecs=600 --seedbase=${SEEDBASE:-5}"
run_scenario plug 2 "alice_in_position nav_profiles_fit plug_spawns_in_a_tunnel plug_creeps_about_1_mps plug_creeps_toward_the_noise plug_body_blocks_the_tunnel plug_stun_applied plug_stun_is_short plug_pushed_back_by_dynamite plug_crushes_20_dps montest_done" "monster_replicated miner_hurt_seen" 0 --monpos=deep:60
run_scenario wicks 2 "alice_in_position wicks_ignore_dark_miner wicks_ignore_flash_under_1s wicks_drawn_to_lit_lantern wicks_drain_oil_fast wicks_bite wicks_lose_miner_within_3s_of_lights_out wicks_circle_last_lit_spot wicks_never_track_dark_miner wicks_drift_back_to_wandering wicks_attracted_again_on_relight montest_done" "monster_replicated" 0 --monpos=deep:60
run_scenario plugwalk 2 "alice_at_tunnel_mouth miner_cannot_pass_the_plug miner_not_launched_by_the_plug miner_not_launched_by_creeping_plug miner_pushed_back_not_through montest_done" "" 0 --monpos=tun --monpush
run_scenario loudloot 2 "alice_in_position golem_waits_before_the_reveal reveal_noise_pulls_a_far_golem golem_walks_toward_the_reveal mimic_shriek_pulls_a_far_golem mimic_cannot_settle_in_a_tunnel montest_done" "" 0 --monpos=deep:60
HOSTEXTRA="--roundsecs=600 --seedbase=${SEEDBASE:-5} --mondirector --dirquiet=14 --dircheck=5"
run_scenario director 2 "alice_in_position director_sent_a_monster_after_quiet director_waited_for_the_quiet_period director_monster_heads_to_the_miner director_monster_closes_in montest_done" "" 0 --monpos=deep:60
HOSTEXTRA="--roundsecs=600 --seedbase=${SEEDBASE:-5}"
run_scenario replicate 2 "alice_in_position all_monster_kinds_moved_on_host lamplighter_grab_for_replication montest_done" "monster_replicated:golem monster_replicated:wicks monster_replicated:plug monster_replicated:lamplighter monster_replicated:assessor monster_moved_on_client:golem monster_moved_on_client:wicks monster_moved_on_client:plug monster_moved_on_client:lamplighter monster_moved_on_client:assessor monster_state_seen:lamplighter monster_state_seen:golem" 0 --monpos=deep:60

if [ $RC -eq 0 ]; then echo "MONSTERS PASS"; else echo "MONSTERS FAILED (see $LOGDIR)"; fi
exit $RC
