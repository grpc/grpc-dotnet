const JestPuppeteerEnvironment = require("jest-environment-puppeteer").TestEnvironment;

// expect-puppeteer requires jest's expect to be on the global object.
// If the global object isn't populated then there is a null reference error in expect-puppeteer.
global.expect = require('expect').expect;
const expect = require('expect-puppeteer').expect;

class CustomEnvironment extends JestPuppeteerEnvironment {
    // Load page and get test names to run
    async setup() {
        await super.setup();

        // Workaround puppeteer bug: https://github.com/argos-ci/jest-puppeteer/issues/586
        if (this.global.context.isIncognito === undefined) {
            this.global.context.isIncognito = () => false;
        }

        console.log('Calling gRPC-Web client app');

        var page = this.global.page;
        await page.goto('http://localhost:8081', { waitUntil: 'networkidle0' });

        // Wait for Blazor to finish loading.
        await expect(page).toMatchTextContent('gRPC-Web interop tests');

        // Get test names.
        this.global.__GRPC_WEB_TEST_NAMES__ = await page.evaluate(() => getTestNames('GrpcWeb'));
        this.global.__GRPC_WEB_TEXT_TEST_NAMES__ = await page.evaluate(() => getTestNames('GrpcWebText'));
   }

    async teardown() {
        await super.teardown();
    }
}

module.exports = CustomEnvironment
