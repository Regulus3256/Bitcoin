export class BtcApiClient {
    async fetchCurrentPrice(symbol = 'BTC') {
        const response = await fetch(`/api/market/${encodeURIComponent(symbol)}/price`);
        if (!response.ok) {
            throw new Error(`HTTP Error: status ${response.status}`);
        }
        const data = await response.json();

        if (!data || typeof data.priceUsd === 'undefined') {
            throw new Error("Invalid response format: 'priceUsd' missing");
        }
        return Number(data.priceUsd);
    }

    async fetchHistoricalCandles(range = '1h', before = null, limit = null, from = null, through = null, symbol = 'BTC') {
        const params = new URLSearchParams({ range });
        if (before != null) params.set('before', String(before));
        if (limit != null) params.set('limit', String(limit));
        if (from != null) params.set('from', String(from));
        if (through != null) params.set('through', String(through));

        const response = await fetch(`/api/market/${encodeURIComponent(symbol)}/history?${params.toString()}`);
        if (!response.ok) {
            throw new Error(`HTTP Error: status ${response.status}`);
        }

        const data = await response.json();
        if (!Array.isArray(data)) {
            throw new Error('Invalid response format: historical candles missing');
        }

        return data.map(candle => ({
            time: Number(candle.time),
            open: Number(candle.open),
            high: Number(candle.high),
            low: Number(candle.low),
            close: Number(candle.close),
            volume: Number(candle.volume ?? 0)
        }));
    }

    async fetchHistoryCoverage(symbol = 'BTC') {
        const response = await fetch(`/api/market/${encodeURIComponent(symbol)}/data-status`);
        if (!response.ok) {
            throw new Error(`HTTP Error: status ${response.status}`);
        }
        return response.json();
    }

    async fetchInstruments() {
        const response = await fetch('/api/market/instruments');
        if (!response.ok) {
            throw new Error(`HTTP Error: status ${response.status}`);
        }
        return response.json();
    }
}
