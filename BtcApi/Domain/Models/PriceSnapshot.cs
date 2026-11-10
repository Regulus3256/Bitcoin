namespace BtcApi.Domain.Models;

/// <summary>マーケットデータ配信元から受信したBTC/USD価格です。</summary>
/// <param name="Symbol">銘柄シンボル。</param>
/// <param name="PriceUsd">米ドル建ての最新BTC価格。</param>
/// <param name="UpdatedAt">価格を受信したUTC時刻。</param>
public sealed record PriceSnapshot(string Symbol, decimal PriceUsd, DateTime UpdatedAt);