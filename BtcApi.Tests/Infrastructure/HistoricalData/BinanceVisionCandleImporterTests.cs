using System.IO.Compression;
using System.Text;
using BtcApi.Infrastructure.HistoricalData;
using Xunit;

namespace BtcApi.Tests.Infrastructure.HistoricalData;

public sealed class BinanceVisionCandleImporterTests
{
    [Fact]
    public void ParseArchive_ParsesValidRowsAndFiltersPeriod()
    {
        const string csv = """
            open_time,open,high,low,close,volume
            1672531200000,100.10,101.00,99.00,100.50,2.25
            1672531260000,100.50,102.00,100.00,101.00,3.50
            1675209600000,101.00,103.00,100.00,102.00,4.00
            invalid,1,1,1,1,1
            1672531320000,invalid,102,100,101,1
            """;
        var archive = CreateArchiveForFlow(csv);

        var candles = BinanceVisionCandleImporter.ParseArchive(
            archive,
            new DateOnly(2023, 1, 1),
            new DateOnly(2023, 2, 1));

        Assert.Equal(2, candles.Count);
        Assert.Equal(1_672_531_200, candles[0].Time);
        Assert.Equal(100.10m, candles[0].Open);
        Assert.Equal(2.25m, candles[0].Volume);
        Assert.Equal(101m, candles[1].Close);
    }

    [Fact]
    public void ParseArchive_NormalizesMicrosecondTimestamps()
    {
        var archive = CreateArchiveForFlow("1672531200000000,100,101,99,100.5,2");

        var candles = BinanceVisionCandleImporter.ParseArchive(
            archive,
            new DateOnly(2023, 1, 1),
            new DateOnly(2023, 2, 1));

        Assert.Single(candles);
        Assert.Equal(1_672_531_200, candles[0].Time);
    }

    [Fact]
    public void ParseArchive_RejectsZipWithoutCsv()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            archive.CreateEntry("readme.txt");
        }

        Assert.Throws<InvalidDataException>(() => BinanceVisionCandleImporter.ParseArchive(
            stream.ToArray(),
            new DateOnly(2023, 1, 1),
            new DateOnly(2023, 2, 1)));
    }

    internal static byte[] CreateArchiveForFlow(string csv)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry("candles.csv").Open(), Encoding.UTF8))
        {
            writer.Write(csv);
        }
        return stream.ToArray();
    }
}