const puppeteer = require('puppeteer');

(async () => {
    const browser = await puppeteer.launch({ headless: 'new' });
    const page = await browser.newPage();

    try {
        // Wait for the app to be ready
        await page.goto('http://localhost:5000/workflow-builder/', { waitUntil: 'networkidle2', timeout: 30000 });

        console.log('Page loaded successfully');

        // Wait a bit for Alpine to initialize
        await new Promise(r => setTimeout(r, 2000));

        // Check for signal labels
        const signalLabels = await page.$$('.port-label');
        console.log('Signal labels found:', signalLabels.length);

        // Check for draggable attributes on port labels
        const draggableLabels = await page.$$('.port-label[draggable="true"]');
        console.log('Draggable signal labels:', draggableLabels.length);

        // Check page title
        const title = await page.title();
        console.log('Page title:', title);

        // Check for adapter panel element (should be hidden initially)
        const adapterPanel = await page.$('.adapter-panel');
        console.log('Adapter panel exists:', adapterPanel !== null);

        // Check for signal drag line SVG
        const dragLineSvg = await page.$('.signal-drag-line');
        console.log('Signal drag line SVG exists:', dragLineSvg !== null);

        // Get HTML of port labels to check attributes
        const labelHtml = await page.evaluate(() => {
            const labels = document.querySelectorAll('.port-label.output');
            return Array.from(labels).slice(0, 3).map(l => l.outerHTML);
        });
        console.log('Output port labels HTML:', labelHtml);

        // Check how many nodes we have
        const nodes = await page.$$('.drawflow-node');
        console.log('Nodes found:', nodes.length);

        // Take initial screenshot
        await page.screenshot({ path: 'D:/Source/styloflow/workflow-test.png', fullPage: true });
        console.log('Screenshot saved to workflow-test.png');

        // Test signal dragging interaction
        console.log('\n--- Testing signal drag interaction ---');

        // Get first output signal position
        const outputSignal = await page.$('.port-label.output[data-signal]');
        if (outputSignal) {
            const box = await outputSignal.boundingBox();
            console.log('Output signal position:', box);

            // Simulate mousedown to start drag
            await page.mouse.move(box.x + box.width/2, box.y + box.height/2);
            await page.mouse.down();

            // Wait a moment
            await new Promise(r => setTimeout(r, 100));

            // Check if signal drag became active
            const dragActive = await page.evaluate(() => {
                // Check Alpine state if available
                const el = document.querySelector('[x-data]');
                return el && el.__x && el.__x.$data.signalDrag && el.__x.$data.signalDrag.active;
            });
            console.log('Signal drag active:', dragActive);

            // Take screenshot during drag
            await page.screenshot({ path: 'D:/Source/styloflow/workflow-drag.png', fullPage: true });

            // Release mouse
            await page.mouse.up();
        }

        console.log('\n=== Test completed successfully ===');
    } catch (err) {
        console.error('Test failed:', err.message);
        await page.screenshot({ path: 'D:/Source/styloflow/workflow-error.png' });
    } finally {
        await browser.close();
    }
})();
