import assert from "node:assert/strict";
import { test } from "node:test";
import { SyntheticProvider } from "./provider.js";

test("host provider emits synthetic streamed text and records only loopback requests", { timeout: 10_000 }, async () => {
    const provider = new SyntheticProvider();
    await provider.start();
    try {
        const url = new URL(provider.baseUrl);
        assert.equal(url.hostname, "127.0.0.1");
        assert.equal(url.protocol, "http:");
        assert.notEqual(url.port, "0");
        provider.enqueue({ kind: "text", chunks: ["synthetic", "-response"] });
        const response = await fetch(`${provider.baseUrl}/chat/completions`, {
            method: "POST", body: JSON.stringify({ input: "synthetic" }),
        });
        await provider.waitForRequests(1);
        assert.equal(response.status, 200);
        assert.equal(response.headers.get("content-type"), "text/event-stream");
        const content = await response.text();
        assert.match(content, /"content":"synthetic"/);
        assert.match(content, /"content":"-response"/);
        assert.match(content, /"finish_reason":"stop"/);
        assert.match(content, /data: \[DONE\]/);
        assert.deepEqual(provider.requests, [{
            body: '{"input":"synthetic"}', credentialPresent: false, path: "/v1/chat/completions",
        }]);
    } finally {
        await provider.stop();
    }
});

test("host provider emits scripted tool proposals without executing tools", { timeout: 10_000 }, async () => {
    const provider = new SyntheticProvider();
    await provider.start();
    try {
        provider.enqueue({ kind: "tool", name: "synthetic-counter", arguments: '{"value":1}' });
        const response = await fetch(`${provider.baseUrl}/chat/completions`, { method: "POST", body: "{}" });
        const content = await response.text();
        assert.match(content, /synthetic-counter/);
        assert.match(content, /"finish_reason":"tool_calls"/);
        assert.match(content, /data: \[DONE\]/);
        assert.equal(provider.requests.length, 1);
    } finally {
        await provider.stop();
    }
});

test("host provider returns scripted 401, 429, 500 and rejects unscripted requests", { timeout: 10_000 }, async () => {
    const provider = new SyntheticProvider();
    await provider.start();
    try {
        provider.enqueue({ kind: "error", status: 401 }, { kind: "error", status: 429 },
            { kind: "error", status: 500 });
        for (const { status, message } of [
            { status: 401, message: "Synthetic HTTP 401" },
            { status: 429, message: "Synthetic HTTP 429" },
            { status: 500, message: "Synthetic HTTP 500" },
            { status: 500, message: "Unscripted synthetic request" },
        ]) {
            const response = await fetch(`${provider.baseUrl}/chat/completions`, { method: "POST", body: "{}" });
            assert.equal(response.status, status);
            assert.deepEqual(await response.json(), { error: { message } });
        }
        assert.equal(provider.requests.length, 4);
    } finally {
        await provider.stop();
    }
});

test("host provider shutdown closes held responses and its listener", { timeout: 10_000 }, async () => {
    const provider = new SyntheticProvider();
    await provider.start();
    let stopped = false;
    try {
        provider.enqueue({ kind: "hold" });
        const response = await fetch(`${provider.baseUrl}/chat/completions`, { method: "POST", body: "{}" });
        await provider.waitForRequests(1);
        const body = response.text();
        const failedBody = assert.rejects(body);
        await provider.stop();
        stopped = true;
        await failedBody;
        assert.ok(provider.responses.every(item => item.destroyed));
        await assert.rejects(fetch(`${provider.baseUrl}/chat/completions`, { signal: AbortSignal.timeout(2_000) }));
    } finally {
        if (!stopped) await provider.stop();
    }
});
