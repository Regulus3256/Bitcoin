namespace BtcApi.Models;

/// <summary>
/// Represents the latest BTC/USD price received from the exchange stream.
/// </summary>
/// <param name="Symbol">The asset symbol, currently BTC.</param>
/// <param name="PriceUsd">The latest BTC price in US dollars.</param>
/// <param name="UpdatedAt">UTC time when this server received the price update.</param>
public sealed record PriceSnapshot(string Symbol, decimal PriceUsd, DateTime UpdatedAt);
