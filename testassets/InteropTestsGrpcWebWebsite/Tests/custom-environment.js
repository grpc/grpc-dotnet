const JestPuppeteerEnvironment = require("jest-environment-puppeteer").TestEnvironment;
const expect = require('expect-puppeteer').expect;

class CustomEnvironment extends JestPuppeteerEnvironment {
    // Load page and get test names to run
    async setup() {
        await super.setup();

        console.log('Calling gRPC-Web client app');

        var page = this.global.page;
        await page.goto('http:localhost:8081', { waitUntil: 'networkidle0' });

        // Wait for Blazor to finish loading
        await expect(page).toMatchTextContent('gRPC-Web interop tests');

        // Get test names
        this.global.__GRPC_WEB_TEST_NAMES__ = await page.evaluate(() => getTestNames('GrpcWeb'));
        this.global.__GRPC_WEB_TEXT_TEST_NAMES__ = await page.evaluate(() => getTestNames('GrpcWebText'));
   }

    async teardown() {
        await super.teardown();
    }
}

module.exports = CustomEnvironment
