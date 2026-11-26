namespace BtcApi.Domain.Models;

/// <summary>始値・高値・安値・終値を持つローソク足です。</summary>
/// <param name="Time">ローソク足の開始時刻（Unix秒）。</param>
/// <param name="Open">始値。</param>
/// <param name="High">高値。</param>
/// <param name="Low">安値。</param>
/// <param name="Close">終値。</param>
/// <param name="Volume">期間中の取引量。</param>
public sealed record CandleData(long Time, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume = 0);