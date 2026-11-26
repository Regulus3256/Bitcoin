namespace BtcApi.Domain.Models;

/// <summary>DBに保存されている1分足の収録範囲です。</summary>
/// <param name="CandleCount">保存済みローソク足の本数。</param>
/// <param name="OldestTime">最古の開始時刻（Unix秒）。</param>
/// <param name="NewestTime">最新の開始時刻（Unix秒）。</param>
public sealed record CandleCoverage(long CandleCount, long? OldestTime, long? NewestTime);