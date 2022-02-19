// custom-environment.js
function _interopDefault(ex) { return (ex && (typeof ex === 'object') && 'default' in ex) ? ex['default'] : ex; }

const PuppeteerEnvironment = require('jest-environment-puppeteer');
const expect = _interopDefault(require('expect-puppeteer'));

class CustomEnvironment extends PuppeteerEnvironment {
    // Load page and get test names to run
    async setup() {
        await super.setup();

        console.log('Calling gRPC-Web client app');

        var page = this.global.page;
        await page.goto('http:localhost:8081', { waitUntil: 'networkidle0' });

        for (var property in expect) {
            console.log(`${property}: ${expect[property]}`);
        }

        // Wait for Blazor to finish loading
        await expect(page).toMatch('gRPC-Web interop tests');

        // Get test names
        this.global.__GRPC_WEB_TEST_NAMES__ = await page.evaluate(() => getTestNames('GrpcWeb'));
        this.global.__GRPC_WEB_TEXT_TEST_NAMES__ = await page.evaluate(() => getTestNames('GrpcWebText'));
   }

    async teardown() {
        await super.teardown();
    }
}

module.exports = CustomEnvironment