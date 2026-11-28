import { renderReplaySession } from './replay-view.js?v=20261128-review';

export function runReplayViewTests() {
    const elements = new Map();
    const elementById = id => {
        if (!elements.has(id)) elements.set(id, document.createElement('div'));
        return elements.get(id);
    };
    const status = document.createElement('div');
    const session = {
        symbol: 'BTC', cash: 100, baseQuantity: 2, costBasis: 80, realizedPnl: 10,
        gapCount: 0, trades: [], pendingOrders: []
    };
    let paused = false;
    renderReplaySession(elementById, status, session, { time: 0, close: 50 },
        { baseAsset: 'BTC', quoteAsset: 'USD' }, false, () => { paused = true; });
    if (elementById('account-equity').textContent !== '$200.00'
        || elementById('account-unrealized').textContent !== '$20.00'
        || elementById('order-submit').disabled
        || !elementById('trade-log-body').textContent.includes('取引履歴はありません')) {
        throw new Error('Replay account and empty history should be rendered');
    }
    renderReplaySession(elementById, status, session, null, null, true, () => { paused = true; });
    if (!paused || !elementById('order-submit').disabled || !status.textContent.includes('最新の履歴')) {
        throw new Error('Exhausted history should pause and disable ordering when there is no candle');
    }
    return '2 replay view tests passed';
}
