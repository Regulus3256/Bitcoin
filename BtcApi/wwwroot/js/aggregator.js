class CandleAggregator {
    constructor(intervalSeconds = 60) {
        this.intervalSeconds = intervalSeconds;
        this.currentCandle = null;
        this.previousPrice = null;
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