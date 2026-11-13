#!/usr/bin/env bash
# Items / downed / lantern oil / elimination edge cases: scripted matches with separate headless processes (see src/Items/ItemsDebug.cs).
#  util       (5 miners, 4 rounds): v2.1 sack (stuffed stein, Infinity not stuffable, unlimited sack, burden, bag pour all-or-nothing, robbery one stone per press highest first), chest loot only to opener, stein stats owner-only, gas mask, dynamite, bear trap, rope climb/cut, Midas statue,
#                                   ghost meddling, respawn with pickaxe+lantern only, Blood stein penalty / kept with a kill, lantern oil (drain, toggle,
#                                   public flicker/out, drum refill), downed -> looted -> bleeds out, downed -> hit again -> dies, final two
#  infinity   (4 miners, 3 rounds): Infinity Stein reset on drop / transferred / counted per round, wins at the end of round 3 before the weighing
#  tie        (3 miners):           two miners tied for lightest are both thrown, the last miner wins
#  disconnect (3 miners):           a miner who disconnects is eliminated; when one miner remains the match ends with them as winner
# Usage: tools/items_test.sh   (env: PORT=7802 LOGDIR=<dir> TIMEOUT=300)
set -u
cd "$(dirname "$0")/.."
PORT="${PORT:-7802}"
TIMEOUT="${TIMEOUT:-300}"
LOGDIR="${LOGDIR:-$(mktemp -d)}"
mkdir -p "$LOGDIR"
RC=0
fail() { echo "ITEMS FAIL: $*"; RC=1; }

if [ -z "${SKIP_BUILD:-}" ]; then
  dotnet build AppStein.csproj -nologo -v q 2>&1 | tail -15 > "$LOGDIR/build.txt"
  grep -q "0 Error(s)" "$LOGDIR/build.txt" || { cat "$LOGDIR/build.txt"; echo "ITEMS FAIL: build"; exit 1; }
  godot --headless --path . --import > "$LOGDIR/import.log" 2>&1
fi

run_match() { # name give extra-client-names...   (clients: Alice Bob [Carol Dave])
  local name="$1" give="$2"; shift 2
  local clients=("$@") d="$LOGDIR/$name"
  mkdir -p "$d"; rm -f "$d"/*.log
  local PIDS=()
  godot --headless --path . -- --host --name=Host --port="$PORT" --autostart=$(( ${#clients[@]} + 1 )) --preset=debug --itest="$name" --give="$give" --bot=t_host --nomonsters --notraps --timeout="$TIMEOUT" > "$d/host.log" 2>&1 &
  PIDS+=($!)
  sleep 4
  for c in "${clients[@]}"; do
    local lc; lc=$(echo "$c" | tr 'A-Z' 'a-z')
    godot --headless --path . -- --join=127.0.0.1 --port="$PORT" --name="$c" --bot=t_"$lc" --timeout="$TIMEOUT" > "$d/$lc.log" 2>&1 &
    PIDS+=($!)
  done
  local start=$(date +%s)
  while :; do
    local alive=0
    for p in "${PIDS[@]}"; do kill -0 "$p" 2>/dev/null && alive=1; done
    [ $alive -eq 0 ] && break
    if [ $(( $(date +%s) - start )) -gt "$TIMEOUT" ]; then fail "$name: timeout"; kill "${PIDS[@]}" 2>/dev/null; break; fi
    sleep 1
  done
  ALL=$(cat "$d"/*.log)
  grep -h "^FAIL:" "$d"/*.log && fail "$name: FAIL lines"
  grep -hE "ERROR|SCRIPT ERROR|Unhandled exception|Exception:" "$d"/*.log | grep -vE "leaked at exit|Pages in use|RID allocations|Unreferenced static string" | grep . && fail "$name: runtime errors"
}
want() { if grep -q "^PASS: $1" <<<"$ALL"; then echo "PASS: $CUR/$1"; else fail "$CUR: missing PASS: $1"; fi; }

CUR=util
run_match util "stein:regen,Alice=util:gasmask,Bob=util:dynamite,Host=util:beartrap" Alice Bob Carol Dave
for n in chest_opened chest_loot_to_opener_only chest_loot_received stein_stats_owner_only own_swift_applied no_foreign_loot no_foreign_state \
         gasmask_immune gas_hurts_unprotected mask_visible_remote dynamite_placed dynamite_exploded dynamite_vein_broken dynamite_noise explosion_seen \
         beartrap_placed beartrap_triggered rooted_received root_held rope_placed rope_climbed rope_climbed_server rope_cut \
         midas_statue midas_statue_verified \
         ghost_lantern_blown lantern_out_seen ghost_pebble ghost_pebble_noise respawn_kit_only \
         blood_penalty_applied blood_kept_with_kill blood_penalty_verified blood_kept_verified \
         oil_starts_full oil_drains_while_lit lantern_toggled_off lantern_out_seen oil_not_burning_when_off lantern_toggled_on lantern_low_seen oil_refilled oil_refilled_to_full \
         lantern_out_of_oil lantern_out_when_oil_gone \
         stuffed_stein_counts infinity_cannot_be_stuffed sack_unlimited burden_grows burden_speed_formula bag_poured_all bag_poured miner_downed downed_state_set downed_cannot_act downed_seen looted_downed downed_looted_by_other downed_bleeds_out downed_second_state downed_further_damage_kills \
         miner_eliminated final_weighing winner_server match_end; do want $n; done
grep -q "^PASS: dynamite_exploded.*victims=[1-9]" <<<"$ALL" || fail "dynamite hurt nobody"
grep -q "^PASS: chest_opened opener=Alice" "$LOGDIR/util/host.log" || fail "chest opener is not Alice"
grep -q "^PASS: beartrap_triggered victim=Alice" "$LOGDIR/util/host.log" || fail "trap victim is not Alice"
grep -q "^PASS: rooted_received" "$LOGDIR/util/alice.log" || fail "Alice not rooted"
grep -q "^PASS: rooted_received" "$LOGDIR/util/bob.log" && fail "Bob was rooted"
grep -q "^PASS: chest_loot_received" "$LOGDIR/util/bob.log" && fail "Bob received chest loot"
grep -q "^PASS: own_swift_applied" "$LOGDIR/util/bob.log" && fail "Bob got swift"
grep -q "^PASS: blood_penalty_applied name=Host" "$LOGDIR/util/host.log" || fail "blood penalty not on Host"
grep -q "^PASS: blood_kept_with_kill name=Alice" "$LOGDIR/util/host.log" || fail "blood not kept by Alice"
grep -q "^PASS: midas_statue victim=Dave killer=Bob" "$LOGDIR/util/host.log" || fail "Midas statue not left for Dave by Bob"
grep -q "^PASS: looted_downed looter=Alice victim=Host" "$LOGDIR/util/host.log" || fail "Alice did not loot the downed Host"
grep '^PASS: looted_downed looter=Alice victim=Host' "$LOGDIR/util/host.log" | head -1 | grep -q 'item=ore:gold' || fail "robbery did not take the most valuable stone first"
[ "$(grep -c '^PASS: looted_downed looter=Alice victim=Host' "$LOGDIR/util/host.log")" -ge 3 ] || fail "robbery should take one item per press (3 presses for gold, coal, swift)"
grep -q "^PASS: lantern_low_seen" "$LOGDIR/util/host.log" || fail "the low-oil flicker was not visible on another client"
grep -q "^PASS: death_bag_dropped victim=Host cause=bleed.*downed=True" "$LOGDIR/util/host.log" || fail "Host did not bleed out"
grep -q "^PASS: death_bag_dropped victim=Alice cause=test.*downed=True" "$LOGDIR/util/host.log" || fail "a further hit did not kill the downed Alice"
# the oil level is private: no client's own-state log ever shows another miner's oil (each log has only its owner's OWNSTATE lines)
[ "$(grep -c '^PASS: miner_eliminated' "$LOGDIR/util/host.log")" = "4" ] || fail "expected 4 eliminations in the util match (Carol, Dave, Bob, then Host in the final two)"
grep -q "^WINNER=Alice" "$LOGDIR/util/host.log" || fail "util winner is not Alice"

CUR=infinity
run_match infinity "Alice=stein:infinity" Alice Bob Carol
for n in infinity_reset_on_drop infinity_transferred infinity_count infinity_win infinity_match_end miner_eliminated match_end no_foreign_state; do want $n; done
grep -q "^PASS: infinity_win winner=Bob round=3" "$LOGDIR/infinity/host.log" || fail "Bob did not win at round 3"
[ "$(grep -c '^PASS: round_started' "$LOGDIR/infinity/host.log")" = "3" ] || fail "expected exactly 3 rounds"
grep -q "carried for 1 round\." "$LOGDIR/infinity/alice.log" || fail "no announcement for round 1"
grep -q "carried for 3 rounds" "$LOGDIR/infinity/alice.log" || fail "no announcement for 3 rounds"
grep -h "carried for" "$LOGDIR"/infinity/*.log | grep -qE "Alice|Bob|Host|Carol" && fail "announcement named the holder"
grep -q "WINNER=Bob" "$LOGDIR/infinity/host.log" || fail "winner is not Bob"

CUR=tie
run_match tie "stein:regen" Alice Bob
for n in weigh_reveal weigh_verdict weigh_throw miner_eliminated final_weighing winner_server match_end no_foreign_state; do want $n; done
grep -q "^PASS: weigh_verdict thrown=.* and .* finalTwo=False tie=True" "$LOGDIR/tie/host.log" || fail "tied lightest miners were not both thrown"
[ "$(grep -c '^PASS: miner_eliminated' "$LOGDIR/tie/host.log")" = "2" ] || fail "expected two eliminations from the tie"
grep -q "^WINNER=Bob" "$LOGDIR/tie/host.log" || fail "tie winner is not Bob"
[ "$(grep -c '^PASS: round_started' "$LOGDIR/tie/host.log")" = "1" ] || fail "tie: expected a single round"

CUR=disconnect
run_match disconnect "stein:regen" Alice Bob
for n in disconnect_eliminated one_left_after_first_leaves last_miner_standing winner_server; do want $n; done
grep -q "^PASS: disconnect_eliminated peer=.* name=Bob" "$LOGDIR/disconnect/host.log" || fail "Bob's disconnect did not eliminate him"
grep -q "^PASS: disconnect_eliminated peer=.* name=Alice" "$LOGDIR/disconnect/host.log" || fail "Alice's disconnect did not eliminate her"
grep -q "^WINNER=Host" "$LOGDIR/disconnect/host.log" || fail "the last miner (Host) did not win"

if [ $RC -eq 0 ]; then echo "ITEMS PASS"; else echo "ITEMS FAILED (see $LOGDIR)"; fi
exit $RC
