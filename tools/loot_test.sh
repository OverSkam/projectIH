#!/usr/bin/env bash
# Step 2 loot test: geodes, bright chests, mimics (src/Loot2).
#  1. `-- --l2check`: seeded roll tables (dud rate, junk share, stein rates 4 / 1 / 0.3 %, depth / round / size effects, Luck over 2000 rolls, chest and vein tables)
#  2. one scripted match in the grey-box cave (2 miners = 1 round): Host (bot l2_host, runs the Loot2Director) and Alice (bot l2_alice) as separate processes:
#       vein -> geodes (no stones) | Alice cracks a geode in 3 real pickaxe hits, the stones replicate and are taken (toast) | forced rarities: the reveal noise radius of each tier (stein 45 m)
#       gas dud (damage, mask immunity) and bats dud (noise, harmless) | chest: hold E 2.5 s opens it, 3-6 stones jump out | interrupted hold (damage, moving away) gives nothing
#       mimic: bites when the hold completes, holds 4 s (30 damage, cannot move, lantern out), shriek noise, bait, relocates and replicates | a hit snaps it | a second miner frees the victim | stun
#       private state: only the opener receives chest-hold messages, no_foreign_state
# Usage: tools/loot_test.sh   (env: PORT=7891 LOGDIR=<dir> TIMEOUT=420 ROUNDSECS=420 (the script ends the round itself) SKIP_BUILD=1)
set -u
cd "$(dirname "$0")/.."
PORT="${PORT:-7891}"
TIMEOUT="${TIMEOUT:-420}"
ROUNDSECS="${ROUNDSECS:-420}"
LOGDIR="${LOGDIR:-$(mktemp -d)}"
mkdir -p "$LOGDIR"
RC=0
fail() { echo "LOOT FAIL: $*"; RC=1; }

if [ -z "${SKIP_BUILD:-}" ]; then
  dotnet build AppStein.csproj -nologo -v q 2>&1 | tail -15 > "$LOGDIR/build.txt"
  grep -q "0 Error(s)" "$LOGDIR/build.txt" || { cat "$LOGDIR/build.txt"; echo "LOOT FAIL: build"; exit 1; }
  godot --headless --path . --import > "$LOGDIR/import.log" 2>&1
fi

echo "== roll tables (--l2check)"
timeout 120 godot --headless --path . -- --l2check > "$LOGDIR/l2check.txt" 2>&1 || fail "l2check exited non-zero"
grep -q "^FAIL:" "$LOGDIR/l2check.txt" && { grep "^FAIL:" "$LOGDIR/l2check.txt"; fail "l2check FAIL lines"; }
[ "$(grep -c '^PASS: l2check_' "$LOGDIR/l2check.txt")" -ge 15 ] || fail "l2check did not run all checks"
grep -q "^PASS: l2check all roll-table checks" "$LOGDIR/l2check.txt" || fail "l2check did not pass"
grep -h "^PASS: l2check_" "$LOGDIR/l2check.txt" | sed 's/^PASS: /PASS: /' | head -30

echo "== scripted match (logs in $LOGDIR)"
rm -f "$LOGDIR"/host.log "$LOGDIR"/alice.log
PIDS=()
godot --headless --path . -- --host --name=Host --port="$PORT" --autostart=2 --preset=debug --roundsecs="$ROUNDSECS" --cave=greybox --l2test=main ${L2PROBE:+--l2probe} ${L2ONLY:+--l2only=$L2ONLY} ${BOTDEBUG:+--botdebug} --give=stein:regen --bot=l2_host --nomonsters --notraps --timeout="$TIMEOUT" > "$LOGDIR/host.log" 2>&1 &
PIDS+=($!)
sleep 4
godot --headless --path . -- --join=127.0.0.1 --port="$PORT" --name=Alice --cave=greybox --bot=l2_alice ${BOTDEBUG:+--botdebug} --timeout="$TIMEOUT" > "$LOGDIR/alice.log" 2>&1 &
PIDS+=($!)
start=$(date +%s)
while :; do
  alive=0
  for p in "${PIDS[@]}"; do kill -0 "$p" 2>/dev/null && alive=1; done
  [ $alive -eq 0 ] && break
  if [ $(( $(date +%s) - start )) -gt "$TIMEOUT" ]; then fail "timeout"; kill "${PIDS[@]}" 2>/dev/null; break; fi
  sleep 1
done
wait 2>/dev/null
H="$LOGDIR/host.log"; A="$LOGDIR/alice.log"
ALL=$(cat "$H" "$A")
IGN="csharp_lang|Leaked unsafe reference|Unreferenced static string|leaked at exit|Pages in use|non-existing signal|propagate_notification|meta. values|connected_peers"
if grep -hE "^FAIL:|ERROR|SCRIPT ERROR|Unhandled exception|Exception:" "$H" "$A" | grep -vE "$IGN" | head -20 | grep .; then fail "FAIL lines / errors in the logs"; fi
want() { if grep -q "^PASS: $1" <<<"$ALL"; then echo "PASS: $1"; else fail "missing PASS: $1"; fi; }
for n in l2_start vein_broken_into_geodes vein_drops_no_stones geode_three_hits geode_stones_spawned geode_stone_taken geode_stones_replicated \
         reveal_noise_tier0 reveal_noise_tier1 reveal_noise_tier2 reveal_noise_tier3 stein_reveal_heard_45m \
         gas_dud_hurts gas_dud_mask_immune bats_dud_noisy_harmless \
         chest_hold_opens chest_hold_takes_2_5s chest_gives_stones chest_interrupted_damage chest_interrupted_away \
         mimic_bites mimic_lantern_out mimic_shriek_noise mimic_holds_4s mimic_victim_cannot_move mimic_30_damage mimic_lantern_back mimic_spits_bait mimic_relocates mimic_is_chest_again \
         mimic_snaps_when_hit mimic_unkillable mimic_freed_by_second_miner mimic_freed_less_damage mimic_rooted_stays mimic_stunned mimic_stun_blocks_opening mimic_stun_ends mimic_stunned_by_dynamite l2_done; do want $n; done
# what the remote client (Alice) saw and felt
for n in geode_hit_seen loot_burst_seen loot_toast bchest_pop_seen bchest_open_seen mimic_bite_seen mimic_run_seen mimic_settled_seen mimic_pos_replicated chest_hold_msg geode_dud_seen; do
  grep -q "^PASS: $n" "$A" || fail "Alice never logged PASS: $n"
done
grep -q "^PASS: mimic_pos_replicated" "$H" || fail "the host's client did not see the mimic at its new place"
# private state: the host never held a chest, so it never receives a hold message; nobody gets another miner's state
grep -q "^PASS: chest_hold_msg" "$H" && fail "the host received a chest-hold message that was not for it"
for f in "$H" "$A"; do grep -q "^PASS: no_foreign_state" "$f" || fail "$(basename "$f"): no_foreign_state missing"; done
grep -q "^PASS: match_end" "$H" || fail "the match did not end normally"
if [ $RC -eq 0 ]; then echo "LOOT PASS"; else echo "LOOT FAILED (see $LOGDIR)"; fi
exit $RC
