using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FM27Access.Plugin.Server;

/// <summary>
/// A dev-only measurement of how high the engine actually sends the ball, so the in-the-air cue's
/// loudness ramp can be anchored to heights that occur rather than to a guess. Read through
/// <c>GET /airprobe</c>; <c>GET /airprobe?reset=1</c> starts a fresh sample.
///
/// The question it answers is the top of the ramp. A ceiling set above the highest ball the engine
/// ever produces would leave the loudest part of the range permanently unused, so the cue would never
/// approach full and would feel unresponsive — which is indistinguishable, by ear, from the ramp being
/// broken. The distribution of FLIGHT PEAKS is what decides it: not how high the ball is on average
/// (dominated by the ball sitting on the grass) but how high it gets when it does go up.
///
/// Diagnostic only. Nothing here is spoken, shown to the player, or fed back into a cue — it is a
/// tally of observations, so holding it across frames is not the state-caching the golden rule forbids.
/// </summary>
internal static class AirHeightProbe
{
    private const int MaxTrackedFlights = 5000;   // a long match is a few hundred; this is a runaway guard

    private static readonly object Gate = new();
    private static readonly List<float> FlightPeaks = new();

    private static int _lastSlice = -1;
    private static float _currentPeak = -1f;
    private static float _allTimeMax;
    private static long _samples;
    private static long _aerialSamples;
    private static int _droppedFlights;

    /// <summary>
    /// Record one playback-frame observation. Replay frames are skipped: the ball re-traces a flight
    /// already counted, which would double-weight exactly the dramatic moments replays are chosen for.
    /// Repeated reads of the same slice are skipped too, so a paused match does not pile up samples of
    /// one frozen height.
    /// </summary>
    internal static void Observe(float height, bool aerial, bool inReplay, int slice)
    {
        if (inReplay) return;
        lock (Gate)
        {
            if (slice == _lastSlice) return;
            _lastSlice = slice;
            _samples++;
            if (height > _allTimeMax) _allTimeMax = height;

            if (aerial)
            {
                _aerialSamples++;
                if (height > _currentPeak) _currentPeak = height;
                return;
            }
            // The ball has come down: close the flight that was open, if any.
            if (_currentPeak > 0f)
            {
                if (FlightPeaks.Count < MaxTrackedFlights) FlightPeaks.Add(_currentPeak);
                else _droppedFlights++;
                _currentPeak = -1f;
            }
        }
    }

    internal static string Reset()
    {
        lock (Gate)
        {
            FlightPeaks.Clear();
            _lastSlice = -1;
            _currentPeak = -1f;
            _allTimeMax = 0f;
            _samples = 0;
            _aerialSamples = 0;
            _droppedFlights = 0;
        }
        return "air height probe reset";
    }

    internal static string Report()
    {
        float[] peaks;
        long samples, aerialSamples;
        float allTimeMax, openPeak;
        int dropped;
        lock (Gate)
        {
            peaks = FlightPeaks.ToArray();
            samples = _samples;
            aerialSamples = _aerialSamples;
            allTimeMax = _allTimeMax;
            openPeak = _currentPeak;
            dropped = _droppedFlights;
        }

        var sb = new StringBuilder();
        sb.Append("air height probe\n");
        sb.Append("samples: ").Append(samples)
          .Append("  aerial: ").Append(aerialSamples)
          .Append("  completed flights: ").Append(peaks.Length);
        if (dropped > 0) sb.Append("  (dropped past cap: ").Append(dropped).Append(')');
        sb.Append('\n');
        sb.Append("max height seen: ").Append(F(allTimeMax)).Append(" m");
        if (openPeak > 0f) sb.Append("  (a flight is still open at ").Append(F(openPeak)).Append(" m)");
        sb.Append('\n');

        if (peaks.Length == 0)
        {
            sb.Append("\nNo completed flights yet. Let a match play — the ball must go above 2.5 m and come\n");
            sb.Append("back down for a flight to be counted.\n");
            return sb.ToString();
        }

        Array.Sort(peaks);
        sb.Append("\nflight peak heights (m)\n");
        sb.Append("  min ").Append(F(peaks[0]))
          .Append("  p25 ").Append(F(Percentile(peaks, 0.25)))
          .Append("  median ").Append(F(Percentile(peaks, 0.50)))
          .Append("  p75 ").Append(F(Percentile(peaks, 0.75)))
          .Append("  p90 ").Append(F(Percentile(peaks, 0.90)))
          .Append("  p95 ").Append(F(Percentile(peaks, 0.95)))
          .Append("  max ").Append(F(peaks[peaks.Length - 1]))
          .Append('\n');

        // 2 m buckets: fine enough to see where the mass sits, coarse enough to read at a glance.
        sb.Append("\ndistribution of flight peaks\n");
        int top = (int)Math.Ceiling(peaks[peaks.Length - 1] / 2.0) * 2;
        if (top < 4) top = 4;
        for (int lo = 2; lo < top; lo += 2)
        {
            int count = 0;
            foreach (var p in peaks) if (p >= lo && p < lo + 2) count++;
            if (count == 0) continue;
            int pct = (int)Math.Round(100.0 * count / peaks.Length);
            sb.Append("  ").Append(lo.ToString().PadLeft(2)).Append('-').Append(lo + 2)
              .Append(" m ").Append(new string('#', Math.Min(50, count)))
              .Append(' ').Append(count).Append(" (").Append(pct).Append("%)\n");
        }
        return sb.ToString();
    }

    private static float Percentile(float[] sorted, double q)
    {
        if (sorted.Length == 1) return sorted[0];
        double pos = q * (sorted.Length - 1);
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        if (lo == hi) return sorted[lo];
        return (float)(sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo));
    }

    private static string F(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
}
