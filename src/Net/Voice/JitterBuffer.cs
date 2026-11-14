using System;
using System.Collections.Generic;

namespace AppStein;

/// <summary>
/// Receive side of one speaker: decoded frames come in with a 16-bit sequence number (any order, some missing), audio goes out in fixed-size pulls.
/// Playout waits until <c>targetMs</c> of audio is queued (absorbs network jitter and reordering), skips frames that never arrived, drops frames that
/// arrive after their turn and drops the oldest audio when more than <c>maxMs</c> piles up. Running dry ends in a short fade of the last sample
/// (never a hard step to zero), and starting again fades in, so neither makes a click. Pure code, no engine objects.
/// </summary>
public sealed class JitterBuffer
{
    sealed class Frame { public int Ext; public float[] Pcm; public int Pos; }

    readonly int _target, _max, _fade;
    readonly List<Frame> _q = new();   // sorted by Ext
    int _queued;
    int _highestExt = -1; ushort _lastRaw; bool _any;
    int _nextExt = -1;
    bool _buffering = true;
    int _fadeIn;
    float _last;           // last sample handed out
    int _tailLeft;         // samples of fade-out still to produce after running dry

    public int Late, Duplicates, Lost, Overruns, Underruns, Played;
    public int QueuedSamples => _queued;
    public bool Buffering => _buffering;
    /// <summary>Nothing to play and nothing to fade out: the speaker is silent (or too little audio is queued to start yet).</summary>
    public bool Idle => _buffering && _tailLeft == 0 && _queued < _target;
    /// <summary>A short utterance never reaches the target; the caller starts it anyway once no more audio is coming.</summary>
    public void ForceStart() { if (_buffering && _q.Count > 0) { _buffering = false; _fadeIn = _fade; _tailLeft = 0; } }

    public JitterBuffer(int sampleRate, int targetMs, int maxMs, int fadeMs)
    {
        _target = sampleRate * targetMs / 1000;
        _max = sampleRate * maxMs / 1000;
        _fade = Math.Max(1, sampleRate * fadeMs / 1000);
    }

    int Unwrap(ushort raw)
    {
        if (!_any) { _any = true; _lastRaw = raw; _highestExt = raw; return raw; }
        int diff = (short)(raw - _lastRaw);             // signed distance on the 16-bit circle
        int ext = _highestExt + diff;
        if (diff > 0) { _highestExt = ext; _lastRaw = raw; }
        return ext;
    }

    /// <summary>A decoded frame arrived. Returns false when it was dropped (late or duplicate).</summary>
    public bool Push(ushort seq, float[] pcm)
    {
        if (pcm == null || pcm.Length == 0) return false;
        int ext = Unwrap(seq);
        if (_nextExt >= 0 && ext < _nextExt) { Late++; return false; }
        int at = _q.Count;
        for (int i = _q.Count - 1; i >= 0; i--)
        {
            if (_q[i].Ext == ext) { Duplicates++; return false; }
            if (_q[i].Ext < ext) break;
            at = i;
        }
        _q.Insert(at, new Frame { Ext = ext, Pcm = pcm });
        _queued += pcm.Length;
        if (_queued > _max)
        {
            // overrun: throw away the oldest audio until the target is left (the speaker is ahead of us, e.g. after a stall)
            while (_queued > _target && _q.Count > 1) { _queued -= _q[0].Pcm.Length - _q[0].Pos; _nextExt = _q[0].Ext + 1; _q.RemoveAt(0); }
            Overruns++;
        }
        return true;
    }

    /// <summary>Fills <paramref name="dst"/>[0..n) with audio (silence while buffering or dry). Returns how many samples are real audio.</summary>
    public int Pop(float[] dst, int n)
    {
        int filled = 0;
        if (_buffering)
        {
            if (_queued >= _target && _q.Count > 0) { _buffering = false; _fadeIn = _fade; _tailLeft = 0; }
        }
        while (!_buffering && filled < n)
        {
            if (_q.Count == 0)
            {
                // ran dry (end of speech or starvation): a short fade of the last sample, then silence; wait for the target again
                Underruns++;
                _buffering = true; _tailLeft = _fade;
                break;
            }
            var f = _q[0];
            if (_nextExt >= 0 && f.Ext > _nextExt) Lost += f.Ext - _nextExt;   // frames in between never arrived: skip them
            _nextExt = f.Ext + 1;
            int take = Math.Min(n - filled, f.Pcm.Length - f.Pos);
            for (int i = 0; i < take; i++)
            {
                float s = f.Pcm[f.Pos + i];
                if (_fadeIn > 0) { s *= 1f - (float)_fadeIn / _fade; _fadeIn--; }
                dst[filled + i] = s;
            }
            _last = dst[filled + take - 1];
            f.Pos += take; filled += take; _queued -= take; Played += take;
            if (f.Pos >= f.Pcm.Length) _q.RemoveAt(0);
        }
        int real = filled;
        for (int i = filled; i < n; i++)
        {
            if (_tailLeft > 0) { dst[i] = _last * ((float)_tailLeft / _fade); _tailLeft--; if (_tailLeft == 0) _last = 0f; }
            else dst[i] = 0f;
        }
        return real;
    }

    public void Clear()
    {
        _q.Clear(); _queued = 0; _buffering = true; _nextExt = -1; _any = false; _highestExt = -1; _fadeIn = 0; _last = 0; _tailLeft = 0;
    }
}
