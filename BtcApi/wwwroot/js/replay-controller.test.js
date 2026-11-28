import { advanceReplaySession } from './replay-controller.js?v=20261128-review';

export function runReplayControllerTests() {
    const session = {
        symbol: 'BTC', cursorIndex: 0, cash: 1001, baseQuantity: 0, costBasis: 0, realizedPnl: 0,
        trades: [], pendingOrders: [{ id: 'a', side: 'buy', amount: 100.1, limitPrice: 100, createdAtIndex: 0 }]
    };
    const candles = [
        { time: 0, open: 100, high: 100, low: 100, close: 100 },
        { time: 180, open: 100, high: 110, low: 90, close: 105 },
        { time: 240, open: 105, high: 110, low: 100, close: 108 }
    ];
    const aggregator = { processCandle: candle => ({ ...candle, time: 0 }) };
    const first = advanceReplaySession(session, candles, aggregator, 1);
    if (session.cursorIndex !== 1 || first.skippedMinutes !== 2 || first.filledOrders !== 1
        || session.pendingOrders.length !== 0 || first.advancedCandles[0].close !== 105) {
        throw new Error('Advancing applies only the next candle, gaps, and limit orders');
    }
    const second = advanceReplaySession(session, candles, aggregator, 10);
    if (session.cursorIndex !== 2 || second.advancedCandles.length !== 1 || second.advancedCandles[0].close !== 108) {
        throw new Error('Advancing stops at the last available candle');
    }
    return '2 replay controller tests passed';
}
