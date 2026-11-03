const api = new BtcApiClient();
const aggregators = {
    '1m': new CandleAggregator(60),
    '1h': new CandleAggregator(60),
    '4h': new CandleAggregator(60),
    '1d': new CandleAggregator(60),
    '1w': new CandleAggregator(60)
};
const chartManager = new CandleChartManager('chart', document.getElementById('candle-detail'));
const statusEl = document.getElementById('status');
const timeframeButtons = [...document.querySelectorAll('.timeframe-btn')];
let currentRange = '1h';

async function loadOlderHistory() {
    const oldestTime = chartManager.getOldestTime();
    if (!oldestTime) {
        return;
    }

    try {
        const olderCandles = await api.fetchHistoricalCandles(currentRange, oldestTime - 1, 120);
        if (olderCandles.length > 0) {
            chartManager.prependCandles(olderCandles);
        }
    } catch (err) {
        console.error('Older history load error:', err);
    }
}

async function loadHistory(range = currentRange) {
    try {
        const candles = await api.fetchHistoricalCandles(range, null, range === '1m' ? 120 : 120);
        if (candles.length > 0) {
            chartManager.setCandles(candles);
            chartManager.setLoadMoreHandler(loadOlderHistory);
            const latest = candles[candles.length - 1];
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
    }
}

async function tick() {
    try {
        const price = await api.fetchCurrentPrice();
        const candle = aggregators[currentRange].processPrice(price);
        chartManager.updateCandle(candle);

        statusEl.textContent = `Last Price: $${price.toLocaleString()} | Updated: ${new Date().toLocaleTimeString()} | ${currentRange.toUpperCase()}`;
    } catch (err) {
        console.error('Tick error:', err);
        statusEl.textContent = `Error: ${err.message}`;
    }
}

function setRange(range) {
    currentRange = range;
    timeframeButtons.forEach(button => {
        const isActive = button.dataset.range === range;
        button.classList.toggle('active', isActive);
    });
    loadHistory(range);
}

timeframeButtons.forEach(button => {
    button.addEventListener('click', () => setRange(button.dataset.range));
});

chartManager.setLoadMoreHandler(loadOlderHistory);
loadHistory();
tick();
setInterval(tick, 10000);