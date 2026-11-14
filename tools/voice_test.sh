#!/usr/bin/env bash
# App Stein voice chat test: the whole pipeline (fake microphone -> RPC -> server routing -> jitter buffer -> playback buffer) over ENet on localhost,
# headless, no Steam, no real microphone. Miners are "pinned" to known positions with --voicepin so distances are exact.
#  0. --voicecheck: falloff, hearing rules, noise radius, rate buckets, jitter buffer (reorder, loss, late, duplicate, wrap, underrun, overrun), fake codec.
#  Lobby (6 players + 1 peer that never passes the handshake):
#     Alice talks; everybody, however far, hears her (global).
#  Round 1 (positions: Host 60 m, Alice 0, Bob 4, Carol 17, Dave 50, Spam 90, all on the x axis):
#     Bob (4 m) hears Alice at gain 1.0, Carol (17 m) at 0.25, Dave (50 m) and Host (60 m) get nothing; nobody hears their own voice;
#     Bob muted Carol: her frames arrive but nothing is played; Dave whispers: heard by the Host 10 m away, but makes no monster noise;
#     Alice / Carol talking make ServerNoise, at most one per 0.7 s each; Gus and Gina join late and are ghosts: they hear Alice and Carol (global),
#     Gina hears Gus, and nobody alive ever hears Gus; Spam sends one oversized frame and 300 frames at once: refused and capped; a made-up
#     unverified peer id cannot speak; the stalled client receives nothing.
#  Weighing: Alice talks; Dave, 50 m away, hears her at full volume (ceremony rule).
# Usage: tools/voice_test.sh        (env: PORT=7777  LOGDIR=<dir>)
set -u
cd "$(dirname "$0")/.."
PORT="${PORT:-7777}"
LOGDIR="${LOGDIR:-$(mktemp -d)}"
mkdir -p "$LOGDIR"
rm -f "$LOGDIR"/*.log "$LOGDIR"/*.txt
RC=0
fail() { echo "VOICE FAIL: $*"; RC=1; }
ok() { echo "PASS: $*"; }
PIDS=()
cleanup() { for p in "${PIDS[@]}"; do kill "$p" 2>/dev/null; done; }
trap cleanup EXIT

echo "== build"
dotnet build AppStein.csproj -nologo -v q 2>&1 | tail -15 > "$LOGDIR/build.txt"
if ! grep -q "0 Error(s)" "$LOGDIR/build.txt"; then cat "$LOGDIR/build.txt"; echo "VOICE FAIL: build"; exit 1; fi
godot --headless --path . --import > "$LOGDIR/import.log" 2>&1

echo "== voicecheck (rules, jitter buffer, codec)"
godot --headless --path . -- --voicecheck > "$LOGDIR/voicecheck.log" 2>&1; code=$?
[ $code -eq 0 ] || fail "voicecheck exited with $code"
grep -q "^FAIL:" "$LOGDIR/voicecheck.log" && fail "voicecheck FAIL lines: $(grep '^FAIL' "$LOGDIR/voicecheck.log" | head -3)"
grep -q "^PASS: voicecheck" "$LOGDIR/voicecheck.log" && ok "voicecheck $(sed -n 's/^PASS: voicecheck //p' "$LOGDIR/voicecheck.log")" || fail "missing PASS: voicecheck"

wait_for() { local i; for ((i = 0; i < $3 * 5; i++)); do grep -qE "$2" "$1" 2>/dev/null && return 0; sleep 0.2; done; return 1; }

PINS="Host:60:0:0,Alice:0:0:0,Bob:4:0:0,Carol:17:0:0,Dave:50:0:0,Spam:90:0:0"
COMMON=(--fakevoice "--voicepin=$PINS" --nomonsters --notraps --preset=debug --roundsecs=40)
G=(godot --headless --path . --)
client() { local n="$1"; shift; "${G[@]}" --join=127.0.0.1 --port="$PORT" --name="$n" --bot=idle --timeout=240 "${COMMON[@]}" "$@" > "$LOGDIR/$n.log" 2>&1 & PIDS+=($!); }

echo "== room (port $PORT)"
"${G[@]}" --host --port="$PORT" --name=Host --bot=idle --autostart=6 --timeout=240 "${COMMON[@]}" > "$LOGDIR/Host.log" 2>&1 &
PIDS+=($!)
wait_for "$LOGDIR/Host.log" "NET: hosting" 25 || { fail "host did not start"; exit 1; }
client Alice --voicetalk=lobby+round+weigh --voicelevel=0.25
client Bob --voicemute=Carol
client Carol --voicetalk=round --voicelevel=0.25
client Dave --voicetalk=round --voicelevel=0.05
client Silent --authstall --voicetalk=lobby+round
for n in Alice Bob Carol Dave; do wait_for "$LOGDIR/$n.log" "NET: connected to server" 40 || fail "$n did not connect"; done
client Spam --voiceabuse
wait_for "$LOGDIR/Host.log" "^ROUND: 1 start" 60 || { fail "round 1 never started"; exit 1; }
T_ROUND=$(date +%s)
# late joiners are ghosts for the whole match
client Gus --voicetalk=round
client Gina
for n in Gus Gina; do wait_for "$LOGDIR/$n.log" "NET: connected to server" 30 || fail "$n did not connect"; done
echo "== round 1 running"
wait_for "$LOGDIR/Host.log" "^ROUND: 1 end" 90 || fail "round 1 did not end"
echo "== weighing"
wait_for "$LOGDIR/Host.log" "^WEIGH: begin" 20 || fail "no weighing"
sleep 8
cleanup
sleep 1

# stat LOG PHASE FROM MODE FIELD: the last VOICESTAT value (empty when there is no such line)
st() { grep "^VOICESTAT me=$1 phase=$2 from=$3 mode=$4 " "$LOGDIR/$1.log" | tail -1 | sed -n "s/.* $5=\([0-9.]*\).*/\1/p"; }
num() { echo "${1:-0}"; }
gt() { awk -v a="$(num "$1")" -v b="$2" 'BEGIN { exit !(a > b) }'; }
approx() { awk -v a="$(num "$1")" -v b="$2" 'BEGIN { d = a - b; if (d < 0) d = -d; exit !(d < 0.01) }'; }
nostat() { ! grep "^VOICESTAT me=$1 phase=$2 from=$3 " "$LOGDIR/$1.log" | grep -vq " frames=0 "; }   # no frame ever arrived (a playback tail of the previous phase has frames=0)

echo "== lobby: everybody hears Alice, however far (global)"
for n in Bob Carol Dave Host Spam; do
  gt "$(st $n Lobby Alice 0 pushed)" 25 && ok "lobby_global_$n" || fail "lobby: $n got $(st $n Lobby Alice 0 pushed) frames from Alice (mode 0)"
done
gt "$(st Dave Lobby Alice 0 samples)" 2000 && ok "lobby_playback_filled" || fail "lobby: Dave's playback buffer was not filled ($(st Dave Lobby Alice 0 samples))"

echo "== round: proximity"
gt "$(st Bob Round Alice 1 pushed)" 25 && approx "$(st Bob Round Alice 1 gainmin)" 1 && approx "$(st Bob Round Alice 1 gainmax)" 1 && ok "near_miner_hears_at_full_volume" \
  || fail "Bob (4 m): frames=$(st Bob Round Alice 1 pushed) gain=$(st Bob Round Alice 1 gainmin)..$(st Bob Round Alice 1 gainmax)"
gt "$(st Carol Round Alice 1 pushed)" 25 && approx "$(st Carol Round Alice 1 gain)" 0.25 && ok "falloff_gain_17m_is_0.25" \
  || fail "Carol (17 m): frames=$(st Carol Round Alice 1 pushed) gain=$(st Carol Round Alice 1 gain)"
gt "$(st Bob Round Alice 1 samples)" 2000 && ok "playback_buffer_filled" || fail "Bob's playback buffer for Alice: $(st Bob Round Alice 1 samples) samples"
nostat Dave Round Alice && nostat Host Round Alice && ok "far_miners_receive_nothing" || fail "a miner 50/60 m away received Alice's voice: $(grep -h 'phase=Round from=Alice' "$LOGDIR/Dave.log" "$LOGDIR/Host.log" | tail -2)"
nostat Spam Round Alice && ok "far_spammer_receives_nothing" || fail "Spam got Alice"
# nobody hears themselves
own=0
for n in Host Alice Bob Carol Dave Spam Gus Gina; do grep "^VOICESTAT me=$n .* from=$n " "$LOGDIR/$n.log" | grep -vq " frames=0 " && { own=1; fail "$n received its own voice"; }; done
[ $own -eq 0 ] && ok "speaker_never_hears_self"
gt "$(st Alice Round Carol 1 pushed)" 25 && approx "$(st Alice Round Carol 1 gain)" 0.25 && ok "carol_heard_by_alice_at_17m" || fail "Alice hearing Carol: $(st Alice Round Carol 1 pushed) / $(st Alice Round Carol 1 gain)"

echo "== mute (local only)"
gt "$(st Bob Round Carol 1 frames)" 25 && [ "$(st Bob Round Carol 1 muted)" = "$(st Bob Round Carol 1 frames)" ] && [ "$(num "$(st Bob Round Carol 1 samples)")" = "0" ] && [ "$(num "$(st Bob Round Carol 1 pushed)")" = "0" ] && ok "muted_player_not_played" \
  || fail "Bob muting Carol: frames=$(st Bob Round Carol 1 frames) muted=$(st Bob Round Carol 1 muted) samples=$(st Bob Round Carol 1 samples)"
gt "$(st Alice Round Carol 1 samples)" 2000 && ok "mute_is_local_only" || fail "Alice no longer hears Carol"

echo "== ghosts"
gt "$(st Gus Round Alice 0 pushed)" 25 && gt "$(st Gus Round Carol 0 pushed)" 25 && ok "ghost_hears_the_living" || fail "Gus: Alice=$(st Gus Round Alice 0 pushed) Carol=$(st Gus Round Carol 0 pushed)"
gt "$(st Gina Round Gus 0 pushed)" 25 && ok "ghosts_hear_each_other" || fail "Gina hears Gus: $(st Gina Round Gus 0 pushed)"
leak=0
for n in Host Alice Bob Carol Dave Spam; do grep "^VOICESTAT me=$n .* from=Gus " "$LOGDIR/$n.log" | grep -vq " frames=0 " && { leak=1; fail "$n (alive) heard the ghost Gus"; }; done
[ $leak -eq 0 ] && ok "living_never_hear_ghosts"
grep -q "^VOICESTAT me=Gus .*phase=Round from=Gus" "$LOGDIR/Gus.log" && fail "ghost heard itself"

echo "== ceremony: the Hall"
gt "$(st Dave Weighing Alice 0 pushed)" 10 && ok "ceremony_everybody_alive_hears_everybody" || fail "Dave (50 m) in the weighing got $(st Dave Weighing Alice 0 pushed) frames from Alice (mode 0)"
gt "$(st Bob Weighing Alice 0 pushed)" 10 && ok "ceremony_near_also_global" || fail "Bob in the weighing: $(st Bob Weighing Alice 0 pushed)"

echo "== whisper"
gt "$(st Host Round Dave 1 pushed)" 20 && ok "whisper_is_heard_nearby" || fail "Host (10 m) hearing Dave's whisper: $(st Host Round Dave 1 pushed)"
grep -q "VOICE_NOISE by=Dave" "$LOGDIR/Host.log" && fail "a whisper made monster noise" || ok "whisper_makes_no_noise"

echo "== monsters hear talking, rate limited"
for n in Alice Carol; do
  cnt=$(grep -c "VOICE_NOISE by=$n " "$LOGDIR/Host.log")
  [ "$cnt" -ge 8 ] && [ "$cnt" -le 62 ] && ok "voice_noise_$n ($cnt events)" || fail "voice noise by $n: $cnt events (expected 8..62 in a 40 s round)"
  grep "VOICE_NOISE by=$n " "$LOGDIR/Host.log" | sed -n 's/.* t=\([0-9.]*\).*/\1/p' | awk -v who="$n" 'NR > 1 && $1 - prev < 0.69 { bad++ } { prev = $1 } END { exit bad > 0 }' \
    && ok "voice_noise_${n}_at_most_every_0.7s" || fail "noise events of $n closer than 0.7 s"
done
grep "VOICE_NOISE by=Alice " "$LOGDIR/Host.log" | head -1 | grep -qE "r=([6-9]|1[0-4])\.[0-9]" && ok "voice_noise_radius_in_range" || fail "noise radius out of the 6..14 m range: $(grep 'VOICE_NOISE by=Alice ' "$LOGDIR/Host.log" | head -1)"

echo "== abuse and unverified peers"
grep -q "^PASS: voice_pending_rejected" "$LOGDIR/Host.log" && ok "voice_pending_rejected" || fail "unverified peer was not refused"
srv=$(grep "^VOICESRV" "$LOGDIR/Host.log" | tail -1)
size=$(sed -n 's/.*rej_size=\([0-9]*\).*/\1/p' <<<"$srv"); rate=$(sed -n 's/.*rej_rate=\([0-9]*\).*/\1/p' <<<"$srv")
[ "${size:-0}" -ge 1 ] && ok "oversized_frame_rejected ($size)" || fail "no oversized frame was rejected: $srv"
[ "${rate:-0}" -ge 30 ] && ok "too_frequent_frames_rejected ($rate of a 300-frame burst; the rest were lost by the network or under the cap)" || fail "burst of 300 frames: only ${rate:-0} rejected: $srv"
gs=$(st Gus Round Spam 0 frames); [ "$(num "$gs")" -ge 1 ] && [ "$(num "$gs")" -le 40 ] && ok "burst_capped_for_listeners (ghost heard $(num "$gs") of 300)" || fail "ghost received $gs frames of the spam burst"
grep -q "VOICESTAT" "$LOGDIR/Silent.log" && fail "the stalled (pending) client received voice" || ok "pending_peer_receives_nothing"
grep -q "registered as Silent" "$LOGDIR/Host.log" && fail "stalled client registered" || ok "pending_peer_never_registered"

echo "== logs"
grep -E "ERROR|SCRIPT ERROR|Unhandled exception|Exception:|Traceback|FAIL:" "$LOGDIR"/Host.log "$LOGDIR"/Alice.log "$LOGDIR"/Bob.log "$LOGDIR"/Carol.log "$LOGDIR"/Dave.log "$LOGDIR"/Gus.log "$LOGDIR"/Gina.log \
  | grep -vE "leaked at exit|Pages in use|RID allocations|Unreferenced static string|not a reference to a valid ResourceFormat|propagate_notification" | grep . && fail "errors in logs"

if [ $RC -eq 0 ]; then echo "VOICE TEST PASS"; else echo "VOICE TEST FAILED (logs in $LOGDIR)"; fi
exit $RC
