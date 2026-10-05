import assert from "node:assert/strict";
import { test } from "node:test";
import { ManagementBudget, INPUT_BYTES, OUTPUT_BYTES, ObservedRun, parseProposal, type RunSession } from "./envelope.js";
import { MemoryFs } from "./memory-fs.js";

test("management input uses UTF-8 bytes at 32768 and 32769, not characters", () => {
    for (const input of ["x".repeat(INPUT_BYTES), "\u00e9".repeat(INPUT_BYTES / 2)]) {
        const budget = new ManagementBudget();
        assert.equal(Buffer.byteLength(input), INPUT_BYTES);
        assert.equal(budget.admit(input, true), undefined);
        budget.finish(true);
        assert.equal(budget.admit(input + "x", true), "input-limit");
    }
});

test("one in-flight request; unknown cancellation quarantines inference, not local controls", () => {
    const budget = new ManagementBudget();
    assert.equal(budget.admit("synthetic", true), undefined);
    assert.equal(budget.admit("synthetic", true), "busy");
    assert.match(budget.deterministicStatus(), /Cancel/);
    budget.finish(false);
    assert.equal(budget.admit("synthetic", true), "busy");
    assert.match(budget.deterministicStatus(), /ledger/);
});

test("30 remote calls per rolling hour, including failures; exact expiry and local-only gate", () => {
    let now = 100;
    const budget = new ManagementBudget(() => now);
    for (let index = 0; index < 30; index++) {
        assert.equal(budget.admit("synthetic", true), undefined);
        budget.finish(true);
    }
    assert.equal(budget.admit("synthetic", true), "quota");
    now += 3_599_999;
    assert.equal(budget.admit("synthetic", true), "quota");
    now++;
    assert.equal(budget.admit("synthetic", true), undefined);
    budget.finish(true);
    assert.equal(budget.admit("synthetic", true, true), "local-only");
});

test("typed management output accepts only proposal facts, never approval or execution authority", () => {
    const value = { kind: "status", sessionId: "synthetic", ledgerRevision: 1, summary: "waiting" };
    assert.deepEqual(parseProposal(JSON.stringify(value)), value);
    for (const invalid of [
        "not-json", "null", "[]", JSON.stringify({ ...value, approved: true }),
        JSON.stringify({ ...value, kind: "execute" }),
        JSON.stringify({ ...value, ledgerRevision: -1 }),
        JSON.stringify({ ...value, ledgerRevision: 1.5 }),
        JSON.stringify({ ...value, ledgerRevision: "1" }),
        JSON.stringify({ ...value, sessionId: "" }),
    ]) assert.throws(() => parseProposal(invalid));
    assert.equal(OUTPUT_BYTES, 4096);
});

test("session FS writes are volatile, isolated, and explicitly cleared", async () => {
    const first = new MemoryFs();
    const second = new MemoryFs();
    await first.writeFile("Q:\\synthetic\\events.jsonl", "synthetic");
    await first.appendFile("Q:\\synthetic\\events.jsonl", "-continued");
    assert.equal(await first.readFile("Q:\\synthetic\\events.jsonl"), "synthetic-continued");
    assert.equal(await second.exists("Q:\\synthetic\\events.jsonl"), false);
    assert.equal((await first.stat("Q:\\synthetic\\events.jsonl")).size, 19);
    first.clear();
    assert.equal(await first.exists("Q:\\synthetic\\events.jsonl"), false);
    await assert.rejects(first.readFile("Q:\\synthetic\\events.jsonl"), { code: "ENOENT" });
});

test("host deadline does not wait for a stalled SDK send or abort acknowledgement", async () => {
    let denied = false;
    const stalled: RunSession = {
        send: () => new Promise<string>(() => {}),
        on: () => () => {},
        abort: () => new Promise<void>(() => {}),
    };
    const run = new ObservedRun(stalled, () => { denied = true; }, OUTPUT_BYTES, 25);
    try {
        run.send("synthetic");
        const result = await run.result;
        assert.equal(result.outcome, "deadline");
        assert.ok(result.elapsedMs >= 20 && result.elapsedMs < 750);
        assert.equal(result.content, "");
        assert.equal(denied, true);
        assert.equal(run.abortAcknowledged, false);
    } finally {
        run.dispose();
    }
});
