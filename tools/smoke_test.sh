#!/usr/bin/env bash
# App Stein smoke test (v2 rules): builds, then runs 1 headless host (idle bot) + 2 headless bot clients on localhost with the debug preset
# (3 miners = 2 rounds) and checks PASS:/FAIL: lines plus runtime errors in the logs.
# Story: Alice (miner bot) feeds the Goblin; Bob (killer bot) hides far from the Hall in round 1 and so weighs zero (locked out); the host is
# seeded with a small tribute (--tribute) and idles in the Hall. Round 1 weighing: Alice heaviest, then Host, then Bob last -> Bob is thrown and
# stays a ghost; round 2 is the final two: Alice outweighs Host and wins.
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

echo "== weighing rules self-check"
godot --headless --path . -- --weighcheck > "$LOGDIR/weighcheck.txt" 2>&1 || fail "weighcheck exited non-zero"
grep -q "^FAIL:" "$LOGDIR/weighcheck.txt" && fail "weighcheck FAIL lines"
[ "$(grep -c '^PASS: weighcheck_' "$LOGDIR/weighcheck.txt")" -ge 9 ] || fail "weighcheck did not run all checks (ties, final two, lightest, pile API)"
echo "PASS: weighcheck (ties, all tied, final two, single miner, pile API)"

echo "== run (logs in $LOGDIR)"
PIDS=()
godot --headless --path . -- --host --name=Host --port="$PORT" --autostart=3 --preset=debug --bot=idle --nomonsters --notraps --tribute=Alice=40,Host=6 --timeout="$TIMEOUT" > "$LOGDIR/host.log" 2>&1 &
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
for want in connected peers_connected round_started ore_mined ore_picked_up feed_server tribute_credited no_foreign_state locked_out \
            weigh_begin weigh_reveal weigh_verdict weigh_throw weigh_remain miner_eliminated eliminated_ghost final_weighing winner_server match_end; do
  if grep -q "^PASS: $want" <<<"$ALL"; then echo "PASS: $want"; else fail "missing PASS: $want"; fi
done
grep -h "^FAIL:" "$LOGDIR"/host.log "$LOGDIR"/alice.log "$LOGDIR"/bob.log && fail "FAIL lines present"
grep -hE "ERROR|SCRIPT ERROR|Unhandled exception|Exception:|Traceback" "$LOGDIR"/host.log "$LOGDIR"/alice.log "$LOGDIR"/bob.log && fail "runtime errors in logs"

# ---- scenario assertions
H="$LOGDIR/host.log"
[ "$(grep -c '^PASS: round_started' "$H")" -eq 2 ] || fail "expected exactly 2 rounds (3 miners = N-1)"
# feeding: only Alice fed, and only Alice's client was told her tribute credit
if grep '^PASS: feed_server' "$H" | grep -qv 'name=Alice'; then fail "feeding credited to someone other than Alice"; fi
grep -q '^PASS: tribute_credited' "$LOGDIR/alice.log" || fail "Alice never saw her tribute credited"
grep -q '^PASS: tribute_credited' "$LOGDIR/bob.log" && fail "Bob saw a tribute credit"
grep -q '^PASS: tribute_credited' "$H" && fail "Host saw a tribute credit"
# hidden information: nobody but the owner ever sees a tribute number before the weighing
grep -q 'OWNSTATE tribute=[1-9]' "$LOGDIR/bob.log" && fail "Bob's own state shows tribute he never had"
# the miner outside the Hall weighs zero and loses everything
grep -q '^PASS: locked_out name=Bob' "$H" || fail "Bob was not locked out of the Hall in round 1"
# the ceremony: reveal order heaviest first, identical on every peer
rv() { grep '^PASS: weigh_reveal' "$1" | sed 's/^PASS: weigh_reveal //'; }
rv "$H" > "$LOGDIR/rv_host.txt"; rv "$LOGDIR/alice.log" > "$LOGDIR/rv_alice.txt"; rv "$LOGDIR/bob.log" > "$LOGDIR/rv_bob.txt"
cmp -s "$LOGDIR/rv_host.txt" "$LOGDIR/rv_alice.txt" || fail "Alice saw a different reveal sequence than the host"
cmp -s "$LOGDIR/rv_host.txt" "$LOGDIR/rv_bob.txt" || fail "Bob saw a different reveal sequence than the host"
sed -n '1,3p' "$LOGDIR/rv_host.txt" | awk '{print $2" "$3}' | tr '\n' ' ' | grep -q 'name=Alice weight=[0-9]* name=Host weight=6 name=Bob weight=0' || fail "round 1 reveal order is not Alice, Host (6), Bob (0): $(sed -n '1,3p' "$LOGDIR/rv_host.txt" | tr '\n' ' ')"
# the lightest is thrown, stays a ghost, the next round has one miner fewer, the final two are decided by weight
grep -q '^PASS: miner_eliminated name=Bob round=1' "$H" || fail "Bob was not the miner eliminated in round 1"
grep -q 'weigh_remain remaining=2' "$H" || fail "no '2 miners remain' card after round 1"
grep -q '^PASS: weigh_verdict thrown=Host finalTwo=True' "$H" || fail "final weighing: Host should be thrown as the lighter of the final two"
grep -q '^PASS: eliminated_ghost round=1' "$LOGDIR/bob.log" || fail "Bob did not become a ghost after being thrown"
grep -q '^PASS: eliminated_ghost round=2' "$LOGDIR/bob.log" || fail "Bob did not stay a ghost in round 2"
grep -q '^PASS: respawned_next_round' "$LOGDIR/bob.log" && fail "eliminated Bob came back as a miner"
grep -q '^WINNER=Alice' "$H" || fail "winner is not Alice"
grep -q 'ROUND: 2 start miners=2' "$H" || fail "round 2 did not start with 2 miners"

if [ $RC -eq 0 ]; then echo "SMOKE PASS"; else echo "SMOKE FAILED (see $LOGDIR)"; fi
exit $RC
