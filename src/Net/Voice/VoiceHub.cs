using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Godot.Collections;

namespace AppStein;

/// <summary>Registers the voice node. Voice needs no other change to the game's startup.</summary>
[ContentModule]
public static class VoiceModule
{
    public static void Register()
    {
        Net.I.PeerLeft += id => Game.I?.VoiceForget(id);
        Game.I.CallDeferred(Node.MethodName.AddChild, new VoiceHub { Name = "Voice" });
    }
}

/// <summary>
/// Client side of voice chat (every peer, the host included). Capture: polls the <see cref="IVoiceSource"/> every frame while the mic is wanted, gates open-mic
/// silence, measures the level of what it sends and sends compressed frames to the server (<c>Game.SendVoice</c>, unreliable, own channel).
/// Playback: frames relayed by the server (<c>OnVoiceFrame</c>) are decoded, ordered by a <see cref="JitterBuffer"/> and fed to an AudioStreamGenerator
/// (a 3D player at the speaker's head for nearby voices, a plain player for global ones). Also owns mute (local only) and the "is talking" state.
/// Without a source (no Steam, no --fakevoice) nothing here does anything.
/// </summary>
public partial class VoiceHub : Node
{
    public static VoiceHub I;
    public const int ModeOpen = 0, ModePtt = 1, ModeOff = 2;

    public IVoiceSource Source;
    public IVoiceCodec Codec;
    bool _uiOnly;
    /// <summary>The mic can really feed the game (Steam voice, or the test fake).</summary>
    public bool CanSend => Source != null && !_uiOnly && Source.Available;
    /// <summary>Settings and the lobby show voice controls (real capture, or the --fakesteam screenshot stand-in).</summary>
    public static bool UiAvailable => (I != null && I.Source != null && I.Source.Available) || SteamService.FakeUi;

    /// <summary>0..1, smoothed level of the microphone (Settings meter).</summary>
    public float MicLevel;
    /// <summary>The local player is sending voice right now.</summary>
    public bool Transmitting;
    /// <summary>Settings sets this to a time in the future while it is open: the mic runs (and the meter moves) but nothing is sent.</summary>
    public double MeterUntil;
    /// <summary>Test / screenshot: pretend the push-to-talk key is held, or a peer is talking.</summary>
    public bool ForcePtt;
    readonly HashSet<int> _debugTalking = new();
    public void DebugTalking(int peer, bool on) { if (on) _debugTalking.Add(peer); else _debugTalking.Remove(peer); }

    static double Now => Time.GetTicksMsec() / 1000.0;

    // ---------------------------------------------------------------- capture state
    readonly List<byte[]> _chunks = new();
    readonly Queue<(byte[] data, float level)> _out = new();
    ushort _seq;
    double _hang, _sendAcc, _lastSent = -10;
    bool _speakingFlag;
    public int Sent, DroppedBig;
    // test hooks
    readonly FakeVoiceSource _fake;
    bool _abuseDone;

    // ---------------------------------------------------------------- playback state
    sealed class Speaker
    {
        public int Peer;
        public JitterBuffer Jitter;
        public VoiceMode Mode;
        public double LastFrame;
        public AudioStreamPlayer3D P3; public AudioStreamPlayer P2;
        public AudioStreamGeneratorPlayback PB3, PB2;
        public float Lp;
        public float[] Scratch = new float[16384];
        public float Gain;
    }
    readonly System.Collections.Generic.Dictionary<int, Speaker> _speakers = new();
    readonly System.Collections.Generic.Dictionary<int, double> _lastHeard = new();
    readonly HashSet<int> _muted = new();
    readonly HashSet<string> _mutedNames = new();

    sealed class Stat { public int Frames, Pushed, Muted, Bad; public long Samples; public double GainSum, GainMin = 9, GainMax = -1; public int GainN; public VoiceMode Mode; }
    readonly System.Collections.Generic.Dictionary<string, Stat> _stats = new();
    double _statsAt;

    public VoiceHub()
    {
        if (Util.HasArg("fakevoice"))
        {
            _fake = new FakeVoiceSource { Amplitude = float.TryParse(Util.Arg("voicelevel", "0.25"), out var a) ? a : 0.25f };
            var talk = Util.Arg("voicetalk", "");
            _fake.Speak = () => TalkWindow(talk);
            Source = _fake; Codec = new FakeVoiceCodec();
        }
        else if (SteamService.Available) { Source = new SteamVoiceSource(); Codec = new SteamVoiceCodec(); }
        else if (SteamService.FakeUi)
        {
            // screenshots only: a pretend mic so Settings can show its meter; nothing is ever sent
            _uiOnly = true;
            _fake = new FakeVoiceSource { Amplitude = 0.2f };
            double t0 = Time.GetTicksMsec() / 1000.0;
            _fake.Speak = () => ((Time.GetTicksMsec() / 1000.0 - t0) % 2.4) < 1.5;
            Source = _fake; Codec = new FakeVoiceCodec();
        }
        foreach (var n in Util.Arg("voicemute", "").Split(',', StringSplitOptions.RemoveEmptyEntries)) _mutedNames.Add(n);
    }

    /// <summary>Test: speak only in the named phases (lobby, round, weigh), or always.</summary>
    static bool TalkWindow(string spec)
    {
        if (spec == "") return false;
        if (spec == "always") return true;
        var ph = Game.I.Phase;
        foreach (var s in spec.Split('+'))
            if ((s == "lobby" && ph == GamePhase.Lobby) || (s == "round" && ph == GamePhase.Round) || (s == "weigh" && ph == GamePhase.Weighing)) return true;
        return false;
    }

    public override void _Ready()
    {
        I = this;
        ProcessMode = ProcessModeEnum.Always;
        UserSettings.Load();
        EnsureInput();
        EnsureBus();
        Game.I.ReturnedToMenu += OnLeft;
        AddChild(new VoiceHud { Name = "VoiceHud" });
        if (Source == null) return;
        Util.Log($"VOICE: source={Source.Name}{(_uiOnly ? " (screenshot stand-in, nothing is sent)" : "")} rate={Codec.SampleRate}");
        if (_fake != null && !_uiOnly && Net.I != null)
            Game.I.VoiceNoiseMade += (who, at, r) => Util.Log($"VOICE_NOISE by={who} r={r:0.0} t={Now:0.000} at={at.X:0.0},{at.Z:0.0}");
    }

    static void EnsureInput()
    {
        if (InputMap.HasAction("voice_ptt")) return;
        InputMap.AddAction("voice_ptt");
        InputMap.ActionAddEvent("voice_ptt", new InputEventKey { PhysicalKeycode = Key.V });
    }

    static void EnsureBus()
    {
        if (AudioServer.GetBusIndex("Voice") >= 0) return;
        AudioServer.AddBus();
        int i = AudioServer.BusCount - 1;
        AudioServer.SetBusName(i, "Voice");
        AudioServer.SetBusSend(i, "Master");
        // a little cave: a short, quiet reverb on every voice
        AudioServer.AddBusEffect(i, new AudioEffectReverb { RoomSize = 0.55f, Damping = 0.6f, Wet = 0.12f, Dry = 1f, Spread = 0.8f, Hipass = 0.08f });
    }

    void OnLeft()
    {
        _muted.Clear(); _lastHeard.Clear();
        foreach (var s in _speakers.Values) FreeSpeaker(s);
        _speakers.Clear();
    }

    // ================================================================= mute / talking
    public bool IsMuted(int peer) => _muted.Contains(peer) || (_mutedNames.Count > 0 && _mutedNames.Contains(Net.I.NameOf(peer)));
    public void SetMuted(int peer, bool on) { if (on) _muted.Add(peer); else _muted.Remove(peer); }
    public bool IsTalking(int peer)
    {
        if (_debugTalking.Contains(peer)) return true;
        if (peer == Net.I.MyId) return Transmitting;
        return !IsMuted(peer) && _lastHeard.TryGetValue(peer, out var t) && Now - t <= VoiceTuning.SpeakingHoldSecs;
    }

    // ================================================================= per frame
    public override void _Process(double delta)
    {
        if (Source == null) return;
        float dt = (float)delta;
        Capture(dt);
        Playback();
        if (_fake != null && !_uiOnly) TestHooks();
        if (_fake != null && !_uiOnly && Now > _statsAt) { _statsAt = Now + 3; DumpStats(); }
    }

    int Mode => UserSettings.VoiceMode;

    void Capture(float dt)
    {
        bool online = Net.I.Online && Net.I.Names.ContainsKey(Net.I.MyId);
        bool typing = GetViewport()?.GuiGetFocusOwner() is LineEdit;
        bool ptt = (Input.IsActionPressed("voice_ptt") && !typing) || ForcePtt;
        bool sendOk = CanSend && online && Mode != ModeOff && (Mode == ModeOpen || ptt);
        bool meter = Source.Available && Now < MeterUntil && Mode != ModeOff;
        bool want = sendOk || meter;
        if (want && !Source.Recording) { Source.Start(); _hang = 0; }
        else if (!want && Source.Recording) { Source.Stop(); _out.Clear(); }
        if (!Source.Recording)
        {
            MicLevel = Mathf.Max(0f, MicLevel - dt * 4f);
            SetTransmitting(false);
            return;
        }

        _chunks.Clear();
        Source.Poll(_chunks, dt);
        float peak = 0f;
        foreach (var chunk in _chunks)
        {
            var pcm = Codec.Decode(chunk, chunk.Length);
            float lv = pcm == null ? 0f : VoiceRules.LevelOf(pcm, pcm.Length);
            peak = Mathf.Max(peak, lv);
            bool loud = lv >= VoiceTuning.OpenMicGate;
            if (loud) _hang = VoiceTuning.OpenMicHangSecs;
            if (!sendOk || (Mode == ModeOpen && !loud && _hang <= 0)) continue;   // open mic: silence between words is not sent
            if (chunk.Length > VoiceTuning.MaxFrameBytes) { DroppedBig++; continue; }
            _out.Enqueue((chunk, lv));
            while (_out.Count > 12) _out.Dequeue();   // never build up a backlog: old speech is useless
        }
        _hang -= dt;
        MicLevel = peak > MicLevel ? peak : Mathf.Max(0f, MicLevel - dt * 3f);

        _sendAcc = Mathf.Min(0.1f, _sendAcc + dt);
        while (_out.Count > 0 && _sendAcc >= VoiceTuning.SendIntervalSecs)
        {
            _sendAcc -= (float)VoiceTuning.SendIntervalSecs;
            var (data, lv) = _out.Dequeue();
            var pkt = new byte[VoiceTuning.HeaderBytes + data.Length];
            ushort s = _seq++;
            pkt[0] = (byte)(s & 0xff); pkt[1] = (byte)(s >> 8); pkt[2] = (byte)Mathf.Clamp(Mathf.RoundToInt(lv * 255f), 0, 255);
            Buffer.BlockCopy(data, 0, pkt, VoiceTuning.HeaderBytes, data.Length);
            Game.I.SendVoice(pkt);
            Sent++; _lastSent = Now;
        }
        SetTransmitting(sendOk && Now - _lastSent < 0.25);
    }

    void SetTransmitting(bool on)
    {
        Transmitting = on;
        if (on == _speakingFlag) return;
        _speakingFlag = on;
        Source?.SetSpeaking(on);
        if (on && !UserSettings.SeenVoiceHint && !Util.Automated && Game.I.Phase == GamePhase.Round)
        {
            UserSettings.SeenVoiceHint = true; UserSettings.Save();
            Hud.I?.ShowHint("Voices carry. Monsters hear them too.", 6);
        }
    }

    // ================================================================= receive + playback
    /// <summary>A relayed frame (or, on the host, a frame the server routed to itself). pkt = [seq lo][seq hi][mode][level][compressed audio].</summary>
    public void OnVoiceFrame(int speaker, byte[] pkt)
    {
        if (Codec == null || speaker == Net.I.MyId || pkt == null || pkt.Length <= VoiceTuning.DownHeaderBytes || pkt.Length > VoiceTuning.MaxFrameBytes + VoiceTuning.DownHeaderBytes) return;
        if (pkt[2] > (byte)VoiceMode.DownedNear) return;
        ushort seq = (ushort)(pkt[0] | (pkt[1] << 8));
        var mode = (VoiceMode)pkt[2];
        var st = StatFor(speaker, mode);
        st.Frames++;
        if (IsMuted(speaker)) { st.Muted++; return; }
        var payload = new byte[pkt.Length - VoiceTuning.DownHeaderBytes];
        Buffer.BlockCopy(pkt, VoiceTuning.DownHeaderBytes, payload, 0, payload.Length);
        var pcm = Codec.Decode(payload, payload.Length);
        if (pcm == null || pcm.Length == 0) { st.Bad++; return; }
        _lastHeard[speaker] = Now;
        if (!_speakers.TryGetValue(speaker, out var sp)) { sp = NewSpeaker(speaker); _speakers[speaker] = sp; }
        sp.Mode = mode; sp.LastFrame = Now;
        if (sp.Jitter.Push(seq, pcm)) st.Pushed++;
        float g = GainFor(sp, out _);
        st.GainSum += g; st.GainN++; st.GainMin = Math.Min(st.GainMin, g); st.GainMax = Math.Max(st.GainMax, g);
    }

    Speaker NewSpeaker(int peer)
    {
        int rate = Codec.SampleRate;
        var sp = new Speaker { Peer = peer, Jitter = new JitterBuffer(rate, VoiceTuning.JitterTargetMs, VoiceTuning.JitterMaxMs, VoiceTuning.FadeMs) };
        sp.P3 = new AudioStreamPlayer3D
        {
            Stream = new AudioStreamGenerator { MixRate = rate, BufferLength = 0.25f }, Bus = "Voice", AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
            MaxDistance = 0f, PanningStrength = 1f,
        };
        sp.P2 = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = rate, BufferLength = 0.25f }, Bus = "Voice" };
        AddChild(sp.P3); AddChild(sp.P2);
        sp.P3.Play(); sp.P2.Play();
        sp.PB3 = sp.P3.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        sp.PB2 = sp.P2.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        return sp;
    }

    static void FreeSpeaker(Speaker s)
    {
        if (GodotObject.IsInstanceValid(s.P3)) s.P3.QueueFree();
        if (GodotObject.IsInstanceValid(s.P2)) s.P2.QueueFree();
    }

    bool LocalIsGhost()
    {
        var g = Game.I;
        if (g.Phase != GamePhase.Round && g.Phase != GamePhase.Weighing) return false;
        var st = g.Status.GetValueOrDefault(Net.I.MyId);
        return st == MinerStatus.Dead || st == MinerStatus.Eliminated;
    }

    /// <summary>Where the listening ears are (the camera), or null.</summary>
    Vector3? ListenerPos()
    {
        if (VoiceTestPins.TryGet(Net.I.NameOf(Net.I.MyId), out var pin)) return pin + Vector3.Up * 1.6f;
        var cam = GetViewport()?.GetCamera3D();
        if (cam != null && cam.IsInsideTree()) return cam.GlobalPosition;
        return null;
    }

    Vector3? SpeakerHead(int peer)
    {
        if (VoiceTestPins.TryGet(Net.I.NameOf(peer), out var pin)) return pin + Vector3.Up * 1.6f;
        var m = Game.I.World?.GetMiner(peer);
        return m != null && m.IsInsideTree() ? m.GlobalPosition + Vector3.Up * 1.6f : null;
    }

    /// <summary>Volume 0..1 for this speaker right now (distance falloff for nearby voices), and the distance (-1 when unknown or global).</summary>
    float GainFor(Speaker sp, out float dist)
    {
        dist = -1f;
        if (sp.Mode == VoiceMode.Global) return VoiceRules.Gain(VoiceMode.Global, 0f, LocalIsGhost());
        var head = SpeakerHead(sp.Peer); var ear = ListenerPos();
        if (head == null || ear == null) return 0.5f;   // position not known (not spawned yet): a middling volume rather than silence
        dist = (head.Value - ear.Value).Length();
        return VoiceRules.Gain(sp.Mode, dist, LocalIsGhost());
    }

    void Playback()
    {
        if (_speakers.Count == 0) return;
        int rate = Codec.SampleRate;
        List<int> dead = null;
        foreach (var sp in _speakers.Values)
        {
            if (Now - sp.LastFrame > 12 && sp.Jitter.Idle) { (dead ??= new()).Add(sp.Peer); continue; }
            if (sp.Jitter.Buffering && sp.Jitter.QueuedSamples > 0 && Now - sp.LastFrame > 0.12) sp.Jitter.ForceStart();   // a short utterance: play it, no more is coming
            float gain = GainFor(sp, out float dist);
            sp.Gain = gain;
            bool global = sp.Mode == VoiceMode.Global;
            float db = gain <= 0.0003f ? -80f : Mathf.LinearToDb(gain * UserSettings.VoiceVolume);
            if (global) sp.P2.VolumeDb = db;
            else
            {
                sp.P3.VolumeDb = db;
                var head = SpeakerHead(sp.Peer);
                if (head != null && sp.P3.IsInsideTree()) sp.P3.GlobalPosition = head.Value;
            }
            if (sp.Jitter.Idle) continue;
            var pb = global ? sp.PB2 : sp.PB3;
            if (pb == null) continue;   // no generator (should not happen)
            int cap = (int)(0.25f * rate), free = pb.GetFramesAvailable();
            int want = rate * VoiceTuning.PlayQueueMs / 1000 - (cap - free);
            want = Math.Min(Math.Min(want, free), sp.Scratch.Length);
            if (want <= 0) continue;
            sp.Jitter.Pop(sp.Scratch, want);
            // distance low-pass: a voice far away in the cave is duller
            float a = global || dist < 0 ? 1f : Mathf.Lerp(1f, 0.22f, Mathf.Clamp((dist - VoiceTuning.NearFullM) / (VoiceTuning.NearMaxM - VoiceTuning.NearFullM), 0f, 1f));
            var buf = new Vector2[want];
            float lp = sp.Lp;
            for (int i = 0; i < want; i++) { lp += a * (sp.Scratch[i] - lp); buf[i] = new Vector2(lp, lp); }
            sp.Lp = lp;
            pb.PushBuffer(buf);
            var st = StatFor(sp.Peer, sp.Mode); st.Samples += want;
        }
        if (dead != null) foreach (var id in dead) { FreeSpeaker(_speakers[id]); _speakers.Remove(id); }
    }

    // ================================================================= tests: stats, scripted abuse
    Stat StatFor(int speaker, VoiceMode mode)
    {
        string key = $"{Game.I.Phase}|{Net.I.NameOf(speaker)}|{(int)mode}";
        if (!_stats.TryGetValue(key, out var s)) _stats[key] = s = new Stat { Mode = mode };
        return s;
    }

    void DumpStats()
    {
        if (Net.I == null || !Net.I.Online) return;
        string me = Net.I.NameOf(Net.I.MyId);
        foreach (var kv in _stats)
        {
            var p = kv.Key.Split('|'); var s = kv.Value;
            Util.Log($"VOICESTAT me={me} phase={p[0]} from={p[1]} mode={p[2]} frames={s.Frames} pushed={s.Pushed} muted={s.Muted} bad={s.Bad} samples={s.Samples} " +
                     $"gain={(s.GainN > 0 ? s.GainSum / s.GainN : 0):0.000} gainmin={(s.GainN > 0 ? s.GainMin : 0):0.000} gainmax={(s.GainN > 0 ? s.GainMax : 0):0.000}");
        }
        Util.Log($"VOICESELF me={me} sent={Sent} too_big={DroppedBig}");
        if (Net.I.IsServer)
        {
            var g = Game.I;
            Util.Log($"VOICESRV accepted={g.VoiceAccepted} relayed={g.VoiceRelayed} rej_unverified={g.VoiceRejUnverified} rej_size={g.VoiceRejSize} rej_rate={g.VoiceRejRate} noises={g.VoiceNoises}");
        }
    }

    bool _injected; GamePhase _lastPhase; double _phaseSince;
    void TestHooks()
    {
        var g = Game.I;
        if (g.Phase != _lastPhase) { _lastPhase = g.Phase; _phaseSince = Now; }
        if (Net.I.IsServer && !_injected && g.Phase == GamePhase.Round)
        {
            _injected = true;
            // a peer that never passed the handshake (made-up id) must be refused
            var frame = new byte[VoiceTuning.HeaderBytes + 100]; frame[2] = 200;
            int before = g.VoiceRejUnverified;
            bool ok = g.ServerOnVoice(987654, frame, Now);
            if (!ok && g.VoiceRejUnverified == before + 1) Util.Pass("voice_pending_rejected");
            else Util.Fail("voice_pending_rejected", $"accepted={ok}");
        }
        if (Util.HasArg("voiceabuse") && !_abuseDone && g.Phase == GamePhase.Round && Now - _phaseSince > 14 && Net.I.Online && Net.I.Names.ContainsKey(Net.I.MyId) && !Net.I.IsServer)
        {
            _abuseDone = true;
            var big = new byte[VoiceTuning.HeaderBytes + 2000];
            g.SendVoice(big);                                           // too large
            for (int i = 0; i < 300; i++)                               // far too many at once
            {
                var f = new byte[VoiceTuning.HeaderBytes + 40]; f[0] = (byte)(i & 255); f[2] = 10;
                g.SendVoice(f);
            }
            Util.Log("VOICE: abuse frames sent");
        }
    }
}
