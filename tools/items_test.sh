#!/usr/bin/env bash
# Items/steins/trials test: two scripted matches with separate processes (host + Alice + Bob), see src/Items/ItemsDebug.cs.
#  util     (3 rounds): chest loot only to opener, stein stats owner-only, gas mask, dynamite, bear trap, rope climb/cut,
#                       ghost meddling, respawn rule, Blood stein penalty / kept with kill
#  infinity (5 rounds): Infinity Stein held 3 round-ends wins; reset when dropped; no holder named
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

run_match() { # name rounds give
  local name="$1" rounds="$2" give="$3" d="$LOGDIR/$1"
  mkdir -p "$d"; rm -f "$d"/*.log
  local PIDS=()
  godot --headless --path . -- --host --name=Host --port="$PORT" --autostart=3 --preset=debug --rounds="$rounds" --itest="$name" --give="$give" --bot=t_host --timeout="$TIMEOUT" > "$d/host.log" 2>&1 &
  PIDS+=($!)
  sleep 4
  godot --headless --path . -- --join=127.0.0.1 --port="$PORT" --name=Alice --bot=t_alice --timeout="$TIMEOUT" > "$d/alice.log" 2>&1 &
  PIDS+=($!)
  godot --headless --path . -- --join=127.0.0.1 --port="$PORT" --name=Bob --bot=t_bob --timeout="$TIMEOUT" > "$d/bob.log" 2>&1 &
  PIDS+=($!)
  local start=$(date +%s)
  while :; do
    local alive=0
    for p in "${PIDS[@]}"; do kill -0 "$p" 2>/dev/null && alive=1; done
    [ $alive -eq 0 ] && break
    if [ $(( $(date +%s) - start )) -gt "$TIMEOUT" ]; then fail "$name: timeout"; kill "${PIDS[@]}" 2>/dev/null; break; fi
    sleep 1
  done
  ALL=$(cat "$d"/host.log "$d"/alice.log "$d"/bob.log)
  grep -h "^FAIL:" "$d"/*.log && fail "$name: FAIL lines"
  grep -hE "ERROR|SCRIPT ERROR|Unhandled exception|Exception:" "$d"/*.log && fail "$name: runtime errors"
}
want() { if grep -q "^PASS: $1" <<<"$ALL"; then echo "PASS: $1"; else fail "$CUR: missing PASS: $1"; fi; }

CUR=util
run_match util 3 "stein:regen,Alice=util:gasmask,Bob=util:dynamite,Host=util:beartrap"
for n in chest_opened chest_loot_to_opener_only chest_loot_received stein_stats_owner_only own_swift_applied no_foreign_loot no_foreign_state \
         gasmask_immune gas_hurts_unprotected mask_visible_remote dynamite_placed dynamite_exploded dynamite_vein_broken dynamite_noise explosion_seen \
         beartrap_placed beartrap_triggered rooted_received root_held rope_placed rope_climbed rope_climbed_server rope_cut \
         ghost_lantern_blown lantern_out_seen ghost_pebble ghost_pebble_noise respawn_kit_and_stein \
         blood_penalty_applied blood_kept_with_kill blood_penalty_verified blood_kept_verified match_end; do want $n; done
grep -q "^PASS: dynamite_exploded.*victims=[1-9]" <<<"$ALL" || fail "dynamite hurt nobody"
grep -q "^PASS: chest_opened opener=Alice" "$LOGDIR/util/host.log" || fail "chest opener is not Alice"
grep -q "^PASS: beartrap_triggered victim=Alice" "$LOGDIR/util/host.log" || fail "trap victim is not Alice"
grep -q "^PASS: rooted_received" "$LOGDIR/util/alice.log" || fail "Alice not rooted"
grep -q "^PASS: rooted_received" "$LOGDIR/util/bob.log" && fail "Bob was rooted"
grep -q "^PASS: chest_loot_received" "$LOGDIR/util/bob.log" && fail "Bob received chest loot"
grep -q "^PASS: own_swift_applied" "$LOGDIR/util/bob.log" && fail "Bob got swift"
grep -q "^PASS: blood_penalty_applied name=Host" "$LOGDIR/util/host.log" || fail "blood penalty not on Host"
grep -q "^PASS: blood_kept_with_kill name=Alice" "$LOGDIR/util/host.log" || fail "blood not kept by Alice"

CUR=infinity
run_match infinity 5 "Alice=stein:infinity"
for n in infinity_reset_on_drop infinity_transferred infinity_count infinity_win infinity_match_end match_end no_foreign_state; do want $n; done
grep -q "^PASS: infinity_win winner=Bob round=4" "$LOGDIR/infinity/host.log" || fail "Bob did not win at round 4"
[ "$(grep -c '^PASS: round_started' "$LOGDIR/infinity/host.log")" = "4" ] || fail "expected exactly 4 rounds"
grep -q "carried for 1 round\." "$LOGDIR/infinity/alice.log" || fail "no announcement for round 1"
grep -q "carried for 3 rounds" "$LOGDIR/infinity/alice.log" || fail "no announcement for 3 rounds"
grep -h "carried for" "$LOGDIR"/infinity/*.log | grep -qE "Alice|Bob|Host" && fail "announcement named the holder"
grep -q "WINNER=Bob" "$LOGDIR/infinity/host.log" || fail "winner is not Bob"

if [ $RC -eq 0 ]; then echo "ITEMS PASS"; else echo "ITEMS FAILED (see $LOGDIR)"; fi
exit $RC
