class CandleChartManager {
    constructor(containerId, detailPanel) {
        this.container = document.getElementById(containerId);
        this.detailPanel = detailPanel;
        this.allCandles = [];
        this.loadMoreHandler = null;
        this.loadingMore = false;
        this.defaultCandleSize = 12;
        this.currentCandleSize = this.defaultCandleSize;

        this.chart = LightweightCharts.createChart(this.container, {
            layout: {
                background: { type: 'solid', color: '#0b1220' },
                textColor: '#dfe7f2',
                fontFamily: 'Segoe UI, sans-serif'
            },
            grid: {
                vertLines: { color: '#1d2a3a' },
                horzLines: { color: '#1d2a3a' }
            },
            rightPriceScale: {
                borderColor: '#2a3f5f'
            },
            timeScale: {
                borderColor: '#2a3f5f',
                timeVisible: true,
                secondsVisible: false,
                barSpacing: this.defaultCandleSize,
                minBarSpacing: this.defaultCandleSize,
                maxBarSpacing: this.defaultCandleSize
            },
            crosshair: {
                mode: LightweightCharts.CrosshairMode.Normal
            }
        });

        this.series = this.chart.addSeries(LightweightCharts.CandlestickSeries, {
            upColor: '#26a69a',
            downColor: '#ef5350',
            borderVisible: false,
            wickUpColor: '#26a69a',
            wickDownColor: '#ef5350',
            priceLineColor: '#7dd3fc',
            lastValueVisible: true
        });

        this.bindCrosshair();
        this.bindVisibleRange();
        this.applyCandleSize(this.currentCandleSize);
    }

    bindCrosshair() {
        this.chart.subscribeCrosshairMove((param) => {
            const candle = param.seriesData.get(this.series);
            if (!candle) {
                return;
            }

            const open = Number(candle.open ?? 0);
            const high = Number(candle.high ?? 0);
            const low = Number(candle.low ?? 0);
            const close = Number(candle.close ?? 0);

            document.getElementById('detail-open').textContent = `$${open.toLocaleString()}`;
            document.getElementById('detail-high').textContent = `$${high.toLocaleString()}`;
            document.getElementById('detail-low').textContent = `$${low.toLocaleString()}`;
            document.getElementById('detail-close').textContent = `$${close.toLocaleString()}`;
        });
    }

    bindVisibleRange() {
        this.chart.timeScale().subscribeVisibleLogicalRangeChange((range) => {
            if (!range || this.loadingMore || !this.loadMoreHandler) {
                return;
            }

            if (range.from < 10) {
                this.loadingMore = true;
                this.loadMoreHandler().finally(() => {
                    this.loadingMore = false;
                });
            }
        });
    }

    applyCandleSize(size) {
        this.currentCandleSize = size;
        this.chart.timeScale().applyOptions({
            barSpacing: this.currentCandleSize,
            minBarSpacing: this.currentCandleSize,
            maxBarSpacing: this.currentCandleSize
        });
    }

    setLoadMoreHandler(handler) {
        this.loadMoreHandler = handler;
    }

    setCandles(candles, autoScroll = true) {
        this.allCandles = candles;
        this.series.setData(candles);
        this.applyCandleSize(this.currentCandleSize);
        if (autoScroll) {
            this.chart.timeScale().scrollToRealTime();
        }
    }

    getOldestTime() {
        return this.allCandles.length > 0 ? this.allCandles[0].time : null;
    }

    prependCandles(candles) {
        if (!candles.length) {
            return;
        }

        const merged = [...candles, ...this.allCandles];
        this.setCandles(merged, false);
    }

    updateCandle(candleData) {
        this.series.update(candleData);
        this.applyCandleSize(this.currentCandleSize);
        this.chart.timeScale().scrollToRealTime();
    }
}