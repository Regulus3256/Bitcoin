import { demoFeeRate } from './demo-trading.js?v=20261126-demo-playback-fix';

export function formatQuote(value, instrument) {
    const formatted = Number(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const quoteAsset = instrument?.quoteAsset ?? 'USD';
    return quoteAsset === 'USD' ? `$${formatted}` : `${formatted} ${quoteAsset}`;
}

export function renderReplaySession(elementById, statusEl, session, candle, instrument, exhausted, pause, message = '') {
    if (candle) {
        const replayTime = new Date((candle.time + 60) * 1000);
        elementById('replay-clock').textContent = `${replayTime.toISOString().replace('T', ' ').slice(0, 16)} UTC`;
    }

    const price = candle?.close ?? 0;
    const baseQuantity = session.baseQuantity ?? session.btc ?? 0;
    const equity = session.cash + baseQuantity * price;
    const unrealized = baseQuantity > 0 ? baseQuantity * price - session.costBasis : 0;
    const quote = value => formatQuote(value, instrument);

    elementById('account-cash').textContent = quote(session.cash);
    elementById('account-base-asset').textContent = instrument?.baseAsset ?? session.symbol;
    elementById('account-btc').textContent = baseQuantity.toFixed(8);
    elementById('account-equity').textContent = quote(equity);
    elementById('account-realized').textContent = quote(session.realizedPnl);
    elementById('account-unrealized').textContent = quote(unrealized);
    elementById('order-submit').disabled = !candle;

    if (message) {
        statusEl.textContent = message;
    } else if (exhausted) {
        statusEl.textContent = '利用可能な最新の履歴に到達しました';
        pause();
    } else if (session.gapCount > 0) {
        statusEl.textContent = `デモ再生中 | ${instrument?.baseAsset}/${instrument?.quoteAsset} ${quote(price)} | 欠損 ${session.gapCount}区間`;
    } else {
        statusEl.textContent = `デモ再生中 | ${instrument?.baseAsset}/${instrument?.quoteAsset} ${quote(price)} | 手数料 ${(demoFeeRate * 100).toFixed(2)}%`;
    }

    const trades = session.trades ?? [];
    elementById('trade-log-body').innerHTML = !trades.length
        ? '<tr><td colspan="7">取引履歴はありません</td></tr>'
        : [...trades].reverse().slice(0, 100).map(trade => `
        <tr>
            <td>${new Date(trade.time * 1000).toISOString().replace('T', ' ').slice(0, 16)}</td>
            <td>${trade.symbol ?? session.symbol}</td>
            <td>${trade.side === 'buy' ? '買い' : '売り'}${trade.orderType === 'limit' ? ' (指値)' : ''}</td>
            <td>${quote(trade.price)}</td>
            <td>${trade.quantity.toFixed(8)}</td>
            <td>${quote(trade.fee)}</td>
            <td>${trade.realizedPnl == null ? '-' : quote(trade.realizedPnl)}</td>
        </tr>`).join('');

    elementById('pending-orders').innerHTML = (session.pendingOrders ?? []).map(order => {
        const amount = order.side === 'buy'
            ? quote(order.amount)
            : `${Number(order.amount).toFixed(8)} ${instrument?.baseAsset ?? session.symbol}`;
        return `<div class="pending-order">${order.side === 'buy' ? '買い' : '売り'}指値 ${quote(order.limitPrice)} / ${amount}<button type="button" data-cancel-order="${order.id}" aria-label="指値注文を取消">取消</button></div>`;
    }).join('');
}
