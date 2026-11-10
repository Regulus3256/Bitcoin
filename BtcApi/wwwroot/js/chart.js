export class CandleChartManager {
    constructor(containerId, detailPanel) {
        this.container = document.getElementById(containerId);
        this.detailPanel = detailPanel;
        this.allCandles = [];
        this.loadMoreHandler = null;
        this.loadingMore = false;
        this.defaultCandleSize = 12;
        this.currentCandleSize = this.defaultCandleSize;
        this.currentPriceLine = null;
        this.followLatest = true;
        this.fibonacciDrawing = false;
        this.fibonacciAnchor = null;
        this.fibonacciPoints = null;
        this.fibonacciLines = [];
        this.fibonacciStateHandler = null;
        this.draggingFibIndex = null;

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
                rightOffset: 0,
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
            lastValueVisible: false
        });

        this.smaSeries = new Map([
            [10, { color: '#f4c95d', series: this.chart.addSeries(LightweightCharts.LineSeries, { color: '#f4c95d', lineWidth: 2, priceLineVisible: false, lastValueVisible: false, visible: false, title: '10' }) }],
            [50, { color: '#54c6eb', series: this.chart.addSeries(LightweightCharts.LineSeries, { color: '#54c6eb', lineWidth: 2, priceLineVisible: false, lastValueVisible: false, visible: false, title: '50' }) }],
            [100, { color: '#e879b9', series: this.chart.addSeries(LightweightCharts.LineSeries, { color: '#e879b9', lineWidth: 2, priceLineVisible: false, lastValueVisible: false, visible: false, title: '100' }) }],
            [200, { color: '#a78bfa', series: this.chart.addSeries(LightweightCharts.LineSeries, { color: '#a78bfa', lineWidth: 2, priceLineVisible: false, lastValueVisible: false, visible: false, title: '200' }) }]
        ]);
        this.fibonacciConnector = this.chart.addSeries(LightweightCharts.LineSeries, {
            color: '#f0b84c',
            lineWidth: 1,
            lineStyle: LightweightCharts.LineStyle.Dashed,
            priceLineVisible: false,
            lastValueVisible: false,
            crosshairMarkerVisible: false
        });
        this.fibonacciConnector.setData([]);
        this.fibHandles = [this.createFibHandle(0), this.createFibHandle(1)];

        this.bindCrosshair();
        this.bindDrawingClick();
        this.bindVisibleRange();
        this.applyCandleSize(this.currentCandleSize);
        this.resizeObserver = new ResizeObserver(() => this.positionFibHandles());
        this.resizeObserver.observe(this.container);
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
            this.positionFibHandles();
            if (!range) {
                return;
            }

            const latestIndex = this.allCandles.length - 1;
            if (latestIndex >= 0) {
                this.followLatest = range.to >= latestIndex - 0.5;
            }

            if (!this.loadingMore && this.loadMoreHandler && range.from < 10) {
                this.loadingMore = true;
                this.loadMoreHandler().finally(() => {
                    this.loadingMore = false;
                });
            }
        });
    }

    bindDrawingClick() {
        this.chart.subscribeClick(param => {
            if (!this.fibonacciDrawing || !param.point) {
                return;
            }

            const price = this.series.coordinateToPrice(param.point.y);
            if (price === null || !Number.isFinite(price)) {
                return;
            }

            const time = param.time ?? this.getTimeAtCoordinate(param.point.x);
            if (time === null || time === undefined) {
                return;
            }

            const point = { time, price };
            if (!this.fibonacciAnchor) {
                this.fibonacciAnchor = point;
                this.notifyFibonacciState('second-point');
                return;
            }

            this.drawFibonacci(this.fibonacciAnchor, point);
            this.fibonacciAnchor = null;
            this.setFibonacciDrawing(false);
            this.notifyFibonacciState('complete');
        });
    }

    getTimeAtCoordinate(x) {
        const timeScale = this.chart.timeScale();
        let time = timeScale.coordinateToTime(x);
        if (time !== null && time !== undefined) {
            return time;
        }

        let logical = typeof timeScale.coordinateToLogical === 'function'
            ? timeScale.coordinateToLogical(x)
            : null;
        if ((logical === null || logical === undefined) && this.allCandles.length > 0) {
            const range = timeScale.getVisibleLogicalRange();
            const scaleWidth = typeof timeScale.width === 'function'
                ? timeScale.width()
                : this.container.clientWidth - 80;
            if (range && scaleWidth > 0) {
                logical = range.from + (x / scaleWidth) * (range.to - range.from);
            }
        }

        if (logical === null || logical === undefined || this.allCandles.length === 0) {
            return null;
        }

        const index = Math.max(0, Math.min(this.allCandles.length - 1, Math.round(logical)));
        return this.allCandles[index].time;
    }

    createFibHandle(index) {
        const handle = document.createElement('button');
        handle.type = 'button';
        handle.className = 'fib-anchor-handle';
        handle.textContent = index === 0 ? 'A' : 'B';
        handle.setAttribute('aria-label', `フィボナッチ${index === 0 ? '始点' : '終点'}をドラッグ`);
        handle.hidden = true;
        this.container.append(handle);

        handle.addEventListener('pointerdown', event => {
            if (!this.fibonacciPoints) {
                return;
            }

            event.preventDefault();
            event.stopPropagation();
            this.draggingFibIndex = index;
            handle.classList.add('dragging');
            handle.setPointerCapture(event.pointerId);
        });

        handle.addEventListener('pointermove', event => {
            if (this.draggingFibIndex !== index) {
                return;
            }

            const rect = this.container.getBoundingClientRect();
            const timeScale = this.chart.timeScale();
            const plotWidth = typeof timeScale.width === 'function'
                ? timeScale.width()
                : this.container.clientWidth - 80;
            const x = Math.max(0, Math.min(event.clientX - rect.left, plotWidth));
            const y = Math.max(0, Math.min(event.clientY - rect.top, this.container.clientHeight));
            const time = timeScale.coordinateToTime(x);
            const price = this.series.coordinateToPrice(y);
            if (time === null || time === undefined || price === null || !Number.isFinite(price)) {
                return;
            }

            this.fibonacciPoints[index] = { time, price };
            this.drawFibonacciLevels();
        });

        const stopDragging = event => {
            if (this.draggingFibIndex !== index) {
                return;
            }
            this.draggingFibIndex = null;
            handle.classList.remove('dragging');
            if (handle.hasPointerCapture(event.pointerId)) {
                handle.releasePointerCapture(event.pointerId);
            }
        };

        handle.addEventListener('pointerup', stopDragging);
        handle.addEventListener('pointercancel', stopDragging);
        return handle;
    }

    positionFibHandles() {
        if (!this.fibonacciPoints) {
            this.fibHandles.forEach(handle => { handle.hidden = true; });
            return;
        }

        this.fibonacciPoints.forEach((point, index) => {
            const x = this.chart.timeScale().timeToCoordinate(point.time);
            const y = this.series.priceToCoordinate(point.price);
            const handle = this.fibHandles[index];
            if (x === null || y === null || x === undefined || y === undefined) {
                handle.hidden = true;
                return;
            }

            handle.hidden = false;
            handle.style.left = `${x}px`;
            handle.style.top = `${y}px`;
        });
    }

    setSmaVisible(period, visible) {
        const indicator = this.smaSeries.get(Number(period));
        if (indicator) {
            indicator.series.applyOptions({ visible });
        }
    }

    setFibonacciStateHandler(handler) {
        this.fibonacciStateHandler = handler;
    }

    notifyFibonacciState(state) {
        if (this.fibonacciStateHandler) {
            this.fibonacciStateHandler(state);
        }
    }

    setFibonacciDrawing(enabled) {
        this.fibonacciDrawing = enabled;
        this.fibonacciAnchor = null;
        this.container.style.cursor = enabled ? 'crosshair' : '';
        this.notifyFibonacciState(enabled ? 'first-point' : 'idle');
    }

    drawFibonacci(firstPoint, secondPoint) {
        this.fibonacciLines.forEach(line => this.series.removePriceLine(line));
        this.fibonacciLines = [];
        this.fibonacciPoints = [firstPoint, secondPoint];
        this.drawFibonacciLevels();
    }

    drawFibonacciLevels() {
        if (!this.fibonacciPoints) {
            return;
        }

        this.fibonacciLines.forEach(line => this.series.removePriceLine(line));
        this.fibonacciLines = [];
        const [firstPoint, secondPoint] = this.fibonacciPoints;

        const priceRange = firstPoint.price - secondPoint.price;
        const levels = [
            [0, '0%'],
            [0.236, '23.6%'],
            [0.382, '38.2%'],
            [0.5, '50%'],
            [0.618, '61.8%'],
            [0.786, '78.6%'],
            [1, '100%']
        ];

        this.fibonacciLines = levels.map(([ratio, label]) => this.series.createPriceLine({
            price: secondPoint.price + priceRange * ratio,
            color: ratio === 0 || ratio === 1 ? '#f0b84c' : '#9aabc2',
            lineWidth: 1,
            lineStyle: LightweightCharts.LineStyle.Dashed,
            lineVisible: true,
            axisLabelVisible: true,
            title: `Fib ${label}`
        }));

        const connectorPoints = [...this.fibonacciPoints].sort((a, b) => a.time - b.time);
        this.fibonacciConnector.setData(connectorPoints.map(point => ({
            time: point.time,
            value: point.price
        })));
        this.positionFibHandles();
    }

    clearFibonacci() {
        this.fibonacciLines.forEach(line => this.series.removePriceLine(line));
        this.fibonacciLines = [];
        this.fibonacciAnchor = null;
        this.fibonacciPoints = null;
        this.fibonacciConnector.setData([]);
        this.positionFibHandles();
        this.setFibonacciDrawing(false);
    }

    refreshSmaData() {
        for (const [period, indicator] of this.smaSeries) {
            const points = [];
            let sum = 0;
            for (let index = 0; index < this.allCandles.length; index += 1) {
                sum += this.allCandles[index].close;
                if (index >= period) {
                    sum -= this.allCandles[index - period].close;
                }
                if (index >= period - 1) {
                    points.push({ time: this.allCandles[index].time, value: sum / period });
                }
            }
            indicator.series.setData(points);
        }
    }

    updateLatestSmaData() {
        const latestIndex = this.allCandles.length - 1;
        for (const [period, indicator] of this.smaSeries) {
            if (this.allCandles.length < period) {
                continue;
            }

            const recentCandles = this.allCandles.slice(-period);
            const average = recentCandles.reduce((sum, candle) => sum + candle.close, 0) / period;
            indicator.series.update({ time: this.allCandles[latestIndex].time, value: average });
        }
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
        this.refreshSmaData();
        this.applyCandleSize(this.currentCandleSize);
        if (autoScroll) {
            this.chart.timeScale().scrollToRealTime();
        }
        this.positionFibHandles();
    }

    getOldestTime() {
        return this.allCandles.length > 0 ? this.allCandles[0].time : null;
    }

    prependCandles(candles) {
        if (!candles.length) {
            return;
        }

        const visibleRange = this.chart.timeScale().getVisibleLogicalRange();
        const uniqueCandles = candles.filter(candle => candle.time < this.allCandles[0]?.time);
        if (!uniqueCandles.length) {
            return;
        }

        const merged = [...uniqueCandles, ...this.allCandles];
        this.setCandles(merged, false);

        if (visibleRange) {
            this.chart.timeScale().setVisibleLogicalRange({
                from: visibleRange.from + uniqueCandles.length,
                to: visibleRange.to + uniqueCandles.length
            });
        }
    }

    updateCandle(candleData, currentPrice = candleData.close) {
        const timeScale = this.chart.timeScale();
        const visibleRange = timeScale.getVisibleLogicalRange();
        const shouldFollowLatest = this.followLatest || !visibleRange;
        const lastCandle = this.allCandles[this.allCandles.length - 1];
        const previousLastIndex = this.allCandles.length - 1;
        const isNewCandle = !lastCandle || candleData.time > lastCandle.time;

        if (lastCandle && candleData.time === lastCandle.time) {
            this.allCandles[this.allCandles.length - 1] = candleData;
        } else if (isNewCandle) {
            this.allCandles.push(candleData);
        }
        this.series.update(candleData);
        this.updateLatestSmaData();

        this.applyCandleSize(this.currentCandleSize);
        this.updateCurrentPrice(currentPrice);

        if (!visibleRange) {
            return;
        }

        if (shouldFollowLatest && isNewCandle) {
            const addedBars = this.allCandles.length - previousLastIndex - 1;
            timeScale.setVisibleLogicalRange({
                from: visibleRange.from + addedBars,
                to: visibleRange.to + addedBars
            });
        } else {
            timeScale.setVisibleLogicalRange(visibleRange);
            if (!shouldFollowLatest) {
                this.followLatest = false;
            }
        }
    }

    updateCurrentPrice(price) {
        if (this.currentPriceLine) {
            this.series.removePriceLine(this.currentPriceLine);
        }

        this.currentPriceLine = this.series.createPriceLine({
            price,
            color: '#f5c451',
            lineWidth: 1,
            lineStyle: LightweightCharts.LineStyle.Dashed,
            lineVisible: false,
            axisLabelVisible: true
        });
    }
}