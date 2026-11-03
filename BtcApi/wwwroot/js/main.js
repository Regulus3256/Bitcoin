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
let currentRange = '1h';
let priceSocket;
let reconnectDelay = 2000;

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
        if (range !== currentRange) {
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

            const candle = aggregators[currentRange].processPrice(price);
            chartManager.updateCandle(candle);
            statusEl.textContent = `BTC/USD $${price.toLocaleString()} | ${new Date(update.updatedAt).toLocaleTimeString()} | ${currentRange.toUpperCase()}`;
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
connectPriceStream();