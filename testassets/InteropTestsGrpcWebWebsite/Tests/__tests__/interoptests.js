describe('gRPC-Web interop tests', () => {
    test.each(global.__GRPC_WEB_TEST_NAMES__)('Run %s (grpc-web)', async (testName) => {
        var result = await page.evaluate((n) => runTest(n, "GrpcWeb"), testName);
        expect(result).toBe("Success");
    });

    test.each(global.__GRPC_WEB_TEXT_TEST_NAMES__)('Run %s (grpc-web-text)', async (testName) => {
        var result = await page.evaluate((n) => runTest(n, "GrpcWebText"), testName);
        expect(result).toBe("Success");
    });
});