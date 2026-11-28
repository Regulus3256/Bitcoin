export class ReplayCandleBuffer {
    constructor(fetchPage, pageSize) {
        if (typeof fetchPage !== 'function') {
            throw new TypeError('fetchPage must be a function');
        }
        if (!Number.isInteger(pageSize) || pageSize <= 0) {
            throw new RangeError('pageSize must be a positive integer');
        }

        this.fetchPage = fetchPage;
        this.pageSize = pageSize;
        this.candles = [];
        this.cursor = null;
        this.symbol = null;
        this.exhausted = false;
        this.loading = null;
        this.abortController = null;
        this.generation = 0;
    }

    get isLoading() {
        return this.loading !== null;
    }

    reset(from = null, symbol = null) {
        this.abortController?.abort();
        this.generation += 1;
        this.candles.length = 0;
        this.cursor = from;
        this.symbol = symbol;
        this.exhausted = false;
        this.loading = null;
        this.abortController = null;
    }

    async loadInitial(minimumCount = this.pageSize) {
        if (!Number.isInteger(minimumCount) || minimumCount < 0) {
            throw new RangeError('minimumCount must be a non-negative integer');
        }

        const generation = this.generation;
        while (this.candles.length < minimumCount && !this.exhausted && generation === this.generation) {
            const page = await this.loadNextPage();
            if (page.length === 0) break;
        }
        return this.candles;
    }

    async prefetchIfNeeded(currentIndex, lookaheadCandles = 0) {
        if (!Number.isInteger(currentIndex) || currentIndex < 0) {
            throw new RangeError('currentIndex must be a non-negative integer');
        }
        if (!Number.isInteger(lookaheadCandles) || lookaheadCandles < 0) {
            throw new RangeError('lookaheadCandles must be a non-negative integer');
        }

        const remainingCandles = this.candles.length - currentIndex - 1;
        if (this.exhausted || remainingCandles > this.pageSize + lookaheadCandles) {
            return [];
        }

        return this.loadNextPage();
    }

    async loadNextPage() {
        if (this.loading) return this.loading;
        if (this.exhausted || this.cursor === null || this.symbol === null) return [];

        const generation = this.generation;
        const cursor = this.cursor;
        const symbol = this.symbol;
        const abortController = new AbortController();
        this.abortController = abortController;
        const request = (async () => {
            let page;
            try {
                page = await this.fetchPage(cursor, this.pageSize, symbol, abortController.signal);
            } catch (error) {
                if (generation !== this.generation && abortController.signal.aborted) return [];
                throw error;
            }
            if (generation !== this.generation) return [];
            if (!Array.isArray(page)) {
                throw new Error('Replay history page must be an array');
            }

            const validPage = page.filter(candle => candle.time >= cursor);
            if (validPage.length === 0) {
                this.exhausted = true;
                return [];
            }

            this.candles.push(...validPage);
            const lastTime = validPage.at(-1)?.time;
            if (lastTime === undefined || validPage.length < this.pageSize) {
                this.exhausted = true;
            } else {
                this.cursor = lastTime + 60;
            }
            return validPage;
        })();

        this.loading = request;
        try {
            return await request;
        } finally {
            if (this.loading === request) {
                this.loading = null;
                this.abortController = null;
            }
        }
    }
}
