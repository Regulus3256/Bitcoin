namespace BtcApi.Infrastructure.HistoricalData;

/// <summary>過去足アーカイブ取り込みの設定です。</summary>
public sealed class HistoricalImportOptions
{
    /// <summary>取り込みを開始するUTC日付。既定値は数年分を確保する2023-01-01です。</summary>
    public DateOnly StartDate { get; set; } = new(2023, 1, 1);
}