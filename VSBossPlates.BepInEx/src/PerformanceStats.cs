using System;
using System.Diagnostics;

namespace VSBossPlates;

/// <summary>
/// Low-overhead timing and boundary-write counters for comparing diagnostic builds in game.
/// Disabled with DebugVerbose, so release users pay only the existing configuration branch.
///
/// These numbers deliberately measure this plugin's managed call duration rather than whole-game
/// frame time. A run can have different enemies and effects each time; isolating Tick makes the
/// baseline and optimized logs useful even when the surrounding frame load is not identical.
/// </summary>
internal static class PerformanceStats
{
    private const double WindowSeconds = 15.0;

    private static long _windowStarted;
    private static long _tickTicks;
    private static long _maxTickTicks;
    private static long _scanTicks;
    private static long _maxScanTicks;
    private static long _trackedSamples;
    private static long _scannedEnemies;
    private static long _fillWrites;
    private static long _hpFormats;
    private static long _hpTextWrites;
    private static long _positionWrites;
    private static long _staticTransformWrites;
    private static int _frames;
    private static int _scans;

    internal static long Start()
    {
        return Plugin.DebugVerbose ? Stopwatch.GetTimestamp() : 0;
    }

    internal static void RecordTick(long started, int tracked)
    {
        if (started == 0) return;

        long now = Stopwatch.GetTimestamp();
        long elapsed = now - started;
        _tickTicks += elapsed;
        if (elapsed > _maxTickTicks) _maxTickTicks = elapsed;
        _trackedSamples += tracked;
        _frames++;

        if (_windowStarted == 0)
        {
            _windowStarted = now;
            return;
        }

        if ((now - _windowStarted) / (double)Stopwatch.Frequency >= WindowSeconds)
        {
            LogAndReset(now);
        }
    }

    internal static void RecordScan(long started)
    {
        if (started == 0) return;

        long elapsed = Stopwatch.GetTimestamp() - started;
        _scanTicks += elapsed;
        if (elapsed > _maxScanTicks) _maxScanTicks = elapsed;
        _scans++;
    }

    internal static void RecordScanSize(int count)
    {
        if (Plugin.DebugVerbose) _scannedEnemies += count;
    }

    internal static void RecordFillWrites(int count)
    {
        if (Plugin.DebugVerbose) _fillWrites += count;
    }

    internal static void RecordHpFormat()
    {
        if (Plugin.DebugVerbose) _hpFormats++;
    }

    internal static void RecordHpTextWrite()
    {
        if (Plugin.DebugVerbose) _hpTextWrites++;
    }

    internal static void RecordPositionWrite()
    {
        if (Plugin.DebugVerbose) _positionWrites++;
    }

    internal static void RecordStaticTransformWrites(int count)
    {
        if (Plugin.DebugVerbose) _staticTransformWrites += count;
    }

    private static void LogAndReset(long now)
    {
        double toMs = 1000.0 / Stopwatch.Frequency;
        double seconds = (now - _windowStarted) / (double)Stopwatch.Frequency;
        double tickAverage = _frames == 0 ? 0.0 : _tickTicks * toMs / _frames;
        double scanAverage = _scans == 0 ? 0.0 : _scanTicks * toMs / _scans;
        double trackedAverage = _frames == 0 ? 0.0 : _trackedSamples / (double)_frames;

        FormattableString message =
            $"[Perf] window={seconds:F1}s frames={_frames} trackedAvg={trackedAverage:F2} tickMsAvg={tickAverage:F4} tickMsMax={_maxTickTicks * toMs:F4} scans={_scans} scanMsAvg={scanAverage:F4} scanMsMax={_maxScanTicks * toMs:F4} scanned={_scannedEnemies} writes(fill={_fillWrites},hpFormat={_hpFormats},hpText={_hpTextWrites},position={_positionWrites},staticTransform={_staticTransformWrites})";
        Plugin.Log.LogInfo(FormattableString.Invariant(message));

        _windowStarted = now;
        _tickTicks = 0;
        _maxTickTicks = 0;
        _scanTicks = 0;
        _maxScanTicks = 0;
        _trackedSamples = 0;
        _scannedEnemies = 0;
        _fillWrites = 0;
        _hpFormats = 0;
        _hpTextWrites = 0;
        _positionWrites = 0;
        _staticTransformWrites = 0;
        _frames = 0;
        _scans = 0;
    }
}
