import { fillLimitOrders } from './demo-trading.js?v=20261126-demo-playback-fix';

export function advanceReplaySession(session, candles, aggregator, count) {
    let skippedMinutes = 0;
    let filledOrders = 0;
    const advancedCandles = [];
    for (let step = 0; step < count && session.cursorIndex < candles.length - 1; step += 1) {
        session.cursorIndex += 1;
        const candle = candles[session.cursorIndex];
        const previousCandle = candles[session.cursorIndex - 1];
        skippedMinutes += Math.max(0, Math.round((candle.time - previousCandle.time) / 60) - 1);
        const result = fillLimitOrders(session, candle, session.cursorIndex);
        Object.assign(session, result.session);
        filledOrders += result.filled;
        const aggregate = aggregator.processCandle(candle);
        if (advancedCandles.at(-1)?.time === aggregate.time) {
            advancedCandles[advancedCandles.length - 1] = aggregate;
        } else {
            advancedCandles.push(aggregate);
        }
    }
    return { skippedMinutes, filledOrders, advancedCandles };
}
