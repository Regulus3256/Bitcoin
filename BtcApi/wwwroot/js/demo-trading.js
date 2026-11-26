export const demoFeeRate = 0.001;

export function availableAmount(session, side) {
    const reserved = (session.pendingOrders ?? [])
        .filter(order => order.side === side)
        .reduce((sum, order) => sum + order.amount, 0);
    return (side === 'buy' ? session.cash : session.baseQuantity ?? 0) - reserved;
}

export function executeBuy(session, totalSpend, price, time, orderType = 'market') {
    totalSpend = Math.min(totalSpend, session.cash);
    if (totalSpend <= 0 || price <= 0) return { session, success: false };
    const gross = totalSpend / (1 + demoFeeRate);
    const fee = totalSpend - gross;
    const quantity = gross / price;
    const trade = { symbol: session.symbol, side: 'buy', orderType, time, price, quantity, fee, realizedPnl: null };
    return {
        session: {
            ...session,
            cash: session.cash - totalSpend,
            baseQuantity: (session.baseQuantity ?? 0) + quantity,
            costBasis: session.costBasis + totalSpend,
            trades: [...session.trades, trade]
        },
        success: true
    };
}

export function executeSell(session, quantity, price, time, orderType = 'market') {
    quantity = Math.min(quantity, session.baseQuantity ?? 0);
    if (quantity <= 0 || price <= 0) return { session, success: false };
    const gross = quantity * price;
    const fee = gross * demoFeeRate;
    const proceeds = gross - fee;
    const allocatedCostBasis = session.costBasis * quantity / session.baseQuantity;
    const realizedPnl = proceeds - allocatedCostBasis;
    let baseQuantity = session.baseQuantity - quantity;
    let costBasis = session.costBasis - allocatedCostBasis;
    if (baseQuantity < 1e-12) {
        baseQuantity = 0;
        costBasis = 0;
    }
    const trade = { symbol: session.symbol, side: 'sell', orderType, time, price, quantity, fee, realizedPnl };
    return {
        session: {
            ...session,
            cash: session.cash + proceeds,
            baseQuantity,
            costBasis,
            realizedPnl: session.realizedPnl + realizedPnl,
            trades: [...session.trades, trade]
        },
        success: true
    };
}

export function fillLimitOrders(session, candle, candleIndex) {
    const remaining = [];
    let updatedSession = session;
    let filled = 0;
    for (const order of session.pendingOrders ?? []) {
        const touched = order.side === 'buy' ? candle.low <= order.limitPrice : candle.high >= order.limitPrice;
        if (candleIndex <= order.createdAtIndex || !touched) {
            remaining.push(order);
            continue;
        }
        const price = order.side === 'buy'
            ? (candle.open <= order.limitPrice ? candle.open : order.limitPrice)
            : (candle.open >= order.limitPrice ? candle.open : order.limitPrice);
        const result = order.side === 'buy'
            ? executeBuy(updatedSession, order.amount, price, candle.time + 60, 'limit')
            : executeSell(updatedSession, order.amount, price, candle.time + 60, 'limit');
        updatedSession = result.session;
        if (result.success) filled += 1;
        else remaining.push(order);
    }
    return { session: { ...updatedSession, pendingOrders: remaining }, filled };
}