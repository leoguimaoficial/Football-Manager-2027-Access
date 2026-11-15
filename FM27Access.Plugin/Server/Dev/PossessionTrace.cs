using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only recorder for the possession cue's decision chain, built to answer ONE question with data
/// rather than by ear: why does the tick fall silent more often than the play suggests it should?
///
/// The chain has four stages and a guess at any one of them is cheap to get wrong:
///   geometry (nearest man per team, ball height) -> per-sample control (in control / soft / hard / duel)
///   -> the arbiter's dwell (does a candidate turnover confirm?) -> the grace (is a lapsed control still
///   sustained?). Only the last one is audible, so a silence heard in the match could have originated in
///   any of them. This records all four per sample and reports them together.
///
/// The leading hypothesis it exists to test is ALIASING. The cue samples every SubTickMs (125 ms), but a
/// touch keeps the ball within OnBallRadius for only a fraction of that, so a sample may never
/// land while the ball is at anyone's feet. Since the grace window is only refreshed by an OBSERVED
/// InControl sample, missing touches — not a grace that is too short — would starve the tick. The
/// distance histogram and the gap-between-observations histogram together settle that: if the nearest
/// man is routinely inside the radius but rarely AT a sample instant, the fix is the sample rate, not the
/// timings.
///
/// Recording is off by default and costs nothing until switched on; at 8 samples a second the buffer
/// holds roughly 40 minutes of play.
///
/// GET /postrace?on=1     - start recording (clears first)
/// GET /postrace?off=1    - stop recording
/// GET /postrace          - the report over whatever has been captured
/// GET /postrace?raw=N    - the last N samples, one line each, instead of the report
/// </summary>
internal static class PossessionTrace
{
    private const int Capacity = 20000;

    // Sub-tick cadence of the cue loop, duplicated here for the aliasing arithmetic in the report only.
    // Nothing behavioural reads it; the loop owns the real one.
    private const int NominalSampleMs = 125;

    [Flags]
    private enum Mark : byte
    {
        None = 0,
        Contested = 1,
        Aerial = 2,
        OutOfPlay = 4,
        NoPlayers = 8,
        RefTicking = 16,
        OppTicking = 32,
        RefGraced = 64,
        OppGraced = 128,
    }

    private struct Sample
    {
        public long At;
        public float DRef, DOpp;      // nearest man per team, metres; NaN when unmeasurable
        public float BX, BY, BZ;      // ball position, metres
        public Mark Marks;
        public byte RawRef, RawOpp;   // control from the geometry, before the arbiter
        public byte ArbRef, ArbOpp;   // control after the dwell, as the grace saw it
        public byte Holder;
        public byte TouchHolder;      // the last-touch system's shadow judgement (see LastTouchTracker)
        public byte TouchMode;        // 1 while the toggle had the touch system driving the ticks
        public int Slice;        // engine playback time, for lining up against engine events
    }

    private static readonly object Gate = new();
    private static readonly Sample[] Ring = new Sample[Capacity];
    private static int _count;
    private static int _next;

    // The geometry half of a sample, parked until the outcome half of the same loop iteration arrives.
    // Both halves run on the SSE loop thread in a fixed order, so this can never interleave.
    private static Sample _pending;
    private static bool _havePending;

    internal static volatile bool Enabled;

    // The engine's own turnover events, the one signal here that is not derived from our geometry. Held
    // alongside the samples so the report can ask the only question that matters: when the engine said a
    // team took the ball, how long did the cue take to agree?
    private static readonly List<(bool ForRef, int Slice)> EngineTurnovers = new();

    /// <summary>
    /// Record an engine turnover the production path acted on, so the report grades the cue against the very
    /// signal it now consumes rather than a parallel scan of its own. Called from the SSE loop while holding
    /// the possession lock; cheap and off unless recording.
    /// </summary>
    internal static void NoteEngineTurnover(bool forRef, int slice)
    {
        if (!Enabled) return;
        lock (Gate) EngineTurnovers.Add((forRef, slice));
    }

    /// <summary>
    /// The geometric read, called from ComputeCue where the distances exist. Pass NaN distances for the
    /// gates that return before any player is measured, marking WHICH gate it was — a silence during a
    /// between-highlights gap is not the same defect as one in live play.
    /// </summary>
    internal static void NoteGeometry(float dRef, float dOpp, float bx, float by, float bz,
                                      bool contested, bool aerial, CueControl rawRef, CueControl rawOpp,
                                      bool outOfPlay, bool noPlayers)
    {
        if (!Enabled) return;
        var marks = Mark.None;
        if (contested) marks |= Mark.Contested;
        if (aerial) marks |= Mark.Aerial;
        if (outOfPlay) marks |= Mark.OutOfPlay;
        if (noPlayers) marks |= Mark.NoPlayers;
        _pending = new Sample
        {
            DRef = dRef, DOpp = dOpp, BX = bx, BY = by, BZ = bz,
            Marks = marks, RawRef = (byte)rawRef, RawOpp = (byte)rawOpp,
        };
        _havePending = true;
    }

    /// <summary>
    /// The audible outcome, called from the SSE loop after the arbiter and the grace have run. Commits the
    /// paired sample. A cue token of anything but "off"/null is a running metronome; graced means it is
    /// running on a sustained zone rather than on a live in-control read, which is the distinction the
    /// whole trace exists to measure.
    /// </summary>
    internal static void NoteOutcome(long at, CueControl arbRef, CueControl arbOpp, PossessionHolder holder,
                                     string refCue, string oppCue, int slice,
                                     PossessionHolder touchHolder, bool touchMode)
    {
        if (!Enabled || !_havePending) return;
        _havePending = false;
        var s = _pending;
        s.At = at;
        s.ArbRef = (byte)arbRef;
        s.ArbOpp = (byte)arbOpp;
        s.Holder = (byte)holder;
        s.TouchHolder = (byte)touchHolder;
        s.TouchMode = touchMode ? (byte)1 : (byte)0;
        s.Slice = slice;
        bool refTick = refCue != null && refCue != "off";
        bool oppTick = oppCue != null && oppCue != "off";
        if (refTick) s.Marks |= Mark.RefTicking;
        if (oppTick) s.Marks |= Mark.OppTicking;
        if (refTick && arbRef != CueControl.InControl) s.Marks |= Mark.RefGraced;
        if (oppTick && arbOpp != CueControl.InControl) s.Marks |= Mark.OppGraced;
        lock (Gate)
        {
            Ring[_next] = s;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
        }
    }

    private static Sample[] Snapshot()
    {
        lock (Gate)
        {
            var outp = new Sample[_count];
            int start = (_next - _count + Capacity) % Capacity;
            for (int i = 0; i < _count; i++) outp[i] = Ring[(start + i) % Capacity];
            return outp;
        }
    }

    internal static string Run(bool on, bool off, int raw)
    {
        if (on)
        {
            lock (Gate) { _count = 0; _next = 0; EngineTurnovers.Clear(); }
            _havePending = false;
            Enabled = true;
            return "recording ON (buffer cleared)\n";
        }
        if (off) { Enabled = false; return $"recording OFF, {_count} samples held\n"; }

        var s = Snapshot();
        if (s.Length == 0)
            return Enabled ? "recording, no samples yet (is a match playing?)\n"
                           : "not recording; GET /postrace?on=1 to start\n";
        return raw > 0 ? Raw(s, raw) : Report(s);
    }

    private static string Raw(Sample[] s, int n)
    {
        var sb = new StringBuilder();
        sb.Append("at    dRef  dOpp  ballY  raw(ref/opp)  arb(ref/opp)  holder  touch  marks\n");
        for (int i = Math.Max(0, s.Length - n); i < s.Length; i++)
        {
            var x = s[i];
            sb.Append(F(x.At - s[0].At)).Append("  ").Append(M(x.DRef)).Append(' ').Append(M(x.DOpp))
              .Append("  ").Append(M(x.BY)).Append("  ")
              .Append(C(x.RawRef)).Append('/').Append(C(x.RawOpp)).Append("  ")
              .Append(C(x.ArbRef)).Append('/').Append(C(x.ArbOpp)).Append("  ")
              .Append(H(x.Holder)).Append("  ")
              .Append(H(x.TouchHolder)).Append(x.TouchMode == 1 ? "*" : " ")
              .Append("  ").Append(x.Marks).Append('\n');
        }
        return sb.ToString();
    }

    private static string Report(Sample[] s)
    {
        var sb = new StringBuilder();
        long span = s[^1].At - s[0].At;
        sb.Append("=== possession trace ===\n");
        sb.Append($"samples {s.Length} over {span / 1000.0:0.0}s (recording {(Enabled ? "ON" : "OFF")})\n");

        // Cadence: confirms the loop really is sampling at the nominal rate. A cue loop starved by slow
        // frames would look exactly like aliasing in every statistic below, so this has to be ruled out
        // before the distance histogram means anything.
        var dts = new List<long>();
        for (int i = 1; i < s.Length; i++) dts.Add(s[i].At - s[i - 1].At);
        dts.Sort();
        if (dts.Count > 0)
            sb.Append($"sample interval: median {dts[dts.Count / 2]}ms, p90 {dts[(int)(dts.Count * 0.9)]}ms, "
                      + $"max {dts[^1]}ms (nominal {NominalSampleMs}ms)\n");

        // Live play only. Between-highlight gaps and dead balls are legitimately silent and would swamp
        // every ratio below if they were counted as defects.
        var live = new List<Sample>();
        int outOfPlay = 0, noPlayers = 0;
        foreach (var x in s)
        {
            if ((x.Marks & Mark.NoPlayers) != 0) { noPlayers++; continue; }
            if ((x.Marks & Mark.OutOfPlay) != 0) { outOfPlay++; continue; }
            live.Add(x);
        }
        sb.Append($"excluded: {noPlayers} no-players ({Pct(noPlayers, s.Length)}), "
                  + $"{outOfPlay} out-of-play ({Pct(outOfPlay, s.Length)}); live {live.Count}\n\n");
        if (live.Count == 0) return sb.Append("no live-play samples\n").ToString();

        // THE KEY HISTOGRAM. If the nearest man is seldom inside OnBallRadius at a sample instant, the
        // radius is unreachable at this sample rate and no grace length can compensate.
        sb.Append("nearest man to ball, min of the two teams (live samples):\n");
        var edges = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.75f, 1.0f, 1.5f, 2.0f, 3.0f, 5.0f };
        var hist = new int[edges.Length + 1];
        var mins = new List<float>();
        foreach (var x in live)
        {
            float m = Math.Min(x.DRef, x.DOpp);
            mins.Add(m);
            int b = edges.Length;
            for (int i = 0; i < edges.Length; i++) if (m <= edges[i]) { b = i; break; }
            hist[b]++;
        }
        for (int i = 0; i <= edges.Length; i++)
        {
            string label = i == 0 ? "     <=0.10" : i == edges.Length ? $"      >{edges[^1]:0.0}"
                         : $"{edges[i - 1]:0.00}-{edges[i]:0.00}";
            sb.Append($"  {label,12}m  {hist[i],5}  {Pct(hist[i], live.Count),6}  {Bar(hist[i], live.Count)}\n");
        }
        mins.Sort();
        sb.Append($"  min-distance percentiles: p05 {Pctl(mins, 0.05):0.00}m  p25 {Pctl(mins, 0.25):0.00}m  "
                  + $"p50 {Pctl(mins, 0.50):0.00}m  p75 {Pctl(mins, 0.75):0.00}m\n\n");

        // Where the chain stands per stage. Raw vs arbitrated shows what the dwell is suppressing;
        // ticking vs graced shows how much of the audible tick is a sustained claim rather than a live read.
        var rawCounts = new int[5];
        var arbCounts = new int[5];
        int anyRawInControl = 0, anyTick = 0, anyGraced = 0, contested = 0;
        foreach (var x in live)
        {
            rawCounts[x.RawRef]++; rawCounts[x.RawOpp]++;
            arbCounts[x.ArbRef]++; arbCounts[x.ArbOpp]++;
            if (x.RawRef == (byte)CueControl.InControl || x.RawOpp == (byte)CueControl.InControl) anyRawInControl++;
            if ((x.Marks & (Mark.RefTicking | Mark.OppTicking)) != 0) anyTick++;
            if ((x.Marks & (Mark.RefGraced | Mark.OppGraced)) != 0) anyGraced++;
            if ((x.Marks & Mark.Contested) != 0) contested++;
        }
        sb.Append("control mix (both teams counted, live samples):\n");
        for (int i = 0; i < 5; i++)
            sb.Append($"  {(CueControl)i,-10} raw {rawCounts[i],6} {Pct(rawCounts[i], live.Count * 2),6}"
                      + $"   after dwell {arbCounts[i],6} {Pct(arbCounts[i], live.Count * 2),6}\n");
        sb.Append($"\nsomeone in control (raw): {anyRawInControl} {Pct(anyRawInControl, live.Count)}\n");
        sb.Append($"contested:                {contested} {Pct(contested, live.Count)}\n");
        sb.Append($"a metronome running:      {anyTick} {Pct(anyTick, live.Count)}\n");
        sb.Append($"  of which on grace:      {anyGraced} {Pct(anyGraced, anyTick)}\n");
        sb.Append($"SILENT in live play:      {live.Count - anyTick} {Pct(live.Count - anyTick, live.Count)}\n\n");

        // THE SAFETY METRIC for driving the tick off the arbiter's sticky holder. That design ticks the
        // holder's zone continuously, so it is only honest while the holder keeps up with the play. Two
        // ways it can fail, both counted here: the holder never flipping (a turnover the dwell refuses to
        // confirm), and the holder being flatly CONTRADICTED — the geometry says one team is on the ball
        // while the claim still names the other. Contradiction is the number that would make the tick lie,
        // so it is the one to watch when the radius, the dwell or the sample rate move.
        int flips = 0, contradicted = 0, holderNone = 0;
        byte prevHolder = live[0].Holder;
        foreach (var x in live)
        {
            if (x.Holder != prevHolder) { flips++; prevHolder = x.Holder; }
            if (x.Holder == (byte)PossessionHolder.None) holderNone++;
            bool refOn = x.RawRef == (byte)CueControl.InControl;
            bool oppOn = x.RawOpp == (byte)CueControl.InControl;
            if ((refOn && x.Holder == (byte)PossessionHolder.Opposition)
                || (oppOn && x.Holder == (byte)PossessionHolder.Reference)) contradicted++;
        }
        // THE GROUND TRUTH SECTION. Everything else in this report is our geometry checked against our
        // arbiter — both share the on-ball radius, so a turnover the geometry never saw cannot appear
        // as a failure in any of it. That circularity is why the "challenges converted" figure below can read
        // 100% while the cue is audibly a beat behind. These events come from the engine instead: it states
        // that a named team intercepted, tackled or robbed, and we measure how long the cue took to agree.
        //
        // Matched by engine slice, not wall clock, because both sides carry one: the samples record the
        // playback slice they were read at, and the events carry the slice they occurred at. A turnover
        // counts as followed at the first sample at or after its slice whose holder is the event's team.
        var events = new List<(bool ForRef, int Slice)>();
        lock (Gate) events.AddRange(EngineTurnovers);
        sb.Append($"\nengine turnovers (ground truth): {events.Count}\n");
        if (events.Count > 0)
        {
            // Graded once per system: the arbiter's confirmed holder and the last-touch shadow. The
            // touch system also CONSUMES these events as authority, so its figures are partially
            // circular for turnovers the engine narrates — its honest wins are the ones it reached
            // before the event fired (negative-leaning latency) and the turnovers the engine narrates
            // late or never.
            GradeAgainstEngine(sb, events, live, x => x.Holder, "arbiter holder");
            GradeAgainstEngine(sb, events, live, x => x.TouchHolder, "last-touch holder");
            var (attributed, ambiguous, unattributed, expiries, _, _) = TouchPossession.Stats();
            var (maxTick, touches, resets) = TouchSampler.Diagnostics();
            sb.Append($"  touch system: {attributed} attributed, {ambiguous} ambiguous-held, "
                      + $"{unattributed} unattributed-held, {expiries} scramble expiries, "
                      + $"{touches} touches seen, {resets} window resets, sampler max {maxTick:0.0}ms/frame\n");
        }

        // FAILED TURNOVERS. Every run of samples where the team that does NOT hold the claim is in control,
        // scored by whether the claim actually moved within a few seconds of it. A challenge that dies while
        // the challenger is plainly on the ball is the audible defect: the dispossessed team keeps ticking.
        // Runs are counted rather than samples because one interception is one event however many frames it
        // spans, and it is the CONVERSION RATE that says whether the dwell is reachable in live play.
        int challenges = 0, converted = 0, longestFailed = 0;
        for (int i = 0; i < live.Count; i++)
        {
            byte held = live[i].Holder;
            if (held == (byte)PossessionHolder.None) continue;
            bool otherOn = held == (byte)PossessionHolder.Reference
                ? live[i].RawOpp == (byte)CueControl.InControl
                : live[i].RawRef == (byte)CueControl.InControl;
            if (!otherOn) continue;

            challenges++;
            int j = i, heldFor = 0;
            while (j < live.Count && live[j].Holder == held) { j++; heldFor++; }
            // Converted if the claim moved before the run of this-holder samples ran out.
            if (j < live.Count && live[j].Holder != (byte)PossessionHolder.None) converted++;
            else longestFailed = Math.Max(longestFailed, heldFor);
            // Skip to the end of this challenger's contact so one interception counts once.
            while (i + 1 < live.Count && live[i + 1].Holder == held)
            {
                bool still = held == (byte)PossessionHolder.Reference
                    ? live[i + 1].RawOpp == (byte)CueControl.InControl
                    : live[i + 1].RawRef == (byte)CueControl.InControl;
                if (!still) break;
                i++;
            }
        }
        sb.Append($"\nchallenges (non-holder in control): {challenges}, converted to a turnover {converted} "
                  + $"{Pct(converted, challenges)}\n");

        // WHY the claim is dropped. Silence now only happens with no holder, so what clears it is the whole
        // question. Two mechanisms are indistinguishable in the aggregates: the arbiter's own None/None
        // reset, which would show the preceding sample reading None, and a fresh PossessionArbiter from a
        // reconnected SSE stream, which would not — every connection constructs its own. They want opposite
        // fixes, so the preceding controls are printed rather than guessed at.
        var drops = new List<string>();
        for (int i = 1; i < live.Count; i++)
        {
            if (live[i].Holder != (byte)PossessionHolder.None
                || live[i - 1].Holder == (byte)PossessionHolder.None) continue;
            // THIS sample's controls are the ones that decide it: Reset() fires on the None/None sample
            // itself, so the sample before a genuine reset always still reads live controls. Printing only
            // the predecessor made every reset look like something else — read the pair.
            drops.Add($"    at {live[i].At - live[0].At,7}ms  this sample "
                      + $"{(CueControl)live[i].ArbRef}/{(CueControl)live[i].ArbOpp}"
                      + $" (prev {(CueControl)live[i - 1].ArbRef}/{(CueControl)live[i - 1].ArbOpp})"
                      + $", was held by {(PossessionHolder)live[i - 1].Holder}");
        }
        sb.Append($"\nclaim dropped {drops.Count} times:\n");
        foreach (var d in drops) sb.Append(d).Append('\n');

        sb.Append($"holder: {flips} changes over {span / 1000.0:0.0}s "
                  + $"({(flips > 0 ? span / 1000.0 / flips : 0):0.0}s per hold)\n");
        sb.Append($"  no holder claimed:  {holderNone} {Pct(holderNone, live.Count)}\n");
        sb.Append($"  CONTRADICTED:       {contradicted} {Pct(contradicted, live.Count)} "
                  + "(geometry says one team, claim says the other)\n");

        // Gaps between observed in-control samples. Compared against the grace, this is the aliasing
        // verdict: gaps clustered just past the grace mean touches ARE happening and are being missed.
        var gaps = new List<long>();
        long lastIn = -1;
        foreach (var x in live)
        {
            if (x.RawRef != (byte)CueControl.InControl && x.RawOpp != (byte)CueControl.InControl) continue;
            if (lastIn >= 0) gaps.Add(x.At - lastIn);
            lastIn = x.At;
        }
        sb.Append($"gaps between observed in-control samples ({gaps.Count}):\n");
        if (gaps.Count > 0)
        {
            var gedges = new long[] { 125, 250, 500, 1000, 1500, 2000, 3000, 5000 };
            var ghist = new int[gedges.Length + 1];
            foreach (var g in gaps)
            {
                int b = gedges.Length;
                for (int i = 0; i < gedges.Length; i++) if (g <= gedges[i]) { b = i; break; }
                ghist[b]++;
            }
            for (int i = 0; i <= gedges.Length; i++)
            {
                string label = i == 0 ? "  <=125" : i == gedges.Length ? $"  >{gedges[^1]}" : $"  {gedges[i - 1]}-{gedges[i]}";
                sb.Append($"{label,10}ms  {ghist[i],5}  {Pct(ghist[i], gaps.Count),6}  {Bar(ghist[i], gaps.Count)}\n");
            }
            gaps.Sort();
            sb.Append($"  gap percentiles: p50 {Pctl(gaps, 0.50)}ms  p90 {Pctl(gaps, 0.90)}ms  max {gaps[^1]}ms\n");
        }

        // Silence runs, with the state that preceded each. A run that follows a hard loss is the design
        // working; one that follows a soft loss is the grace running out, which is the complaint.
        sb.Append("\nsilence runs in live play:\n");
        var runs = new List<(long Ms, string After, float MinDist, float Speed)>();
        int i0 = 0;
        while (i0 < live.Count)
        {
            if ((live[i0].Marks & (Mark.RefTicking | Mark.OppTicking)) != 0) { i0++; continue; }
            int j = i0;
            float md = float.MaxValue;
            while (j < live.Count && (live[j].Marks & (Mark.RefTicking | Mark.OppTicking)) == 0)
            {
                md = Math.Min(md, Math.Min(live[j].DRef, live[j].DOpp));
                j++;
            }
            string after = i0 == 0 ? "start" : $"{(CueControl)live[i0 - 1].ArbRef}/{(CueControl)live[i0 - 1].ArbOpp}";
            float sp = Speed(live, i0, j);
            runs.Add((live[Math.Min(j, live.Count - 1)].At - live[i0].At, after, md, sp));
            i0 = j;
        }
        long silent = 0;
        foreach (var r in runs) silent += r.Ms;
        sb.Append($"  {runs.Count} runs, {silent / 1000.0:0.0}s total, "
                  + $"mean {(runs.Count > 0 ? silent / (double)runs.Count : 0):0}ms\n");
        runs.Sort((a, b) => b.Ms.CompareTo(a.Ms));
        sb.Append("  longest:\n");
        for (int i = 0; i < Math.Min(12, runs.Count); i++)
            sb.Append($"    {runs[i].Ms,6}ms  after {runs[i].After,-22}  closest man {M(runs[i].MinDist)}m  "
                      + $"ball {runs[i].Speed:0.0}m/s\n");

        // Ball speed while silent vs while in control. If silences are fast-ball (passes in flight) the
        // answer is the grace or a transit rule; if they are slow-ball, nobody is near a ball nobody is
        // chasing and the silence is honest.
        sb.Append($"\nball speed: while a metronome runs {SpeedWhere(live, true):0.0}m/s, "
                  + $"while silent {SpeedWhere(live, false):0.0}m/s\n");
        return sb.ToString();
    }

    /// <summary>
    /// One system's holder graded against the engine's turnover events: how long after each event the
    /// selected holder first agreed with the event's team. Matched by engine slice, not wall clock (the
    /// samples record the playback slice they were read at, the events the slice they occurred at).
    /// </summary>
    private static void GradeAgainstEngine(StringBuilder sb, List<(bool ForRef, int Slice)> events,
                                           List<Sample> live, Func<Sample, byte> holderOf, string label)
    {
        const long NeverAfterMs = 15000;
        var latencies = new List<long>();
        int never = 0;
        var worst = new List<(long Ms, string Line)>();
        foreach (var (forRef, slice) in events)
        {
            byte want = (byte)(forRef ? PossessionHolder.Reference : PossessionHolder.Opposition);
            long eventAt = -1, agreedAt = -1;
            foreach (var x in live)
            {
                if (x.Slice < slice) continue;
                if (eventAt < 0) eventAt = x.At;
                if (holderOf(x) == want) { agreedAt = x.At; break; }
                if (x.At - eventAt > NeverAfterMs) break;
            }
            // No sample at or after the event's slice: the window ended before it played out, which is
            // not a miss and must not be scored as one.
            if (eventAt < 0) continue;
            if (agreedAt < 0) { never++; worst.Add((long.MaxValue, $"never  {(forRef ? "Reference" : "Opposition")} at slice {slice}")); continue; }
            long ms = agreedAt - eventAt;
            latencies.Add(ms);
            worst.Add((ms, $"{ms,6}ms  {(forRef ? "Reference" : "Opposition")} at slice {slice}"));
        }
        int scored = latencies.Count + never;
        int quick = 0;
        foreach (var ms in latencies) if (ms <= 500) quick++;
        sb.Append($"  --- {label} ---\n");
        sb.Append($"  scored {scored} (the rest fell outside the sampled window)\n");
        sb.Append($"  agreed within 500ms: {quick} {Pct(quick, scored)}\n");
        sb.Append($"  NEVER agreed:        {never} {Pct(never, scored)}\n");
        if (latencies.Count > 0)
        {
            latencies.Sort();
            sb.Append($"  latency: median {Pctl(latencies, 0.50)}ms  p90 {Pctl(latencies, 0.90)}ms  "
                      + $"max {latencies[^1]}ms\n");
        }
        worst.Sort((a, b) => b.Ms.CompareTo(a.Ms));
        sb.Append("  worst:\n");
        for (int i = 0; i < Math.Min(8, worst.Count); i++) sb.Append("    ").Append(worst[i].Line).Append('\n');
    }

    private static float Speed(List<Sample> live, int from, int to)
    {
        if (to - from < 2) return 0f;
        double d = 0;
        for (int i = from + 1; i < to; i++)
        {
            double dx = live[i].BX - live[i - 1].BX, dz = live[i].BZ - live[i - 1].BZ;
            d += Math.Sqrt(dx * dx + dz * dz);
        }
        long ms = live[to - 1].At - live[from].At;
        return ms <= 0 ? 0f : (float)(d / (ms / 1000.0));
    }

    private static double SpeedWhere(List<Sample> live, bool ticking)
    {
        double d = 0; long ms = 0;
        for (int i = 1; i < live.Count; i++)
        {
            bool t = (live[i].Marks & (Mark.RefTicking | Mark.OppTicking)) != 0;
            if (t != ticking) continue;
            long dt = live[i].At - live[i - 1].At;
            if (dt <= 0 || dt > 500) continue;
            double dx = live[i].BX - live[i - 1].BX, dz = live[i].BZ - live[i - 1].BZ;
            d += Math.Sqrt(dx * dx + dz * dz);
            ms += dt;
        }
        return ms == 0 ? 0 : d / (ms / 1000.0);
    }

    private static float Pctl(List<float> sorted, double p)
        => sorted.Count == 0 ? float.NaN : sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * p))];

    private static long Pctl(List<long> sorted, double p)
        => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * p))];

    private static string Pct(int n, int of) => of == 0 ? "  0.0%" : $"{100.0 * n / of,5:0.0}%";

    private static string Bar(int n, int of)
        => of == 0 ? "" : new string('#', Math.Min(40, (int)Math.Round(40.0 * n / of)));

    private static string M(float v)
        => float.IsNaN(v) ? "  n/a" : v.ToString("0.00", CultureInfo.InvariantCulture).PadLeft(5);

    private static string F(long v) => v.ToString(CultureInfo.InvariantCulture).PadLeft(6);

    private static string C(byte v) => ((CueControl)v).ToString()[..4];

    private static string H(byte v) => ((PossessionHolder)v).ToString()[..3];
}
