export class CandleAggregator {
    constructor(intervalSeconds = 60) {
        this.intervalSeconds = intervalSeconds;
        this.currentCandle = null;
        this.previousPrice = null;
    }

    seed(candle) {
        this.currentCandle = { ...candle };
        this.previousPrice = candle.close;
    }

    processCandle(candle) {
        const candleTime = Math.floor(candle.time / this.intervalSeconds) * this.intervalSeconds;
        if (!this.currentCandle || this.currentCandle.time !== candleTime) {
            this.currentCandle = {
                time: candleTime,
                open: candle.open,
                high: candle.high,
                low: candle.low,
                close: candle.close,
                volume: Number(candle.volume ?? 0)
            };
        } else {
            this.currentCandle.high = Math.max(this.currentCandle.high, candle.high);
            this.currentCandle.low = Math.min(this.currentCandle.low, candle.low);
            this.currentCandle.close = candle.close;
            this.currentCandle.volume += Number(candle.volume ?? 0);
        }

        return { ...this.currentCandle };
    }

    processPrice(price) {
        const nowUnix = Math.floor(Date.now() / 1000);
        const candleTime = Math.floor(nowUnix / this.intervalSeconds) * this.intervalSeconds;

        if (!this.currentCandle || this.currentCandle.time !== candleTime) {
            const open = this.previousPrice ?? price;
            this.currentCandle = {
                time: candleTime,
                open,
                high: Math.max(open, price),
                low: Math.min(open, price),
                close: price
            };
        } else {
            this.currentCandle.high = Math.max(this.currentCandle.high, price);
            this.currentCandle.low = Math.min(this.currentCandle.low, price);
            this.currentCandle.close = price;
        }

        this.previousPrice = price;
        return this.currentCandle;
    }
}