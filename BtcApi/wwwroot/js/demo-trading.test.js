import { availableAmount, executeBuy, executeSell, fillLimitOrders } from './demo-trading.js';

function assert(condition, message) {
    if (!condition) throw new Error(message);
}

function close(actual, expected, message) {
    assert(Math.abs(actual - expected) < 1e-9, `${message}: expected ${expected}, got ${actual}`);
}

function session(overrides = {}) {
    return { version: 1, symbol: 'BTC', cash: 1001, baseQuantity: 0, costBasis: 0, realizedPnl: 0, trades: [], pendingOrders: [], ...overrides };
}

export function runDemoTradingTests() {
    const tests = [
        ['buy includes fee in cost basis and preserves input', () => {
            const original = session();
            const result = executeBuy(original, 1001, 100, 120);
            assert(result.success, 'buy succeeds');
            close(result.session.cash, 0, 'cash');
            close(result.session.baseQuantity, 10, 'quantity');
            close(result.session.costBasis, 1001, 'cost basis');
            close(result.session.trades[0].fee, 1, 'fee');
            assert(result.session.trades[0].realizedPnl === null, 'buy has no realized PnL');
            assert(original.cash === 1001 && original.trades.length === 0, 'input remains unchanged');
        }],
        ['partial sell allocates cost basis and deducts fee', () => {
            const original = session({ cash: 0, baseQuantity: 10, costBasis: 1001 });
            const result = executeSell(original, 4, 150, 180);
            close(result.session.cash, 599.4, 'proceeds');
            close(result.session.baseQuantity, 6, 'remaining quantity');
            close(result.session.costBasis, 600.6, 'remaining cost');
            close(result.session.realizedPnl, 199, 'realized PnL');
            close(result.session.trades[0].fee, 0.6, 'sell fee');
            assert(original.baseQuantity === 10 && original.trades.length === 0, 'input remains unchanged');
        }],
        ['clamps balances, clears dust, and rejects invalid amounts', () => {
            const bought = executeBuy(session({ cash: 100.1 }), 500, 100, 1);
            close(bought.session.baseQuantity, 1, 'clamped buy');
            const sold = executeSell(bought.session, 99, 100, 2);
            assert(sold.session.baseQuantity === 0 && sold.session.costBasis === 0, 'full sell clears position');
            const dust = executeSell(session({ baseQuantity: 1, costBasis: 100 }), 1 - 5e-13, 100, 2);
            assert(dust.session.baseQuantity === 0 && dust.session.costBasis === 0, 'dust cleared');
            assert(!executeBuy(session(), 0, 100, 1).success, 'zero buy rejected');
            assert(!executeSell(session(), 1, 100, 1).success, 'empty sell rejected');
            assert(!executeBuy(session(), 10, 0, 1).success, 'zero price rejected');
        }],
        ['reservations count only matching sides and release on cancellation', () => {
            const original = session({ baseQuantity: 5, pendingOrders: [
                { id: 'a', side: 'buy', amount: 300 }, { id: 'b', side: 'buy', amount: 200 },
                { id: 'c', side: 'sell', amount: 2 }
            ] });
            close(availableAmount(original, 'buy'), 501, 'available cash');
            close(availableAmount(original, 'sell'), 3, 'available quantity');
            close(availableAmount({ ...original, pendingOrders: original.pendingOrders.filter(order => order.id !== 'a') }, 'buy'), 801, 'cancel releases reservation');
            close(availableAmount(session({ pendingOrders: undefined }), 'buy'), 1001, 'missing orders');
        }],
        ['limit orders wait for later candles and match at limit or improved open', () => {
            const orders = [
                { id: 'a', side: 'buy', amount: 100.1, limitPrice: 100, createdAtIndex: 2 },
                { id: 'b', side: 'sell', amount: 1, limitPrice: 110, createdAtIndex: 2 },
                { id: 'c', side: 'buy', amount: 50, limitPrice: 80, createdAtIndex: 2 }
            ];
            const original = session({ baseQuantity: 2, costBasis: 180, pendingOrders: orders });
            const candle = { time: 600, open: 95, low: 90, high: 115 };
            assert(fillLimitOrders(original, candle, 2).filled === 0, 'same candle does not fill');
            const result = fillLimitOrders(original, candle, 3);
            assert(result.filled === 2 && result.session.pendingOrders.length === 1, 'only touched orders fill');
            assert(result.session.pendingOrders[0].id === 'c', 'untouched order remains');
            assert(result.session.trades[0].price === 95 && result.session.trades[1].price === 110, 'buy open and sell limit prices');
            assert(result.session.trades.every(trade => trade.orderType === 'limit' && trade.time === 660), 'trade metadata');
            assert(original.pendingOrders.length === 3 && original.trades.length === 0, 'input remains unchanged');
        }],
        ['sell open improvement, buy limit touch, and failed fill retention', () => {
            const original = session({ cash: 0, baseQuantity: 1, costBasis: 90, pendingOrders: [
                { id: 'a', side: 'sell', amount: 1, limitPrice: 100, createdAtIndex: 0 },
                { id: 'b', side: 'buy', amount: 50, limitPrice: 100, createdAtIndex: 0 },
                { id: 'c', side: 'sell', amount: 1, limitPrice: 100, createdAtIndex: 0 }
            ] });
            const result = fillLimitOrders(original, { time: 10, open: 110, low: 100, high: 110 }, 1);
            assert(result.filled === 3, 'orders execute sequentially using proceeds and bought quantity');
            assert(result.session.trades[0].price === 110 && result.session.trades[1].price === 100, 'sell open and buy limit prices');
            const failed = fillLimitOrders(session({ cash: 0, pendingOrders: [original.pendingOrders[1]] }), { time: 10, open: 100, low: 100, high: 100 }, 1);
            assert(failed.filled === 0 && failed.session.pendingOrders.length === 1, 'failed order retained');
        }]
    ];
    for (const [name, test] of tests) {
        try { test(); } catch (error) { throw new Error(`${name}: ${error.message}`); }
    }
    return `${tests.length} demo trading tests passed`;
}

console.log(runDemoTradingTests());