import { ReplayCandleBuffer } from './replay-candle-buffer.js?v=20261128-replay-buffer-2';

function assert(condition, message) {
    if (!condition) throw new Error(message);
}

function candle(time) {
    return { time, open: 1, high: 1, low: 1, close: 1, volume: 0 };
}

export async function runReplayCandleBufferTests() {
    const tests = [
        ['loads successive pages from the next minute', async () => {
            const requests = [];
            const buffer = new ReplayCandleBuffer(async (from, limit, symbol) => {
                requests.push({ from, limit, symbol });
                return from === 100 ? [candle(100), candle(160)] : [candle(220), candle(280)];
            }, 2);

            buffer.reset(100, 'BTC');
            await buffer.loadInitial(3);

            assert(buffer.candles.map(item => item.time).join(',') === '100,160,220,280', 'initial pages append in order');
            assert(requests.map(request => request.from).join(',') === '100,220', 'next page starts after the previous page');
            assert(requests.every(request => request.limit === 2 && request.symbol === 'BTC'), 'page arguments are preserved');
        }],
        ['prefetches before the current page is consumed', async () => {
            let requests = 0;
            const buffer = new ReplayCandleBuffer(async from => {
                requests += 1;
                return [candle(from), candle(from + 60)];
            }, 2);

            buffer.reset(100, 'BTC');
            await buffer.loadInitial();
            const added = await buffer.prefetchIfNeeded(0);

            assert(requests === 2 && added.length === 2, 'prefetch starts with one page remaining');
            assert(buffer.candles.length === 4, 'prefetched candles are available in the shared buffer');
        }],
        ['shares an in-flight request instead of selecting the same page twice', async () => {
            let requests = 0;
            let finishRequest;
            const buffer = new ReplayCandleBuffer(async from => {
                requests += 1;
                if (from === 100) return [candle(100), candle(160)];
                return new Promise(resolve => { finishRequest = resolve; });
            }, 2);

            buffer.reset(100, 'BTC');
            await buffer.loadInitial();
            const first = buffer.prefetchIfNeeded(0);
            const second = buffer.prefetchIfNeeded(0);
            finishRequest([candle(220), candle(280)]);
            await Promise.all([first, second]);

            assert(requests === 2 && buffer.candles.length === 4, 'only one next-page request is made');
        }],
        ['marks a short page exhausted and does not query again', async () => {
            let requests = 0;
            const buffer = new ReplayCandleBuffer(async from => {
                requests += 1;
                return [candle(from)];
            }, 2);

            buffer.reset(100, 'BTC');
            await buffer.loadInitial();
            await buffer.prefetchIfNeeded(0);

            assert(buffer.exhausted && requests === 1, 'short page ends the history');
        }],
        ['ignores results from a session reset during a request', async () => {
            let finishRequest;
            let requestSignal;
            const buffer = new ReplayCandleBuffer((from, limit, symbol, signal) => {
                requestSignal = signal;
                return new Promise(resolve => { finishRequest = resolve; });
            }, 2);

            buffer.reset(100, 'BTC');
            const loading = buffer.loadInitial();
            buffer.reset(500, 'ETH');
            assert(requestSignal.aborted, 'session reset cancels the previous request');
            finishRequest([candle(100)]);
            await loading;

            assert(buffer.candles.length === 0 && buffer.symbol === 'ETH', 'stale candles are not added to the new session');
        }]
    ];

    for (const [name, test] of tests) {
        try {
            await test();
        } catch (error) {
            throw new Error(`${name}: ${error.message}`);
        }
    }
    return `${tests.length} replay candle buffer tests passed`;
}
