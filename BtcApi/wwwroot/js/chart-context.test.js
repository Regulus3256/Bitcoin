import { CandleChartManager } from './chart.js?v=20261113-chart-resize';

export function runChartContextTests() {
    const container = document.createElement('div');
    document.body.append(container);
    const manager = Object.create(CandleChartManager.prototype);
    let time = 3600;
    let selectedTime = null;
    manager.container = container;
    manager.allCandles = [{ time: 3600 }];
    manager.chart = { timeScale: () => ({ coordinateToTime: () => time }) };
    manager.setContextMenuHandler(value => { selectedTime = value; });
    manager.bindContextMenu();
    const menu = container.querySelector('.chart-context-menu');
    const button = menu.querySelector('button');

    function assert(condition, message) {
        if (!condition) throw new Error(message);
    }

    try {
        const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 10, clientY: 10 });
        container.dispatchEvent(event);
        assert(event.defaultPrevented && !menu.hidden, 'candle opens the menu');
        button.click();
        assert(selectedTime === 3600 && menu.hidden, 'selection invokes the handler and closes the menu');

        time = null;
        const emptyEvent = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
        container.dispatchEvent(emptyEvent);
        assert(menu.hidden && !emptyEvent.defaultPrevented, 'empty area keeps the native menu');

        time = 3600;
        container.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        assert(menu.hidden, 'Escape closes the menu');
        return '3 chart context tests passed';
    } finally {
        container.remove();
    }
}