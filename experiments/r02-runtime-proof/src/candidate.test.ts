import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { readFile } from "node:fs/promises";
import { promisify } from "node:util";
import { test } from "node:test";
import { assertCandidateHost, candidate, validateCandidate } from "./candidate.js";

const exactHost = process.platform === "win32" && process.arch === "x64"
    && process.version === candidate.node;

test("candidate host identity accepts only the recorded Windows x64 Node pin", () => {
    assert.doesNotThrow(() => assertCandidateHost("win32", "x64", candidate.node));
    for (const [platform, arch, node] of [
        ["linux", "x64", candidate.node],
        ["win32", "arm64", candidate.node],
        ["win32", "x64", "v24.21.0"],
        ["win32", "x64", "24.16.0"],
        ["", "", ""],
    ] as const) {
        assert.throws(() => assertCandidateHost(platform, arch, node), /exactly Node 24\.16\.0/);
    }
});

test("mismatched actual host is rejected before installed runtime inspection", { skip: exactHost }, async () => {
    await assert.rejects(validateCandidate(), /exactly Node 24\.16\.0/);
});

test("proof preflight preserves historical evidence on a mismatched host", { skip: exactHost }, async () => {
    const before = await readFile("evidence\\results.json");
    await assert.rejects(
        promisify(execFile)(process.execPath, ["dist\\proof.js"], { timeout: 10_000 }),
        (error: unknown) => {
            assert.ok(error instanceof Error && "code" in error && "stderr" in error);
            assert.equal(error.code, 1);
            assert.match(String(error.stderr), /exactly Node 24\.16\.0/);
            return true;
        },
    );
    assert.deepEqual(await readFile("evidence\\results.json"), before);
});
