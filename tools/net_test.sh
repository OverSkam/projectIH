#!/usr/bin/env bash
# App Stein network test: room codes, room password handshake, join errors. Separate headless processes on localhost (no internet, UPnP is skipped in automated runs).
#  1. --netcheck: room code round trips, typos, forgiving input, raw IP / IP:port parsing, password hash helper.
#  2. Host A (port PORT) with --password=secret, 2 players start a debug match:
#       a) Alice with the right password joins and appears as a player on the host
#       b) Mallory with a wrong password is refused ("Wrong password"), never appears on the host and receives no game state (the match is running!)
#       c) a client with no password is refused ("needs a password")
#       d) a client that never answers the handshake is dropped after the ~6 s timeout and never appears
#       e) after 5 wrong tries from one address the address is blocked: even the right password is refused for a minute
#  3. Host B (port PORT+1) open room: a client without a password joins (f); a client joins with a room code built from 127.0.0.1:PORT+1 (g),
#     also typed in lower case without dashes (h).
# Usage: tools/net_test.sh        (env: PORT=7777  LOGDIR=<dir>  TIMEOUT=180)
set -u
cd "$(dirname "$0")/.."
PORT="${PORT:-7777}"
PORT2=$((PORT + 1))
TIMEOUT="${TIMEOUT:-180}"
LOGDIR="${LOGDIR:-$(mktemp -d)}"
mkdir -p "$LOGDIR"
rm -f "$LOGDIR"/*.log "$LOGDIR"/*.txt
RC=0
fail() { echo "NET FAIL: $*"; RC=1; }
ok() { echo "PASS: $*"; }
PIDS=()
cleanup() { for p in "${PIDS[@]}"; do kill "$p" 2>/dev/null; done; }
trap cleanup EXIT

G=(godot --headless --path . --)
EXTRA=(--preset=debug --roundsecs=45 --nomonsters --notraps)

echo "== build"
dotnet build AppStein.csproj -nologo -v q 2>&1 | tail -15 > "$LOGDIR/build.txt"
if ! grep -q "0 Error(s)" "$LOGDIR/build.txt"; then cat "$LOGDIR/build.txt"; echo "NET FAIL: build"; exit 1; fi
godot --headless --path . --import > "$LOGDIR/import.log" 2>&1

echo "== netcheck (room codes, parsing, password hash)"
"${G[@]}" --netcheck > "$LOGDIR/netcheck.log" 2>&1; code=$?
[ $code -eq 0 ] || fail "netcheck exited with $code"
grep -q "^FAIL:" "$LOGDIR/netcheck.log" && fail "netcheck FAIL lines"
for w in netcheck_roundtrip netcheck_scrambled netcheck_typo netcheck_forgiving netcheck_parse netcheck_password "netcheck all"; do
  grep -q "^PASS: $w" "$LOGDIR/netcheck.log" && ok "$w" || fail "missing PASS: $w"
done

# wait_for FILE PATTERN SECONDS: 0 when the pattern shows up in the file
wait_for() { local i; for ((i = 0; i < $3 * 5; i++)); do grep -qE "$2" "$1" 2>/dev/null && return 0; sleep 0.2; done; return 1; }
# run_client NAME ARGS...: runs a client to its end (it quits by itself on a refusal), max 25 s; sets CODE
run_client() {
  local n="$1"; shift
  "${G[@]}" --name="$n" --bot=idle --timeout=40 "$@" > "$LOGDIR/$n.log" 2>&1 &
  local pid=$! i
  for ((i = 0; i < 125; i++)); do kill -0 "$pid" 2>/dev/null || break; sleep 0.2; done
  if kill -0 "$pid" 2>/dev/null; then kill "$pid" 2>/dev/null; CODE=99; else wait "$pid"; CODE=$?; fi
}
# start_client NAME ARGS...: runs in the background until we kill it (a client that is accepted stays)
start_client() { local n="$1"; shift; "${G[@]}" --name="$n" --bot=idle --timeout="$TIMEOUT" "$@" > "$LOGDIR/$n.log" 2>&1 & PIDS+=($!); }
refused_cleanly() {   # NAME REASON-REGEX
  local n="$1" why="$2" l="$LOGDIR/$1.log"
  [ "$CODE" -ne 0 ] && [ "$CODE" -ne 99 ] || fail "$n: expected a failed join, exit code was $CODE"
  grep -qE "JOIN_REJECTED: .*($why)" "$l" || fail "$n: no '$why' reason in its log: $(grep -E 'JOIN_REJECTED|join failed' "$l" | head -2)"
  # never got in, never saw any game state (the host is mid-match): nothing but the refusal
  grep -qE "PASS: connected|NET: connected to server|^ROUND:|^MATCH:|OWNSTATE|PASS: round_started|PASS: ore_|PASS: weigh|^FOREIGN|spawn" "$l" && fail "$n: got in or received game state: $(grep -E 'PASS: connected|NET: connected|^ROUND:|^MATCH:|OWNSTATE|PASS: round_started|spawn' "$l" | head -3)"
  grep -q "registered as $n" "$LOGDIR/hostA.log" && fail "$n appears on the host"
  return 0
}

echo "== room with a password (port $PORT)"
"${G[@]}" --host --name=Host --port="$PORT" --password=secret --autostart=2 "${EXTRA[@]}" --bot=idle --timeout="$TIMEOUT" > "$LOGDIR/hostA.log" 2>&1 &
HOSTA=$!; PIDS+=($HOSTA)
wait_for "$LOGDIR/hostA.log" "NET: hosting" 20 || fail "host A did not start"
grep -q "NET: hosting on port $PORT (password protected)" "$LOGDIR/hostA.log" || fail "host A does not say it is password protected"

# a) right password
start_client Alice --join=127.0.0.1 --port="$PORT" --password=secret
wait_for "$LOGDIR/Alice.log" "^PASS: connected" 25 && ok "right_password_connected" || fail "a) Alice with the right password did not connect"
wait_for "$LOGDIR/hostA.log" "registered as Alice" 5 && ok "player_on_host" || fail "a) Alice never appeared on the host"
wait_for "$LOGDIR/hostA.log" "PASS: peers_connected count=2" 10 || fail "a) host did not count 2 peers"
wait_for "$LOGDIR/hostA.log" "^MATCH: start" 10 && ok "match_started_with_password_room" || fail "a) match did not start"
wait_for "$LOGDIR/hostA.log" "^ROUND: 1 start" 10 || fail "a) round 1 did not start"

# b) wrong password, mid-match
run_client Mallory --join=127.0.0.1 --port="$PORT" --password=wrong
refused_cleanly Mallory "Wrong password" && ok "wrong_password_refused"
grep -q "refused: Wrong password" "$LOGDIR/hostA.log" || fail "b) host did not log the refusal"

# c) no password
run_client Nopw --join=127.0.0.1 --port="$PORT"
refused_cleanly Nopw "needs a password" && ok "missing_password_refused"

# d) silent client: never answers the challenge
run_client Silent --join=127.0.0.1 --port="$PORT" --authstall
[ "$CODE" -ne 0 ] || fail "d) stalled client was not dropped (exit $CODE)"
grep -q "never finished the handshake" "$LOGDIR/hostA.log" && ok "silent_client_dropped" || fail "d) host did not drop the silent client"
grep -q "registered as Silent" "$LOGDIR/hostA.log" && fail "d) silent client appeared on the host"

# e) brute-force brake: wrong tries so far from this address = Mallory + Nopw = 2; three more make 5
for n in W3 W4 W5; do run_client $n --join=127.0.0.1 --port="$PORT" --password=nope; done
grep -q "blocked for a minute" "$LOGDIR/hostA.log" || fail "e) address was not blocked after 5 wrong tries"
run_client Late --join=127.0.0.1 --port="$PORT" --password=secret
refused_cleanly Late "Too many wrong" && ok "brute_force_brake"
for n in W3 W4 W5; do grep -q "registered as $n" "$LOGDIR/hostA.log" && fail "$n appears on the host"; done

# the game itself is untouched by all this: still exactly the two real players
grep -E "registered as" "$LOGDIR/hostA.log" | grep -vE "registered as (Alice|Host)" | grep -q . && fail "an unexpected peer registered on host A: $(grep 'registered as' "$LOGDIR/hostA.log")"
ok "only_alice_registered"

echo "== open room (port $PORT2)"
"${G[@]}" --host --name=Host2 --port="$PORT2" --bot=idle --nomonsters --notraps --timeout="$TIMEOUT" > "$LOGDIR/hostB.log" 2>&1 &
HOSTB=$!; PIDS+=($HOSTB)
wait_for "$LOGDIR/hostB.log" "NET: hosting" 20 || fail "host B did not start"
grep -q "password protected" "$LOGDIR/hostB.log" && fail "open room claims a password"
start_client Dora --join=127.0.0.1 --port="$PORT2"
wait_for "$LOGDIR/Dora.log" "^PASS: connected" 25 && ok "open_room_joinable" || fail "f) open room: Dora did not connect"
wait_for "$LOGDIR/hostB.log" "registered as Dora" 5 || fail "f) Dora not on host B"

CODE1=$("${G[@]}" --printcode=127.0.0.1:"$PORT2" 2>&1 | sed -n 's/^ROOMCODE: //p')
echo "room code for 127.0.0.1:$PORT2 = $CODE1"
[[ "$CODE1" =~ ^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$ ]] || fail "g) printcode gave '$CODE1'"
start_client Cody --join="$CODE1"
wait_for "$LOGDIR/Cody.log" "^PASS: connected" 25 && ok "join_by_room_code" || fail "g) joining by room code failed: $(tail -3 "$LOGDIR/Cody.log")"
wait_for "$LOGDIR/hostB.log" "registered as Cody" 5 || fail "g) Cody not on host B"
LOWER=$(echo "$CODE1" | tr 'A-Z' 'a-z' | tr -d '-')
start_client Lola --join="$LOWER"
wait_for "$LOGDIR/Lola.log" "^PASS: connected" 25 && ok "join_by_lowercase_code_without_dashes" || fail "h) lowercase code without dashes failed"
# a typo in the code is reported clearly and nothing is contacted
BAD="${CODE1:0:13}$([ "${CODE1:13:1}" = "A" ] && echo B || echo A)"
run_client Typo --join="$BAD"
grep -q "not valid" "$LOGDIR/Typo.log" && [ "$CODE" -ne 0 ] && ok "typo_in_code_reported" || fail "i) typo not reported: $(tail -2 "$LOGDIR/Typo.log")"
# a password given to an open room is simply ignored
start_client Pia --join=127.0.0.1 --port="$PORT2" --password=whatever
wait_for "$LOGDIR/Pia.log" "^PASS: connected" 25 && ok "open_room_ignores_password" || fail "j) open room refused a client that typed a password"

# let host A's match finish by itself (automated host quits at the match end)
echo "== waiting for host A's match to end"
start=$(date +%s)
while kill -0 "$HOSTA" 2>/dev/null; do
  [ $(( $(date +%s) - start )) -gt "$TIMEOUT" ] && { fail "host A did not finish"; break; }
  sleep 1
done
wait "$HOSTA" 2>/dev/null; hc=$?
[ $hc -eq 0 ] || fail "host A exited with $hc"
grep -q "^WINNER=" "$LOGDIR/hostA.log" && ok "match_finished_normally" || fail "match on host A did not finish"

cleanup
sleep 1
ALL=$(cat "$LOGDIR"/hostA.log "$LOGDIR"/hostB.log "$LOGDIR"/Alice.log "$LOGDIR"/Dora.log "$LOGDIR"/Cody.log "$LOGDIR"/Lola.log "$LOGDIR"/Pia.log)
grep -E "UPnP|IP lookup|api.ipify|ifconfig" <<<"$ALL" | grep . && fail "an automated run touched UPnP / the public IP lookup"
grep -E "ERROR|SCRIPT ERROR|Unhandled exception|Exception:|Traceback" "$LOGDIR"/hostA.log "$LOGDIR"/hostB.log "$LOGDIR"/Alice.log "$LOGDIR"/Mallory.log "$LOGDIR"/Nopw.log "$LOGDIR"/Cody.log \
  | grep -vE "leaked at exit|Pages in use|RID allocations|Unreferenced static string" | grep . && fail "runtime errors in logs"

if [ $RC -eq 0 ]; then echo "NET TEST PASS"; else echo "NET TEST FAILED (see $LOGDIR)"; fi
exit $RC
