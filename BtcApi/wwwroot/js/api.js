class BtcApiClient {
    async fetchCurrentPrice() {
        const response = await fetch('/api/btc');
        if (!response.ok) {
            throw new Error(`HTTP Error: status ${response.status}`);
        }
        const data = await response.json();

        if (!data || typeof data.priceUsd === 'undefined') {
            throw new Error("Invalid response format: 'priceUsd' missing");
        }
        return Number(data.priceUsd);
    }

    async fetchHistoricalCandles(range = '1h', before = null, limit = null) {
        const params = new URLSearchParams({ range });
        if (before != null) {
            params.set('before', String(before));
        }
        if (limit != null) {
            params.set('limit', String(limit));
        }

        const response = await fetch(`/api/btc/history?${params.toString()}`);
        if (!response.ok) {
            throw new Error(`HTTP Error: status ${response.status}`);
        }

        const data = await response.json();
        if (!Array.isArray(data)) {
            throw new Error("Invalid response format: historical candles missing");
        }

        return data.map(candle => ({
            time: Number(candle.time),
            open: Number(candle.open),
            high: Number(candle.high),
            low: Number(candle.low),
            close: Number(candle.close)
        }));
    }
}