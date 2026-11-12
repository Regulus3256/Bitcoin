import { BtcApiClient } from './api.js';
import { CandleAggregator } from './aggregator.js';
import { CandleChartManager } from './chart.js';

const api = new BtcApiClient();
const aggregators = {
    '1m': new CandleAggregator(60),
    '1h': new CandleAggregator(3600),
    '4h': new CandleAggregator(14400),
    '1d': new CandleAggregator(86400),
    '1w': new CandleAggregator(604800)
};
const chartManager = new CandleChartManager('chart', document.getElementById('candle-detail'));
const statusEl = document.getElementById('status');
const timeframeButtons = [...document.querySelectorAll('.timeframe-btn')];
const toolsToggle = document.getElementById('tools-toggle');
const toolsDialog = document.getElementById('tools-dialog');
const fibonacciButton = document.getElementById('fib-tool');
const demoModeButton = document.getElementById('demo-mode-toggle');
const instrumentSelect = document.getElementById('market-symbol-select');
const initialVisibleCandles = 250;
const smaWarmupCandles = 199;
const demoStorageKey = 'btc-demo-session-v1';
const demoFeeRate = 0.001;
const replayPageSize = 10000;
let currentRange = '1h';
let priceSocket;
let reconnectDelay = 2000;
let replaySession = null;
let replayCandles = [];
let replayWarmupCandles = [];
let replayContextCandles = [];
let replayTimer = null;
let replaySaveTimer = null;
let replayLastTick = 0;
let replayAccumulator = 0;
let realRangeBeforeReplay = '1h';
let currentSymbol = 'BTC';
let currentInstrument = null;
let replayRange = '1m';
let replayAggregator = new CandleAggregator(60);
let replayProcessedIndex = -1;
let realSymbolBeforeReplay = 'BTC';

const replayIntervals = {
    '1m': 60,
    '1h': 3600,
    '4h': 14400,
    '1d': 86400,
    '1w': 604800
};

async function loadOlderHistory() {
    const oldestTime = chartManager.getOldestTime();
    if (!oldestTime) {
        return;
    }

    try {
        const olderCandles = await api.fetchHistoricalCandles(currentRange, oldestTime - 1, 120, null, null, currentSymbol);
        if (olderCandles.length > 0) {
            chartManager.prependCandles(olderCandles);
        }
    } catch (err) {
        console.error('Older history load error:', err);
    }
}

async function loadHistory(range = currentRange) {
    try {
        const requestedSymbol = currentSymbol;
        const candles = await api.fetchHistoricalCandles(range, null, initialVisibleCandles + smaWarmupCandles, null, null, requestedSymbol);
        if (range !== currentRange || requestedSymbol !== currentSymbol || replaySession) {
            return;
        }

        if (candles.length > 0) {
            chartManager.setCandles(candles);
            chartManager.setLoadMoreHandler(loadOlderHistory);
            const latest = candles[candles.length - 1];
            aggregators[range].seed(latest);
            statusEl.textContent = `Last Price: $${latest.close.toLocaleString()} | Updated: ${new Date(latest.time * 1000).toLocaleTimeString()}`;
            const openValue = Number(latest.open ?? 0);
            const highValue = Number(latest.high ?? 0);
            const lowValue = Number(latest.low ?? 0);
            const closeValue = Number(latest.close ?? 0);
            document.getElementById('detail-open').textContent = `$${openValue.toLocaleString()}`;
            document.getElementById('detail-high').textContent = `$${highValue.toLocaleString()}`;
            document.getElementById('detail-low').textContent = `$${lowValue.toLocaleString()}`;
            document.getElementById('detail-close').textContent = `$${closeValue.toLocaleString()}`;
        }
    } catch (err) {
        console.error('History load error:', err);
        statusEl.textContent = `履歴データを読み込めません: ${err.message}`;
    }
}

async function loadReplayCandles(from, through, symbol = currentSymbol) {
    const candles = [];
    let cursor = from;

    while (cursor < through) {
        const page = await api.fetchHistoricalCandles('1m', null, replayPageSize, cursor, through, symbol);
        if (page.length === 0) {
            break;
        }

        const validPage = page.filter(candle => candle.time >= cursor && candle.time < through);
        candles.push(...validPage);
        const lastTime = validPage.at(-1)?.time;
        if (lastTime === undefined || validPage.length < replayPageSize) {
            break;
        }
        cursor = lastTime + 60;
    }

    return candles;
}

async function loadReplayHistoryContext(range, startTime, symbol) {
    if (replayIntervals[range] === undefined) {
        return { visible: [], warmup: [] };
    }
    const history = await api.fetchHistoricalCandles(
        range,
        startTime,
        initialVisibleCandles + smaWarmupCandles,
        null,
        null,
        symbol);
    const visibleStart = Math.max(0, history.length - initialVisibleCandles);
    return {
        visible: history.slice(visibleStart),
        warmup: history.slice(0, visibleStart)
    };
}

function rebuildReplayChart() {
    if (!replaySession || replayCandles.length === 0) {
        return;
    }

    replayAggregator = new CandleAggregator(replayIntervals[replayRange]);
    if (replayContextCandles.length > 0) {
        replayAggregator.seed(replayContextCandles.at(-1));
    }
    replayProcessedIndex = -1;
    const aggregateCandles = [...replayContextCandles];
    for (let index = 0; index <= replaySession.cursorIndex; index += 1) {
        const candle = replayAggregator.processCandle(replayCandles[index]);
        const previous = aggregateCandles.at(-1);
        if (previous?.time === candle.time) {
            aggregateCandles[aggregateCandles.length - 1] = candle;
        } else {
            aggregateCandles.push(candle);
        }
        replayProcessedIndex = index;
    }

    chartManager.setCandles(aggregateCandles, true, replayWarmupCandles);
}

function saveReplaySession(immediate = false) {
    if (replaySaveTimer !== null) {
        window.clearTimeout(replaySaveTimer);
        replaySaveTimer = null;
    }

    const persist = () => {
        replaySaveTimer = null;
        if (!replaySession) {
            localStorage.removeItem(demoStorageKey);
            return;
        }

        const { candles: ignored, ...persistedSession } = replaySession;
        localStorage.setItem(demoStorageKey, JSON.stringify(persistedSession));
    };

    if (immediate) {
        persist();
    } else {
        replaySaveTimer = window.setTimeout(persist, 500);
    }
}

function formatUsd(value) {
    return `$${Number(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

function formatQuote(value) {
    const quoteAsset = currentInstrument?.quoteAsset ?? 'USD';
    return quoteAsset === 'USD'
        ? formatUsd(value)
        : `${Number(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${quoteAsset}`;
}

function currentReplayCandle() {
    return replaySession ? replayCandles[replaySession.cursorIndex] : null;
}

function setReplayStatus(message = '') {
    if (!replaySession) {
        return;
    }

    const candle = currentReplayCandle();
    if (candle) {
        const replayTime = new Date((candle.time + 60) * 1000);
        document.getElementById('replay-clock').textContent = `${replayTime.toISOString().replace('T', ' ').slice(0, 16)} UTC`;
    }

    const price = candle?.close ?? 0;
    const baseQuantity = replaySession.baseQuantity ?? replaySession.btc ?? 0;
    const equity = replaySession.cash + baseQuantity * price;
    const unrealized = baseQuantity > 0
        ? baseQuantity * price - replaySession.costBasis
        : 0;

    document.getElementById('account-cash').textContent = formatQuote(replaySession.cash);
    document.getElementById('account-base-asset').textContent = currentInstrument?.baseAsset ?? replaySession.symbol;
    document.getElementById('account-btc').textContent = baseQuantity.toFixed(8);
    document.getElementById('account-equity').textContent = formatQuote(equity);
    document.getElementById('account-realized').textContent = formatQuote(replaySession.realizedPnl);
    document.getElementById('account-unrealized').textContent = formatQuote(unrealized);
    document.getElementById('order-submit').disabled = !candle;
    document.getElementById('replay-step').textContent = `${replayRange === '1m' ? '1分足' : replayRange === '1h' ? '1時間足' : replayRange === '4h' ? '4時間足' : replayRange === '1d' ? '1日足' : '1週足'}進める`;

    if (message) {
        statusEl.textContent = message;
    } else if (replaySession.cursorIndex >= replayCandles.length - 1) {
        statusEl.textContent = 'セッション終了時刻です。取引履歴を確認できます';
        pauseReplay();
    } else if (replaySession.gapCount > 0) {
        statusEl.textContent = `デモ再生中 ${replaySession.cursorIndex + 1}/${replayCandles.length} | ${currentInstrument?.baseAsset}/${currentInstrument?.quoteAsset} ${formatQuote(price)} | 欠損 ${replaySession.gapCount}区間`;
    } else {
        statusEl.textContent = `デモ再生中 ${replaySession.cursorIndex + 1}/${replayCandles.length} | ${currentInstrument?.baseAsset}/${currentInstrument?.quoteAsset} ${formatQuote(price)} | 手数料 ${(demoFeeRate * 100).toFixed(2)}%`;
    }

    renderTradeLog();
    renderPendingOrders();
}

function renderTradeLog() {
    const tableBody = document.getElementById('trade-log-body');
    const trades = replaySession?.trades ?? [];
    if (!trades.length) {
        tableBody.innerHTML = '<tr><td colspan="7">取引履歴はありません</td></tr>';
        return;
    }

    tableBody.innerHTML = [...trades].reverse().slice(0, 100).map(trade => `
        <tr>
            <td>${new Date(trade.time * 1000).toISOString().replace('T', ' ').slice(0, 16)}</td>
            <td>${trade.symbol ?? replaySession.symbol}</td>
            <td>${trade.side === 'buy' ? '買い' : '売り'}${trade.orderType === 'limit' ? ' (指値)' : ''}</td>
            <td>${formatQuote(trade.price)}</td>
            <td>${trade.quantity.toFixed(8)}</td>
            <td>${formatQuote(trade.fee)}</td>
            <td>${trade.realizedPnl == null ? '-' : formatQuote(trade.realizedPnl)}</td>
        </tr>`).join('');
}

function renderPendingOrders() {
    const container = document.getElementById('pending-orders');
    const orders = replaySession?.pendingOrders ?? [];
    container.innerHTML = orders.map(order => {
        const amount = order.side === 'buy'
            ? formatQuote(order.amount)
            : `${Number(order.amount).toFixed(8)} ${currentInstrument?.baseAsset ?? replaySession.symbol}`;
        return `<div class="pending-order">${order.side === 'buy' ? '買い' : '売り'}指値 ${formatQuote(order.limitPrice)} / ${amount}<button type="button" data-cancel-order="${order.id}" aria-label="指値注文を取消">取消</button></div>`;
    }).join('');
}

function updateTimeframeControlsForReplay(isReplay) {
    timeframeButtons.forEach(button => {
        button.disabled = false;
        button.classList.toggle('active', isReplay ? button.dataset.range === replayRange : button.dataset.range === currentRange);
    });
    instrumentSelect.disabled = isReplay;
}

function showReplaySession() {
    document.getElementById('replay-setup').hidden = true;
    document.getElementById('replay-session').hidden = false;
    chartManager.setLoadMoreHandler(null);
    currentRange = replayRange;
    rebuildReplayChart();
    updateTimeframeControlsForReplay(true);
    setReplayStatus();
}

async function beginReplaySession(session = null) {
    pauseReplay();
    realSymbolBeforeReplay = currentSymbol;
    realRangeBeforeReplay = currentRange;
    const startTime = session?.startTime ?? Math.floor(new Date(`${document.getElementById('replay-date').value}T00:00:00Z`).getTime() / 1000);
    const durationDays = session?.durationDays ?? Number(document.getElementById('replay-days').value);
    const endTime = session?.endTime ?? startTime + durationDays * 86400;

    statusEl.textContent = '指定期間の1分足をDBから読み込んでいます...';
    const symbol = session?.symbol ?? currentSymbol;
    const candles = await loadReplayCandles(startTime, endTime, symbol);
    if (candles.length === 0) {
        throw new Error('選択期間の履歴がまだDBにありません。取込完了後に再試行してください。');
    }

    replayCandles = candles;
    const selectedReplayRange = session?.range ?? currentRange;
    const historyContext = await loadReplayHistoryContext(selectedReplayRange, startTime, symbol);
    replayContextCandles = historyContext.visible;
    replayWarmupCandles = historyContext.warmup;
    const gapCount = candles.slice(1).filter((candle, index) => candle.time - candles[index].time > 60).length;
    replaySession = session ?? {
        version: 1,
        symbol,
        startTime,
        endTime,
        durationDays,
        cursorIndex: 0,
        initialCash: Number(document.getElementById('replay-capital').value),
        cash: Number(document.getElementById('replay-capital').value),
        baseQuantity: 0,
        costBasis: 0,
        realizedPnl: 0,
        speed: 1,
        trades: [],
        pendingOrders: [],
        gapCount
    };

    replaySession.cursorIndex = Math.min(replaySession.cursorIndex ?? 0, candles.length - 1);
    replaySession.pendingOrders ??= [];
    replaySession.baseQuantity ??= replaySession.btc ?? 0;
    delete replaySession.btc;
    replaySession.gapCount = gapCount;
    currentSymbol = replaySession.symbol ?? symbol;
    replaySession.symbol = currentSymbol;
    instrumentSelect.value = currentSymbol;
    currentInstrument = JSON.parse(instrumentSelect.selectedOptions[0].dataset.instrument);
    document.getElementById('chart-title').textContent = `${currentInstrument.baseAsset}/${currentInstrument.quoteAsset} Realtime Candlestick Chart`;
    replayRange = replaySession.range ?? currentRange;
    replaySession.range = replayRange;
    document.getElementById('replay-quote-asset').textContent = currentInstrument.quoteAsset;
    document.getElementById('replay-price-asset').textContent = currentInstrument.quoteAsset;
    document.getElementById('replay-capital-label').firstChild.textContent = `初期資金 ${currentInstrument.quoteAsset}`;
    document.getElementById('replay-disclaimer').textContent =
        `デモ専用。過去価格は${currentInstrument.archiveSymbol}を${currentInstrument.quoteAsset}の近似として使用し、手数料を含むシミュレーション約定です。`;
    document.getElementById('replay-speed').value = String(replaySession.speed ?? 1);
    syncOrderForm();
    showReplaySession();
    saveReplaySession(true);
}

function advanceReplay(count = 1) {
    if (!replaySession || replaySession.cursorIndex >= replayCandles.length - 1) {
        pauseReplay();
        return;
    }

    let skippedMinutes = 0;
    let filledOrders = 0;
    const advancedCandles = [];
    for (let step = 0; step < count && replaySession.cursorIndex < replayCandles.length - 1; step += 1) {
        replaySession.cursorIndex += 1;
        const candle = currentReplayCandle();
        const previousCandle = replayCandles[replaySession.cursorIndex - 1];
        skippedMinutes += Math.max(0, Math.round((candle.time - previousCandle.time) / 60) - 1);
        filledOrders += fillLimitOrders(candle, replaySession.cursorIndex);
        const aggregateCandle = replayAggregator.processCandle(candle);
        const previousAggregate = advancedCandles.at(-1);
        if (previousAggregate?.time === aggregateCandle.time) {
            advancedCandles[advancedCandles.length - 1] = aggregateCandle;
        } else {
            advancedCandles.push(aggregateCandle);
        }
        replayProcessedIndex = replaySession.cursorIndex;
    }

    if (advancedCandles.length > 0) {
        chartManager.updateCandles(advancedCandles, currentReplayCandle().close);
    }

    const message = skippedMinutes > 0
        ? `価格データ欠損のため${skippedMinutes}分をスキップしました`
        : filledOrders > 0 ? `${filledOrders}件の指値注文が約定しました` : '';
    setReplayStatus(message);
    saveReplaySession();
}

function playReplay() {
    if (!replaySession || replayTimer !== null) {
        return;
    }

    const speed = Number(document.getElementById('replay-speed').value);
    replaySession.speed = speed;
    document.getElementById('replay-play').textContent = '一時停止';
    replayLastTick = performance.now();
    replayAccumulator = 0;
    replayTimer = window.setInterval(() => {
        const now = performance.now();
        replayAccumulator += ((now - replayLastTick) / 1000) * Number(document.getElementById('replay-speed').value);
        replayLastTick = now;
        const candlesToAdvance = Math.floor(replayAccumulator);
        if (candlesToAdvance > 0) {
            replayAccumulator -= candlesToAdvance;
            advanceReplay(candlesToAdvance);
        }
    }, 50);
    saveReplaySession(true);
}

function pauseReplay() {
    if (replayTimer !== null) {
        window.clearInterval(replayTimer);
        replayTimer = null;
    }
    const playButton = document.getElementById('replay-play');
    if (playButton) {
        playButton.textContent = '再生';
    }
}

function executeBuy(totalSpend, price, time, orderType = 'market') {
    totalSpend = Math.min(totalSpend, replaySession.cash);
    if (totalSpend <= 0 || price <= 0) return false;
    const gross = totalSpend / (1 + demoFeeRate);
    const fee = totalSpend - gross;
    const quantity = gross / price;
    replaySession.cash -= totalSpend;
    replaySession.baseQuantity = (replaySession.baseQuantity ?? 0) + quantity;
    replaySession.costBasis += totalSpend;
    replaySession.trades.push({ symbol: replaySession.symbol, side: 'buy', orderType, time, price, quantity, fee, realizedPnl: null });
    return true;
}

function executeSell(quantity, price, time, orderType = 'market') {
    quantity = Math.min(quantity, replaySession.baseQuantity ?? 0);
    if (quantity <= 0 || price <= 0) return false;
    const gross = quantity * price;
    const fee = gross * demoFeeRate;
    const proceeds = gross - fee;
    const allocatedCostBasis = replaySession.costBasis * quantity / replaySession.baseQuantity;
    const realizedPnl = proceeds - allocatedCostBasis;
    replaySession.cash += proceeds;
    replaySession.baseQuantity -= quantity;
    replaySession.costBasis -= allocatedCostBasis;
    replaySession.realizedPnl += realizedPnl;
    if (replaySession.baseQuantity < 1e-12) {
        replaySession.baseQuantity = 0;
        replaySession.costBasis = 0;
    }
    replaySession.trades.push({ symbol: replaySession.symbol, side: 'sell', orderType, time, price, quantity, fee, realizedPnl });
    return true;
}

function placeOrder() {
    if (!replaySession) return;
    const candle = currentReplayCandle();
    const side = document.getElementById('order-side').value;
    const type = document.getElementById('order-type').value;
    const amount = Number(document.getElementById('order-amount').value);
    const limitPrice = Number(document.getElementById('order-limit-price').value);
    if (!candle || !Number.isFinite(amount) || amount <= 0) return;

    if (side === 'buy') {
        const reservedCash = replaySession.pendingOrders.filter(order => order.side === 'buy').reduce((sum, order) => sum + order.amount, 0);
        if (amount > replaySession.cash - reservedCash) {
            setReplayStatus('未使用の現金残高を超える買い注文は出せません');
            return;
        }
    } else {
        const reservedQuantity = replaySession.pendingOrders.filter(order => order.side === 'sell').reduce((sum, order) => sum + order.amount, 0);
        if (amount > replaySession.baseQuantity - reservedQuantity) {
            setReplayStatus('未使用の保有数量を超える売り注文は出せません');
            return;
        }
    }

    if (type === 'market') {
        const success = side === 'buy'
            ? executeBuy(amount, candle.close, candle.time + 60)
            : executeSell(amount, candle.close, candle.time + 60);
        if (success) {
            setReplayStatus(side === 'buy' ? '成行買いを約定しました' : '成行売りを約定しました');
            saveReplaySession(true);
        }
        return;
    }

    if (!Number.isFinite(limitPrice) || limitPrice <= 0) {
        setReplayStatus('有効な指値価格を入力してください');
        return;
    }
    replaySession.pendingOrders.push({
        id: `${Date.now()}-${Math.random().toString(36).slice(2)}`,
        side,
        amount,
        limitPrice,
        createdAtIndex: replaySession.cursorIndex
    });
    setReplayStatus(`${side === 'buy' ? '買い' : '売り'}指値を受け付けました`);
    saveReplaySession(true);
}

function fillLimitOrders(candle, candleIndex) {
    const pendingOrders = replaySession.pendingOrders ?? [];
    let filled = 0;
    replaySession.pendingOrders = pendingOrders.filter(order => {
        if (candleIndex <= order.createdAtIndex) return true;
        const touched = order.side === 'buy' ? candle.low <= order.limitPrice : candle.high >= order.limitPrice;
        if (!touched) return true;
        const price = order.side === 'buy'
            ? (candle.open <= order.limitPrice ? candle.open : order.limitPrice)
            : (candle.open >= order.limitPrice ? candle.open : order.limitPrice);
        const success = order.side === 'buy'
            ? executeBuy(order.amount, price, candle.time + 60, 'limit')
            : executeSell(order.amount, price, candle.time + 60, 'limit');
        if (success) filled += 1;
        return !success;
    });
    return filled;
}

function syncOrderForm() {
    const side = document.getElementById('order-side').value;
    const type = document.getElementById('order-type').value;
    const amountLabel = document.getElementById('order-amount-label');
    const amountAsset = document.getElementById('replay-quote-asset');
    const submit = document.getElementById('order-submit');
    const isBuy = side === 'buy';
    amountLabel.textContent = isBuy ? '注文額' : '注文数量';
    amountAsset.textContent = isBuy
        ? currentInstrument?.quoteAsset ?? 'USD'
        : currentInstrument?.baseAsset ?? replaySession?.symbol ?? 'BTC';
    document.getElementById('order-limit-price-wrap').hidden = type !== 'limit';
    submit.textContent = `${type === 'limit' ? '指値' : '成行'}${isBuy ? '買い' : '売り'}`;
    submit.classList.toggle('buy', isBuy);
    submit.classList.toggle('sell', !isBuy);
}

function handleOrderSideChange() {
    const amountInput = document.getElementById('order-amount');
    if (document.getElementById('order-side').value === 'sell') {
        const reservedQuantity = replaySession?.pendingOrders.filter(order => order.side === 'sell').reduce((sum, order) => sum + order.amount, 0) ?? 0;
        amountInput.value = Math.max(0, (replaySession?.baseQuantity ?? 0) - reservedQuantity).toFixed(8);
    } else {
        amountInput.value = '1000';
    }
    syncOrderForm();
}

function cancelPendingOrder(orderId) {
    if (!replaySession) return;
    replaySession.pendingOrders = replaySession.pendingOrders.filter(order => order.id !== orderId);
    setReplayStatus('指値注文を取り消しました');
    saveReplaySession(true);
}

async function refreshReplayCoverage() {
    const dateInput = document.getElementById('replay-date');
    try {
        const coverage = await api.fetchHistoryCoverage(currentSymbol);
        if (coverage.oldestTime != null && coverage.newestTime != null) {
            document.getElementById('replay-coverage').textContent =
                `${new Date(coverage.oldestTime * 1000).toISOString().slice(0, 10)} 〜 ${new Date(coverage.newestTime * 1000).toISOString().slice(0, 10)} | ${Number(coverage.candleCount).toLocaleString()}本`;
            dateInput.min = new Date(coverage.oldestTime * 1000).toISOString().slice(0, 10);
            dateInput.max = new Date(coverage.newestTime * 1000).toISOString().slice(0, 10);
        } else {
            document.getElementById('replay-coverage').textContent = '過去データを取り込み中です';
        }
    } catch (err) {
        document.getElementById('replay-coverage').textContent = `DB状態を確認できません: ${err.message}`;
    }
}

function bindReplayControls() {
    document.getElementById('replay-start').addEventListener('click', async () => {
        try {
            await beginReplaySession();
        } catch (err) {
            statusEl.textContent = err.message;
        }
    });
    document.getElementById('replay-resume').addEventListener('click', async () => {
        const saved = localStorage.getItem(demoStorageKey);
        if (!saved) return;
        try {
            await beginReplaySession(JSON.parse(saved));
            document.getElementById('replay-resume').hidden = true;
        } catch (err) {
            statusEl.textContent = `履歴データの準備待ちです: ${err.message}`;
        }
    });
    document.getElementById('replay-play').addEventListener('click', () => replayTimer === null ? playReplay() : pauseReplay());
    document.getElementById('replay-step').addEventListener('click', () => {
        advanceReplay(replayIntervals[replayRange] / 60);
    });
    document.getElementById('replay-speed').addEventListener('change', () => {
        if (replayTimer !== null) {
            pauseReplay();
            playReplay();
        }
    });
    document.getElementById('order-submit').addEventListener('click', placeOrder);
    document.getElementById('order-side').addEventListener('change', handleOrderSideChange);
    document.getElementById('order-type').addEventListener('change', syncOrderForm);
    document.getElementById('pending-orders').addEventListener('click', event => {
        const cancelButton = event.target.closest('[data-cancel-order]');
        if (cancelButton) cancelPendingOrder(cancelButton.dataset.cancelOrder);
    });
    document.getElementById('replay-reset').addEventListener('click', () => {
        pauseReplay();
        replaySession = null;
        replayCandles = [];
        saveReplaySession(true);
        document.getElementById('replay-session').hidden = true;
        document.getElementById('replay-setup').hidden = false;
        document.getElementById('replay-panel').hidden = true;
        demoModeButton.setAttribute('aria-pressed', 'false');
        currentSymbol = realSymbolBeforeReplay;
        instrumentSelect.value = currentSymbol;
        currentInstrument = JSON.parse(instrumentSelect.selectedOptions[0].dataset.instrument);
        document.getElementById('chart-title').textContent = `${currentInstrument.baseAsset}/${currentInstrument.quoteAsset} Realtime Candlestick Chart`;
        document.getElementById('replay-quote-asset').textContent = currentInstrument.quoteAsset;
        document.getElementById('replay-price-asset').textContent = currentInstrument.quoteAsset;
        currentRange = realRangeBeforeReplay;
        updateTimeframeControlsForReplay(false);
        void refreshReplayCoverage();
        loadHistory(currentRange);
    });
}

async function initializeInstrumentSelector() {
    const instruments = await api.fetchInstruments();
    const preferredSymbol = localStorage.getItem('btc-chart-symbol');
    instrumentSelect.replaceChildren(...instruments.map(instrument => {
        const option = document.createElement('option');
        option.value = instrument.code;
        option.textContent = `${instrument.baseAsset}/${instrument.quoteAsset}`;
        option.dataset.instrument = JSON.stringify(instrument);
        return option;
    }));

    if (instruments.some(instrument => instrument.code === preferredSymbol)) {
        currentSymbol = preferredSymbol;
    }
    instrumentSelect.value = currentSymbol;
    currentInstrument = instruments.find(instrument => instrument.code === currentSymbol) ?? instruments[0];
    document.getElementById('chart-title').textContent = `${currentInstrument.baseAsset}/${currentInstrument.quoteAsset} Realtime Candlestick Chart`;
    instrumentSelect.addEventListener('change', async () => {
        if (replaySession) return;
        currentSymbol = instrumentSelect.value;
        currentInstrument = instruments.find(instrument => instrument.code === currentSymbol) ?? instruments[0];
        localStorage.setItem('btc-chart-symbol', currentSymbol);
        document.getElementById('chart-title').textContent = `${currentInstrument.baseAsset}/${currentInstrument.quoteAsset} Realtime Candlestick Chart`;
        await refreshReplayCoverage();
        await loadHistory(currentRange);
    });

    document.getElementById('demo-mode-toggle').addEventListener('click', () => {
        document.getElementById('replay-panel').hidden = false;
        demoModeButton.setAttribute('aria-pressed', 'true');
        toolsDialog.close();
        void refreshReplayCoverage();
        document.getElementById('replay-panel').scrollIntoView({ behavior: 'smooth', block: 'start' });
    });

    return instruments;
}

async function initializeReplayPanel() {
    const dateInput = document.getElementById('replay-date');
    const today = new Date();
    today.setUTCDate(today.getUTCDate() - 7);
    dateInput.value = today.toISOString().slice(0, 10);

    await refreshReplayCoverage();
    const saved = localStorage.getItem(demoStorageKey);
    if (saved) {
        document.getElementById('replay-resume').hidden = false;
    }

    bindReplayControls();
}

function connectPriceStream() {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    priceSocket = new WebSocket(`${protocol}//${window.location.host}/ws/price`);

    priceSocket.addEventListener('open', () => {
        reconnectDelay = 2000;
        statusEl.textContent = 'リアルタイム価格に接続しました';
    });

    priceSocket.addEventListener('message', event => {
        try {
            const update = JSON.parse(event.data);
            const price = Number(update.priceUsd);
            if (!Number.isFinite(price) || price <= 0) {
                return;
            }

            if (replaySession) {
                return;
            }
            if (update.symbol !== currentSymbol) return;

            const candle = aggregators[currentRange].processPrice(price);
            chartManager.updateCandle(candle, price);
            statusEl.textContent = chartManager.fibonacciDrawing
                ? (chartManager.fibonacciAnchor ? 'チャートをクリックしてフィボナッチの2点目を選択' : 'チャートをクリックしてフィボナッチの1点目を選択')
                : `${currentInstrument?.baseAsset ?? 'BTC'}/${currentInstrument?.quoteAsset ?? 'USD'} $${price.toLocaleString()} | ${new Date(update.updatedAt).toLocaleTimeString()} | ${currentRange.toUpperCase()}`;
        } catch (err) {
            console.error('Price stream message error:', err);
        }
    });

    priceSocket.addEventListener('close', () => {
        statusEl.textContent = '価格配信が切断されました。再接続しています...';
        window.setTimeout(connectPriceStream, reconnectDelay);
        reconnectDelay = Math.min(reconnectDelay * 2, 30000);
    });

    priceSocket.addEventListener('error', () => priceSocket.close());
}

async function setRange(range) {
    if (replaySession) {
        const sessionSymbol = replaySession.symbol;
        const historyContext = await loadReplayHistoryContext(range, replaySession.startTime, sessionSymbol);
        if (!replaySession || replaySession.symbol !== sessionSymbol) return;
        replayContextCandles = historyContext.visible;
        replayWarmupCandles = historyContext.warmup;
        replayRange = range;
        currentRange = range;
        replaySession.range = range;
        rebuildReplayChart();
        updateTimeframeControlsForReplay(true);
        setReplayStatus();
        saveReplaySession(true);
        return;
    }

    currentRange = range;
    timeframeButtons.forEach(button => {
        const isActive = button.dataset.range === range;
        button.classList.toggle('active', isActive);
    });
    loadHistory(range);
}

timeframeButtons.forEach(button => {
    button.addEventListener('click', () => { void setRange(button.dataset.range); });
});

toolsToggle.addEventListener('click', () => {
    if (!toolsDialog.open) {
        toolsDialog.showModal();
        toolsToggle.setAttribute('aria-expanded', 'true');
    }
});

document.getElementById('tools-close').addEventListener('click', () => {
    toolsDialog.close();
});

toolsDialog.addEventListener('close', () => {
    toolsToggle.setAttribute('aria-expanded', 'false');
});

document.querySelectorAll('[data-sma]').forEach(input => {
    input.addEventListener('change', () => {
        chartManager.setSmaVisible(input.dataset.sma, input.checked);
    });
});

fibonacciButton.addEventListener('click', () => {
    chartManager.setFibonacciDrawing(!chartManager.fibonacciDrawing);
    toolsDialog.close();
});

document.getElementById('fib-clear').addEventListener('click', () => {
    chartManager.clearFibonacci();
});

chartManager.setFibonacciStateHandler(state => {
    const active = state === 'first-point' || state === 'second-point';
    fibonacciButton.setAttribute('aria-pressed', String(active));
    if (state === 'first-point') {
        statusEl.textContent = 'チャートをクリックしてフィボナッチの始点を選択';
    } else if (state === 'second-point') {
        statusEl.textContent = 'チャートをクリックしてフィボナッチの終点を選択';
    } else if (state === 'complete') {
        statusEl.textContent = 'フィボナッチを描画しました。A/Bの丸をドラッグして調整できます';
    }
});

chartManager.setLoadMoreHandler(loadOlderHistory);
initializeInstrumentSelector()
    .then(() => Promise.all([loadHistory(currentRange), initializeReplayPanel()]))
    .catch(err => {
        statusEl.textContent = `初期化に失敗しました: ${err.message}`;
    });
connectPriceStream();