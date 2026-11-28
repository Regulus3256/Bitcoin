import { BtcApiClient } from './api.js?v=20261128-history-cancel';
import { CandleAggregator } from './aggregator.js';
import { CandleChartManager } from './chart.js?v=20261113-chart-resize';
import { availableAmount, demoFeeRate, executeBuy, executeSell, fillLimitOrders } from './demo-trading.js?v=20261126-demo-playback-fix';

const replayPanel = document.getElementById('replay-panel');
function elementById(id) {
    return document.getElementById(id) ?? replayPanel?.querySelector(`#${id}`);
}

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
const historyLoadingEl = document.getElementById('history-loading');
const historyLoadingTextEl = historyLoadingEl.querySelector('.history-loading-text');
const historyHintEl = document.getElementById('history-hint');
const replayStatusEl = elementById('replay-message');
if (replayStatusEl) {
    new MutationObserver(() => { replayStatusEl.textContent = statusEl.textContent; })
        .observe(statusEl, { childList: true, characterData: true, subtree: true });
}
const timeframeButtons = [...document.querySelectorAll('.timeframe-btn')];
const toolsToggle = document.getElementById('tools-toggle');
const toolsDialog = document.getElementById('tools-dialog');
const fibonacciButton = document.getElementById('fib-tool');
const instrumentSelect = document.getElementById('market-symbol-select');
const initialVisibleCandles = 250;
const smaWarmupCandles = 199;
const olderHistoryPageSize = 120;
const demoStorageKey = 'btc-demo-session-v1';
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
let replayAdvancing = false;
let replayPlaybackGeneration = 0;
let replayHistoryExhausted = false;
let currentSymbol = 'BTC';
let currentInstrument = null;
let historyRequestController = null;
let historyRequestId = 0;
let replayRange = '1m';
let replayAggregator = new CandleAggregator(60);
let replayProcessedIndex = -1;

const replayIntervals = {
    '1m': 60,
    '1h': 3600,
    '4h': 14400,
    '1d': 86400,
    '1w': 604800
};
const timeframeLabels = {
    '1m': '1分足',
    '1h': '1時間足',
    '4h': '4時間足',
    '1d': '日足',
    '1w': '週足'
};
const fullHistoryRanges = new Set(['1d', '1w']);
const initialVisibleCandlesByRange = { '1d': 10000, '1w': 10000 };
const olderHistoryPageSizes = { '1d': 60, '1w': 20 };
let historyIndicatorId = 0;
let olderHistoryLoading = false;

function showHistoryLoading(message) {
    const indicatorId = ++historyIndicatorId;
    historyLoadingEl.hidden = false;
    historyLoadingTextEl.textContent = message;
    document.getElementById('chart').setAttribute('aria-busy', 'true');
    return indicatorId;
}

function hideHistoryLoading(indicatorId) {
    if (indicatorId !== historyIndicatorId) return;
    historyLoadingEl.hidden = true;
    document.getElementById('chart').removeAttribute('aria-busy');
}

function updateHistoryHint(range) {
    const visibleCount = initialVisibleCandlesByRange[range];
    historyHintEl.hidden = visibleCount === undefined || fullHistoryRanges.has(range);
    if (visibleCount !== undefined && !fullHistoryRanges.has(range)) {
        historyHintEl.textContent = `最新${visibleCount}${range === '1d' ? '日' : '週'}を表示中。左へスクロールすると過去データを追加します。`;
    }
}

async function loadOlderHistory() {
    if (olderHistoryLoading || document.getElementById('chart').getAttribute('aria-busy') === 'true') {
        return;
    }

    const oldestTime = chartManager.getOldestTime();
    if (oldestTime == null) {
        return;
    }

    const range = currentRange;
    const symbol = currentSymbol;
    const session = replaySession;
    const pageSize = olderHistoryPageSizes[range] ?? olderHistoryPageSize;
    const warmupCount = fullHistoryRanges.has(range) ? 0 : smaWarmupCandles;
    historyRequestController?.abort();
    const controller = new AbortController();
    historyRequestController = controller;
    const requestId = ++historyRequestId;
    const indicatorId = showHistoryLoading(`${timeframeLabels[range] ?? range}の過去データを追加読み込み中...`);
    olderHistoryLoading = true;

    try {
        const olderCandles = await api.fetchHistoricalCandles(
            range, oldestTime, pageSize + warmupCount, null, null, symbol, controller.signal);
        if (requestId !== historyRequestId || range !== currentRange || symbol !== currentSymbol || session !== replaySession || oldestTime !== chartManager.getOldestTime()) return;
        if (olderCandles.length === 0) {
            chartManager.setLoadMoreHandler(null);
            return;
        }

        const visibleStart = Math.max(0, olderCandles.length - pageSize);
        chartManager.prependCandles(olderCandles.slice(visibleStart), olderCandles.slice(0, visibleStart));
    } catch (err) {
        if (!controller.signal.aborted) {
            console.error('Older history load error:', err);
        }
    } finally {
        olderHistoryLoading = false;
        if (requestId === historyRequestId) {
            historyRequestController = null;
            hideHistoryLoading(indicatorId);
        }
    }
}

async function loadHistory(range = currentRange) {
    historyRequestController?.abort();
    const controller = new AbortController();
    historyRequestController = controller;
    const requestId = ++historyRequestId;
    const indicatorId = showHistoryLoading(`${timeframeLabels[range] ?? range}の履歴を読み込み中...`);
    const visibleCount = initialVisibleCandlesByRange[range] ?? initialVisibleCandles;
    const warmupCount = fullHistoryRanges.has(range) ? 0 : smaWarmupCandles;
    updateHistoryHint(range);

    try {
        const requestedSymbol = currentSymbol;
        const candles = await api.fetchHistoricalCandles(
            range, null, visibleCount + warmupCount, null, null, requestedSymbol, controller.signal);
        if (requestId !== historyRequestId || range !== currentRange || requestedSymbol !== currentSymbol || replaySession) {
            return;
        }

        if (candles.length > 0) {
            const visibleStart = Math.max(0, candles.length - visibleCount);
            chartManager.setCandles(candles.slice(visibleStart), true, candles.slice(0, visibleStart));
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
        } else {
            chartManager.setCandles([], true);
            statusEl.textContent = `${timeframeLabels[range] ?? range}の履歴データがありません`;
        }
    } catch (err) {
        if (controller.signal.aborted) {
            return;
        }
        console.error('History load error:', err);
        statusEl.textContent = `履歴データを読み込めません: ${err.message}`;
    } finally {
        if (requestId === historyRequestId) {
            historyRequestController = null;
            hideHistoryLoading(indicatorId);
        }
    }
}

async function loadReplayCandles(from, minimumCount, symbol = currentSymbol) {
    const candles = [];
    let cursor = from;

    while (candles.length < minimumCount) {
        const page = await api.fetchHistoricalCandles('1m', null, replayPageSize, cursor, null, symbol);
        if (page.length === 0) {
            break;
        }

        const validPage = page.filter(candle => candle.time >= cursor);
        candles.push(...validPage);
        const lastTime = validPage.at(-1)?.time;
        if (lastTime === undefined || validPage.length < replayPageSize) {
            break;
        }
        cursor = lastTime + 60;
    }

    return candles;
}

async function loadReplayHistoryContext(range, startTime, symbol, signal = undefined) {
    if (replayIntervals[range] === undefined) {
        return { visible: [], warmup: [] };
    }
    const visibleCount = initialVisibleCandlesByRange[range] ?? initialVisibleCandles;
    const warmupCount = fullHistoryRanges.has(range) ? 0 : smaWarmupCandles;
    const history = await api.fetchHistoricalCandles(
        range,
        startTime,
        visibleCount + warmupCount,
        null,
        null,
        symbol,
        signal);
    const visibleStart = Math.max(0, history.length - visibleCount);
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
        elementById('replay-clock').textContent = `${replayTime.toISOString().replace('T', ' ').slice(0, 16)} UTC`;
    }

    const price = candle?.close ?? 0;
    const baseQuantity = replaySession.baseQuantity ?? replaySession.btc ?? 0;
    const equity = replaySession.cash + baseQuantity * price;
    const unrealized = baseQuantity > 0
        ? baseQuantity * price - replaySession.costBasis
        : 0;

    elementById('account-cash').textContent = formatQuote(replaySession.cash);
    elementById('account-base-asset').textContent = currentInstrument?.baseAsset ?? replaySession.symbol;
    elementById('account-btc').textContent = baseQuantity.toFixed(8);
    elementById('account-equity').textContent = formatQuote(equity);
    elementById('account-realized').textContent = formatQuote(replaySession.realizedPnl);
    elementById('account-unrealized').textContent = formatQuote(unrealized);
    elementById('order-submit').disabled = !candle;

    if (message) {
        statusEl.textContent = message;
    } else if (replayHistoryExhausted && replaySession.cursorIndex >= replayCandles.length - 1) {
        statusEl.textContent = '利用可能な最新の履歴に到達しました';
        pauseReplay();
    } else if (replaySession.gapCount > 0) {
        statusEl.textContent = `デモ再生中 | ${currentInstrument?.baseAsset}/${currentInstrument?.quoteAsset} ${formatQuote(price)} | 欠損 ${replaySession.gapCount}区間`;
    } else {
        statusEl.textContent = `デモ再生中 | ${currentInstrument?.baseAsset}/${currentInstrument?.quoteAsset} ${formatQuote(price)} | 手数料 ${(demoFeeRate * 100).toFixed(2)}%`;
    }

    renderTradeLog();
    renderPendingOrders();
}

function renderTradeLog() {
    const tableBody = elementById('trade-log-body');
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
    const container = elementById('pending-orders');
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
        const isActive = isReplay ? button.dataset.range === replayRange : button.dataset.range === currentRange;
        button.classList.toggle('active', isActive);
        button.setAttribute('aria-pressed', String(isActive));
    });
    instrumentSelect.disabled = isReplay;
}

function showReplaySession() {
    elementById('replay-setup').hidden = true;
    elementById('replay-session').hidden = false;
    currentRange = replayRange;
    rebuildReplayChart();
    chartManager.setLoadMoreHandler(loadOlderHistory);
    updateTimeframeControlsForReplay(true);
    setReplayStatus();
}

async function beginReplaySession(session = null, selectedStartTime = null) {
    pauseReplay();
    const startTime = session?.startTime ?? selectedStartTime ?? Math.floor(new Date(`${elementById('replay-date').value}T00:00:00Z`).getTime() / 1000);

    statusEl.textContent = '開始日からの1分足をDBから読み込んでいます...';
    const symbol = session?.symbol ?? currentSymbol;
    const candles = await loadReplayCandles(startTime, Math.max(replayPageSize, (session?.cursorIndex ?? 0) + 1), symbol);
    if (candles.length === 0) {
        throw new Error('開始日以降の履歴がDBにありません。取込完了後に再試行してください。');
    }

    replayCandles = candles;
    replayHistoryExhausted = false;
    const selectedReplayRange = session?.range ?? currentRange;
    const historyContext = await loadReplayHistoryContext(selectedReplayRange, startTime, symbol);
    replayContextCandles = historyContext.visible;
    replayWarmupCandles = historyContext.warmup;
    const gapCount = candles.slice(1).filter((candle, index) => candle.time - candles[index].time > 60).length;
    replaySession = session ?? {
        version: 1,
        symbol,
        startTime,
        cursorIndex: 0,
        initialCash: Number(elementById('replay-capital').value),
        cash: Number(elementById('replay-capital').value),
        baseQuantity: 0,
        costBasis: 0,
        realizedPnl: 0,
        speed: 1,
        trades: [],
        pendingOrders: [],
        gapCount
    };

    replaySession.cursorIndex = Math.min(replaySession.cursorIndex ?? 0, candles.length - 1);
    delete replaySession.endTime;
    delete replaySession.durationDays;
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
    elementById('replay-quote-asset').textContent = currentInstrument.quoteAsset;
    elementById('replay-price-asset').textContent = currentInstrument.quoteAsset;
    elementById('replay-capital-label').firstChild.textContent = `初期資金 ${currentInstrument.quoteAsset}`;
    elementById('replay-disclaimer').textContent =
        `デモ専用。過去価格は${currentInstrument.archiveSymbol}を${currentInstrument.quoteAsset}の近似として使用し、手数料を含むシミュレーション約定です。`;
    elementById('replay-speed').value = String(replaySession.speed ?? 1);
    syncOrderForm();
    showReplaySession();
    chartManager.updateCurrentPrice(currentReplayCandle().close);
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
        const result = fillLimitOrders(replaySession, candle, replaySession.cursorIndex);
        Object.assign(replaySession, result.session);
        filledOrders += result.filled;
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

async function advanceReplayBars(count, playbackGeneration) {
    if (!replaySession) return;

    const session = replaySession;
    const interval = replayIntervals[replayRange];
    const currentTime = currentReplayCandle().time + 60;
    const targetTime = Math.floor(currentTime / interval) * interval + interval * count;
    while (replayCandles.at(-1).time < targetTime - 60) {
        const cursor = replayCandles.at(-1).time + 60;
        const page = await api.fetchHistoricalCandles('1m', null, replayPageSize, cursor, null, session.symbol);
        if (replaySession !== session || replayPlaybackGeneration !== playbackGeneration) return;
        const nextCandles = page.filter(candle => candle.time >= cursor);
        if (nextCandles.length === 0) {
            replayHistoryExhausted = true;
            break;
        }
        const previous = replayCandles.at(-1);
        session.gapCount += nextCandles.reduce((count, candle, index) => {
            const preceding = index === 0 ? previous : nextCandles[index - 1];
            return count + (candle.time - preceding.time > 60 ? 1 : 0);
        }, 0);
        replayCandles.push(...nextCandles);
    }
    if (replaySession !== session || replayPlaybackGeneration !== playbackGeneration) return;
    let targetIndex = replaySession.cursorIndex;
    while (targetIndex + 1 < replayCandles.length && replayCandles[targetIndex + 1].time < targetTime) {
        targetIndex += 1;
    }
    if (targetIndex === replaySession.cursorIndex && targetIndex < replayCandles.length - 1) {
        targetIndex += 1;
    }
    if (targetIndex === replaySession.cursorIndex) {
        setReplayStatus();
        return;
    }
    advanceReplay(targetIndex - replaySession.cursorIndex);
}

function playReplay() {
    if (!replaySession || replayTimer !== null) {
        return;
    }

    const speed = Number(elementById('replay-speed').value);
    replaySession.speed = speed;
    elementById('replay-play').textContent = '一時停止';
    replayHistoryExhausted = false;
    const playbackGeneration = ++replayPlaybackGeneration;
    replayLastTick = performance.now();
    replayAccumulator = 0;
    replayTimer = window.setInterval(() => {
        const now = performance.now();
        replayAccumulator += ((now - replayLastTick) / 1000) * Number(elementById('replay-speed').value);
        replayLastTick = now;
        const barsToAdvance = Math.floor(replayAccumulator);
        if (barsToAdvance > 0 && !replayAdvancing) {
            replayAccumulator -= barsToAdvance;
            replayAdvancing = true;
            advanceReplayBars(barsToAdvance, playbackGeneration)
                .catch(error => {
                    if (replayPlaybackGeneration === playbackGeneration) {
                        pauseReplay();
                        statusEl.textContent = `履歴データを読み込めません: ${error.message}`;
                    }
                })
                .finally(() => { replayAdvancing = false; });
        }
    }, 50);
    saveReplaySession(true);
}

function pauseReplay() {
    replayPlaybackGeneration += 1;
    if (replayTimer !== null) {
        window.clearInterval(replayTimer);
        replayTimer = null;
    }
    const playButton = elementById('replay-play');
    if (playButton) {
        playButton.textContent = '再生';
    }
}

function placeOrder() {
    if (!replaySession) return;
    const candle = currentReplayCandle();
    const side = elementById('order-side').value;
    const type = elementById('order-type').value;
    const amount = Number(elementById('order-amount').value);
    const limitPrice = Number(elementById('order-limit-price').value);
    if (!candle || !Number.isFinite(amount) || amount <= 0) return;

    if (side === 'buy') {
        if (amount > availableAmount(replaySession, side)) {
            setReplayStatus('未使用の現金残高を超える買い注文は出せません');
            return;
        }
    } else {
        if (amount > availableAmount(replaySession, side)) {
            setReplayStatus('未使用の保有数量を超える売り注文は出せません');
            return;
        }
    }

    if (type === 'market') {
        const result = side === 'buy'
            ? executeBuy(replaySession, amount, candle.close, candle.time + 60)
            : executeSell(replaySession, amount, candle.close, candle.time + 60);
        Object.assign(replaySession, result.session);
        if (result.success) {
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

function syncOrderForm() {
    const side = elementById('order-side').value;
    const type = elementById('order-type').value;
    const amountLabel = elementById('order-amount-label');
    const amountAsset = elementById('replay-quote-asset');
    const submit = elementById('order-submit');
    const isBuy = side === 'buy';
    amountLabel.textContent = isBuy ? '注文額' : '注文数量';
    amountAsset.textContent = isBuy
        ? currentInstrument?.quoteAsset ?? 'USD'
        : currentInstrument?.baseAsset ?? replaySession?.symbol ?? 'BTC';
    elementById('order-limit-price-wrap').hidden = type !== 'limit';
    submit.textContent = `${type === 'limit' ? '指値' : '成行'}${isBuy ? '買い' : '売り'}`;
    submit.classList.toggle('buy', isBuy);
    submit.classList.toggle('sell', !isBuy);
}

function handleOrderSideChange() {
    const amountInput = elementById('order-amount');
    if (elementById('order-side').value === 'sell') {
        amountInput.value = Math.max(0, replaySession ? availableAmount(replaySession, 'sell') : 0).toFixed(8);
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
    const dateInput = elementById('replay-date');
    try {
        const coverage = await api.fetchHistoryCoverage(currentSymbol);
        if (coverage.oldestTime != null && coverage.newestTime != null) {
            elementById('replay-coverage').textContent =
                `${new Date(coverage.oldestTime * 1000).toISOString().slice(0, 10)} 〜 ${new Date(coverage.newestTime * 1000).toISOString().slice(0, 10)} | ${Number(coverage.candleCount).toLocaleString()}本`;
            dateInput.min = new Date(coverage.oldestTime * 1000).toISOString().slice(0, 10);
            dateInput.max = new Date(coverage.newestTime * 1000).toISOString().slice(0, 10);
        } else {
            elementById('replay-coverage').textContent = '過去データを取り込み中です';
        }
    } catch (err) {
        elementById('replay-coverage').textContent = `DB状態を確認できません: ${err.message}`;
    }
}

function bindReplayControls() {
    elementById('replay-start').addEventListener('click', async () => {
        try {
            await beginReplaySession();
        } catch (err) {
            statusEl.textContent = err.message;
        }
    });
    elementById('replay-resume').addEventListener('click', async () => {
        const saved = localStorage.getItem(demoStorageKey);
        if (!saved) return;
        try {
            await beginReplaySession(JSON.parse(saved));
            elementById('replay-resume').hidden = true;
        } catch (err) {
            statusEl.textContent = `履歴データの準備待ちです: ${err.message}`;
        }
    });
    elementById('replay-play').addEventListener('click', () => replayTimer === null ? playReplay() : pauseReplay());
    elementById('replay-speed').addEventListener('change', () => {
        if (replayTimer !== null) {
            pauseReplay();
            playReplay();
        }
    });
    elementById('order-submit').addEventListener('click', placeOrder);
    elementById('order-side').addEventListener('change', handleOrderSideChange);
    elementById('order-type').addEventListener('change', syncOrderForm);
    elementById('pending-orders').addEventListener('click', event => {
        const cancelButton = event.target.closest('[data-cancel-order]');
        if (cancelButton) cancelPendingOrder(cancelButton.dataset.cancelOrder);
    });
    elementById('replay-reset').addEventListener('click', () => {
        pauseReplay();
        replaySession = null;
        replayCandles = [];
        saveReplaySession(true);
        window.location.assign('/');
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
        if (replayPanel) await refreshReplayCoverage();
        await loadHistory(currentRange);
    });

    return instruments;
}

chartManager.setContextMenuHandler(async startTime => {
    if (!replayPanel) {
        window.location.assign(`/demo?start=${startTime}`);
        return;
    }
    if (replaySession) {
        statusEl.textContent = 'デモを終了してから別の足を選択してください';
        return;
    }
    if (localStorage.getItem(demoStorageKey) && !window.confirm('保存済みデモを新しいセッションに置き換えますか？')) return;
    try {
        await beginReplaySession(null, startTime);
    } catch (error) {
        statusEl.textContent = `デモを開始できません: ${error.message}`;
    }
});

async function initializeReplayPanel() {
    const dateInput = elementById('replay-date');
    const today = new Date();
    today.setUTCDate(today.getUTCDate() - 7);
    dateInput.value = today.toISOString().slice(0, 10);

    await refreshReplayCoverage();
    const saved = localStorage.getItem(demoStorageKey);
    if (saved) {
        elementById('replay-resume').hidden = false;
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
        if (range === replayRange) return;
        updateHistoryHint(range);
        historyRequestController?.abort();
        const controller = new AbortController();
        historyRequestController = controller;
        const requestId = ++historyRequestId;
        const indicatorId = showHistoryLoading(`${timeframeLabels[range] ?? range}の履歴を読み込み中...`);
        try {
            const sessionSymbol = replaySession.symbol;
            const historyContext = await loadReplayHistoryContext(
                range, replaySession.startTime, sessionSymbol, controller.signal);
            if (requestId !== historyRequestId || !replaySession || replaySession.symbol !== sessionSymbol) return;
            replayContextCandles = historyContext.visible;
            replayWarmupCandles = historyContext.warmup;
            replayRange = range;
            currentRange = range;
            replaySession.range = range;
            rebuildReplayChart();
            updateTimeframeControlsForReplay(true);
            setReplayStatus();
            saveReplaySession(true);
        } catch (err) {
            if (!controller.signal.aborted) {
                statusEl.textContent = `履歴データを読み込めません: ${err.message}`;
            }
        } finally {
            if (requestId === historyRequestId) {
                historyRequestController = null;
                hideHistoryLoading(indicatorId);
            }
        }
        return;
    }

    if (range === currentRange) return;
    currentRange = range;
    timeframeButtons.forEach(button => {
        const isActive = button.dataset.range === range;
        button.classList.toggle('active', isActive);
        button.setAttribute('aria-pressed', String(isActive));
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
    .then(() => Promise.all([loadHistory(currentRange), replayPanel ? initializeReplayPanel() : Promise.resolve()]))
    .then(() => {
        if (!replayPanel) return;
        const selectedStartTime = Number(new URLSearchParams(window.location.search).get('start'));
        if (Number.isSafeInteger(selectedStartTime) && selectedStartTime > 0) {
            window.history.replaceState(null, '', '/demo');
            if (localStorage.getItem(demoStorageKey) && !window.confirm('保存済みデモを新しいセッションに置き換えますか？')) return;
            void beginReplaySession(null, selectedStartTime)
                .catch(error => { statusEl.textContent = `デモを開始できません: ${error.message}`; });
        }
    })
    .catch(err => {
        statusEl.textContent = `初期化に失敗しました: ${err.message}`;
    });
connectPriceStream();