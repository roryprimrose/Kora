import assert from "node:assert/strict";
import { setTimeout as delay } from "node:timers/promises";
import { performance } from "node:perf_hooks";
import { defineTool, type CopilotSession, type SessionHooks } from "@github/copilot-sdk";
import { ManagementBudget, ObservedRun, managementRequest, OUTPUT_BYTES } from "./envelope.js";
import {
    RuntimeFixture, DENIED_INITIAL, DENIED_RESULT, DENIED_COLLECTION, AUTH_SENTINEL,
} from "./runtime.js";
import { SyntheticProvider } from "./provider.js";

export interface Evidence {
    id: string;
    status: "PASS" | "FAIL" | "BLOCKED";
    detail: string;
    measurements: Record<string, number | string | boolean | string[]>;
}

async function fixture(
    action: (runtime: RuntimeFixture, provider: SyntheticProvider) => Promise<Evidence[]>,
): Promise<Evidence[]> {
    const provider = new SyntheticProvider();
    const runtime = new RuntimeFixture();
    await provider.start();
    try {
        try {
            await runtime.start();
            return await action(runtime, provider);
        } finally {
            await runtime.stop();
        }
    } finally {
        await provider.stop();
    }
}

async function observed(session: CopilotSession, runtime: RuntimeFixture, input: string) {
    const run = new ObservedRun(session, () => runtime.boundary.cancel(session.sessionId));
    try {
        run.send(input);
        return await run.result;
    } finally {
        run.dispose();
    }
}

function toolNames(body: string): string[] {
    const parsed: unknown = JSON.parse(body);
    assert.ok(typeof parsed === "object" && parsed !== null);
    if (!("tools" in parsed)) return [];
    assert.ok(Array.isArray(parsed.tools));
    return parsed.tools.map((tool: unknown) => {
        assert.ok(typeof tool === "object" && tool !== null && "function" in tool);
        assert.ok(typeof tool.function === "object" && tool.function !== null && "name" in tool.function);
        assert.equal(typeof tool.function.name, "string");
        return String(tool.function.name);
    });
}

export async function initialContext(): Promise<Evidence[]> {
    return fixture(async (runtime, provider) => {
        const status = await runtime.client.getStatus();
        assert.equal(status.version, "1.0.90");
        let submitted = 0;
        let transformed = 0;
        provider.enqueue({ kind: "text", chunks: ["synthetic ", "ready"] });
        const session = await runtime.session(provider, {
            hooks: {
                onUserPromptSubmitted: input => {
                    submitted++;
                    return { modifiedPrompt: input.prompt.replace(DENIED_INITIAL, "[host context denied]") };
                },
                onUserPromptTransformed: input => {
                    transformed++;
                    assert.ok(!input.transformedPrompt.includes(DENIED_INITIAL));
                },
            },
        });
        const answer = await observed(session, runtime, `R02_APPROVED_INITIAL ${DENIED_INITIAL}`);
        assert.equal(answer.outcome, "completed");
        assert.equal(answer.content, "synthetic ready");
        assert.equal(submitted, 1);
        assert.equal(transformed, 1);
        assert.equal(provider.requests.length, 1);
        const request = provider.requests[0]!;
        assert.ok(request.body.includes("R02_APPROVED_INITIAL"));
        assert.ok(!request.body.includes(DENIED_INITIAL));
        assert.ok(!request.body.includes(DENIED_COLLECTION));
        assert.ok(!request.body.includes(AUTH_SENTINEL));
        assert.ok(request.credentialPresent);
        assert.deepEqual(toolNames(request.body), []);
        const events = runtime.events.get(session.sessionId)!;
        const deltas = events.filter(event => event.type === "assistant.message_delta").length;
        assert.ok(deltas >= 2);
        assert.ok(events.some(event => event.type === "session.idle"));
        const disk = await runtime.diskObservations();
        assert.deepEqual(disk.markerFiles, []);
        assert.ok(runtime.stores[0]!.writes > 0);
        return [
            {
                id: "initial-context", status: "PASS", detail: "Actual SDK prompt hooks filtered denied context before captured HTTP egress.",
                measurements: { deniedOutbound: 0, requests: 1, submittedHooks: submitted, transformedHooks: transformed },
            },
            {
                id: "streaming-and-auth-transport", status: "PASS",
                detail: "Actual SSE deltas and idle event; synthetic auth is present only in a header, absent from model payload.",
                measurements: { deltas, runtime: status.version, protocol: status.protocolVersion },
            },
            {
                id: "collection-and-session-persistence", status: "PASS",
                detail: "No advertised built-ins or discovered instruction canary; session files routed to volatile host FS, with no context markers found in the scoped runtime directory.",
                measurements: { advertisedTools: 0, deniedCollectionOutbound: 0, volatileWrites: runtime.stores[0]!.writes, diskFiles: disk.files, diskMarkerFiles: 0 },
            },
        ];
    });
}

export async function deniedTool(): Promise<Evidence[]> {
    return fixture(async (runtime, provider) => {
        let effects = 0;
        let gates = 0;
        provider.enqueue({ kind: "tool", name: "test_effect" }, { kind: "text", chunks: ["denied"] });
        const session = await runtime.session(provider, {
            availableTools: ["custom:test_effect"],
            tools: [defineTool("test_effect", {
                parameters: { type: "object", properties: {}, additionalProperties: false },
                skipPermission: true, defer: "never",
                handler: () => { effects++; return "synthetic effect receipt"; },
            })],
            hooks: {
                onPreToolUse: () => {
                    gates++;
                    return { permissionDecision: "deny", permissionDecisionReason: "Host denied synthetic effect" };
                },
            },
        });
        const result = await observed(session, runtime, "R02_APPROVED_TOOL_DENIAL");
        assert.equal(result.outcome, "completed");
        assert.equal(gates, 1);
        assert.equal(effects, 0);
        assert.deepEqual(toolNames(provider.requests[0]!.body), ["test_effect"]);
        assert.equal(provider.requests.length, 2);
        return [{
            id: "denied-tool", status: "PASS", detail: "Actual SDK pre-tool gate denied the proposal before its harmless counter handler.",
            measurements: { deniedEffects: effects, preToolGates: gates, requests: provider.requests.length },
        }];
    });
}

export async function toolResult(): Promise<Evidence[]> {
    return fixture(async (runtime, provider) => {
        let effects = 0;
        let postHooks = 0;
        provider.enqueue({ kind: "tool", name: "test_read" }, { kind: "text", chunks: ["safe result"] });
        const session = await runtime.session(provider, {
            availableTools: ["custom:test_read"],
            tools: [defineTool("test_read", {
                parameters: { type: "object", properties: {}, additionalProperties: false },
                skipPermission: true, defer: "never",
                handler: () => {
                    effects++;
                    return { resultType: "success", textResultForLlm: `${DENIED_RESULT} R02_APPROVED_RECEIPT` };
                },
            })],
            hooks: {
                onPreToolUse: () => ({ permissionDecision: "allow" }),
                onPostToolUse: input => {
                    postHooks++;
                    assert.ok(input.toolResult.textResultForLlm.includes(DENIED_RESULT));
                    return { modifiedResult: { resultType: "success", textResultForLlm: "R02_APPROVED_RECEIPT; context withheld" } };
                },
            },
        });
        const result = await observed(session, runtime, "R02_APPROVED_TOOL_RESULT");
        assert.equal(result.outcome, "completed");
        assert.equal(effects, 1);
        assert.equal(postHooks, 1);
        assert.equal(provider.requests.length, 2);
        assert.ok(provider.requests[1]!.body.includes("R02_APPROVED_RECEIPT"));
        assert.ok(provider.requests.every(request => !request.body.includes(DENIED_RESULT)));
        return [{
            id: "subsequent-tool-result", status: "PASS",
            detail: "Actual post-tool hook withheld denied content while retaining a truthful synthetic read receipt.",
            measurements: { admittedReads: effects, postHooks, requests: 2, deniedOutbound: 0 },
        }];
    });
}

export async function failedToolResult(): Promise<Evidence[]> {
    return fixture(async (runtime, provider) => {
        let successHooks = 0;
        let failureHooks = 0;
        const hooks: SessionHooks = {
            onPreToolUse: () => ({ permissionDecision: "allow" }),
            onPostToolUse: () => {
                successHooks++;
                return { modifiedResult: { resultType: "failure", textResultForLlm: "withheld" } };
            },
            onPostToolUseFailure: () => {
                failureHooks++;
                return { additionalContext: "Do not transmit rejected content" };
            },
            onErrorOccurred: () => ({ errorHandling: "abort" }),
        };
        provider.enqueue({ kind: "tool", name: "test_failure" });
        const session = await runtime.session(provider, {
            availableTools: ["custom:test_failure"],
            tools: [defineTool("test_failure", {
                skipPermission: true, defer: "never",
                handler: () => ({ resultType: "failure", textResultForLlm: DENIED_RESULT, error: DENIED_RESULT }),
            })],
            hooks,
        });
        const result = await observed(session, runtime, "R02_APPROVED_FAILURE");
        assert.equal(result.outcome, "provider-error");
        assert.equal(successHooks, 0);
        assert.equal(failureHooks, 1);
        assert.ok(runtime.boundary.observed.some(request => request.blocked));
        assert.equal(provider.requests.length, 1);
        assert.ok(provider.requests.every(request => !request.body.includes(DENIED_RESULT)));
        return [{
            id: "hook-only-failed-result-egress", status: "FAIL",
            detail: "Success post-tool hook does not mediate failure output. Failure hook adds guidance only. The final request boundary blocked the unsafe continuation; hook-only integration must remain disabled.",
            measurements: { successHooks, failureHooks, interceptedBlocked: runtime.boundary.observed.filter(request => request.blocked).length, deniedOutbound: 0, requests: 1 },
        }, {
            id: "final-boundary-failed-result-egress", status: "PASS",
            detail: "Supported experimental model request handler observed and rejected the unredacted failed result before forwarding any HTTP payload to the synthetic provider.",
            measurements: { deniedOutbound: 0, blockedContinuations: 1 },
        }];
    });
}

export async function providerErrors(): Promise<Evidence[]> {
    return fixture(async (runtime, provider) => {
        const outcomes: string[] = [];
        for (const status of [401, 429, 500]) {
            provider.enqueue({ kind: "error", status });
            const session = await runtime.session(provider);
            runtime.boundary.managementSessions.add(session.sessionId);
            const result = await managementRequest(new ManagementBudget(), session, "R02_APPROVED_ERROR",
                () => runtime.boundary.cancel(session.sessionId));
            assert.equal(result.outcome, "provider-error");
            assert.ok(result.fallback);
            assert.ok(!result.proposal);
            assert.ok(runtime.events.get(session.sessionId)!.some(event => event.type === "session.error"));
            outcomes.push(`${status}:provider-error`);
        }
        assert.equal(provider.requests.length, 3);
        return [{
            id: "authentication-throttling-errors", status: "PASS",
            detail: "Synthetic 401/429/500 produce explicit SDK errors and deterministic fallback; host boundary prevents any second management HTTP attempt.",
            measurements: { outcomes, forwardedRequests: 3, blockedRetries: runtime.boundary.observed.filter(request => request.blocked).length },
        }];
    });
}

export async function independentLanes(): Promise<Evidence[]> {
    return fixture(async (runtime, executionProvider) => {
        const managementProvider = new SyntheticProvider();
        await managementProvider.start();
        try {
            let release!: () => void;
            const blockedTool = new Promise<void>(resolve => { release = resolve; });
            let started!: () => void;
            const toolStarted = new Promise<void>(resolve => { started = resolve; });
            let completedToolEffects = 0;
            executionProvider.enqueue(
                { kind: "tool", name: "test_wait" },
                { kind: "hold" },
                { kind: "text", chunks: ["late synthetic execution answer"] },
            );
            const execution = await runtime.session(executionProvider, {
                availableTools: ["custom:test_wait"],
                tools: [defineTool("test_wait", {
                    skipPermission: true, defer: "never",
                    handler: async () => {
                        started();
                        await blockedTool;
                        completedToolEffects++;
                        return "R02_APPROVED_LATE_TOOL_RECEIPT";
                    },
                })],
                hooks: { onPreToolUse: () => ({ permissionDecision: "allow" }) },
            });
            const executionRun = new ObservedRun(execution, () => runtime.boundary.cancel(execution.sessionId));
            let secondRun: ObservedRun | undefined;
            try {
                executionRun.send("R02_APPROVED_EXECUTION_PRIVATE");
                const toolTimeout = new AbortController();
                try {
                    await Promise.race([toolStarted, delay(10_000, undefined, { signal: toolTimeout.signal })
                        .then(() => { throw new Error("Test tool did not start"); })]);
                } finally {
                    toolTimeout.abort();
                }
                const secondExecution = await runtime.session(executionProvider);
                secondRun = new ObservedRun(secondExecution, () => runtime.boundary.cancel(secondExecution.sessionId));
                secondRun.send("R02_APPROVED_SECOND_EXECUTION_PRIVATE");
                await executionProvider.waitForRequests(2);
                const management = await runtime.session(managementProvider);
                runtime.boundary.managementSessions.add(management.sessionId);
                managementProvider.enqueue({
                    kind: "text",
                    chunks: ['{"kind":"status","sessionId":"synthetic","ledgerRevision":1,"summary":"waiting"}'],
                });
                const acknowledged = performance.now();
                const managed = await managementRequest(new ManagementBudget(), management, "R02_APPROVED_MANAGEMENT_ONLY",
                    () => runtime.boundary.cancel(management.sessionId));
                const managementMs = Math.round(performance.now() - acknowledged);
                assert.equal(managed.outcome, "completed");
                assert.equal(completedToolEffects, 0);
                assert.equal(secondRun.outcome, undefined);
                assert.notEqual(execution.sessionId, management.sessionId);
                assert.notEqual(secondExecution.sessionId, management.sessionId);
                assert.ok(managementProvider.requests.every(request => !request.body.includes("EXECUTION_PRIVATE")));
                assert.ok(executionProvider.requests.every(request => !request.body.includes("MANAGEMENT_ONLY")));
                assert.deepEqual(toolNames(managementProvider.requests[0]!.body), []);
                executionRun.cancel();
                secondRun.cancel();
                assert.equal((await executionRun.result).outcome, "cancelled");
                assert.equal((await secondRun.result).outcome, "cancelled");
                assert.equal(executionRun.acceptedContent, "");
                release();
                await delay(700);
                assert.equal(completedToolEffects, 1);
                assert.equal(executionProvider.requests.length, 2);
                return [{
                    id: "independent-lanes-and-provider-isolation", status: "PASS",
                    detail: "Management completed on a separate provider/session while one execution tool and a second execution inference were blocked; no task context/tool leakage. Three independent SDK conversations, not three production execution slots.",
                    measurements: { managementMs, executionConversations: 2, executionRequests: 2, managementRequests: 1, managementTools: 0 },
                }, {
                    id: "truthful-cancellation", status: "PASS",
                    detail: "Host cancellation returned immediately, suppressed late egress, and did not claim rollback: already-admitted non-cooperative tool completed after cancellation.",
                    measurements: { abortAcknowledged: executionRun.abortAcknowledged, abortFailed: executionRun.abortFailed, admittedLateEffects: completedToolEffects, lateOutbound: 0, suppressedLateEvents: executionRun.suppressedLateEvents, toolOutcomeAtCancel: "unknown" },
                }];
            } finally {
                release();
                executionRun.dispose();
                secondRun?.cancel();
                secondRun?.dispose();
            }
        } finally {
            await managementProvider.stop();
        }
    });
}

export async function managementEnvelope(): Promise<Evidence[]> {
    return fixture(async (runtime, provider) => {
        const session = await runtime.session(provider);
        runtime.boundary.managementSessions.add(session.sessionId);
        provider.enqueue({ kind: "text", chunks: ["x".repeat(OUTPUT_BYTES), "x"] });
        const tooLarge = await managementRequest(new ManagementBudget(), session, "R02_APPROVED_OUTPUT_BOUND",
            () => runtime.boundary.cancel(session.sessionId));
        assert.equal(tooLarge.outcome, "output-limit");
        assert.ok(!tooLarge.proposal);
        const timeoutSession = await runtime.session(provider);
        runtime.boundary.managementSessions.add(timeoutSession.sessionId);
        provider.enqueue({ kind: "hold" });
        const deadline = await managementRequest(new ManagementBudget(), timeoutSession, "R02_APPROVED_DEADLINE",
            () => runtime.boundary.cancel(timeoutSession.sessionId));
        assert.equal(deadline.outcome, "deadline");
        assert.ok(deadline.elapsedMs! >= 14_900 && deadline.elapsedMs! <= 15_750);
        assert.ok(deadline.fallback);
        assert.equal(provider.requests.length, 2);
        const overflowSession = await runtime.session(provider);
        const before = provider.requests.length;
        const overflow = await managementRequest(new ManagementBudget(), overflowSession, "x".repeat(32 * 1024 + 1),
            () => runtime.boundary.cancel(overflowSession.sessionId));
        assert.equal(overflow.outcome, "input-limit");
        assert.equal(provider.requests.length, before);
        return [{
            id: "management-real-deadline-output", status: "PASS",
            detail: "Actual SDK streamed output overflow is rejected; real held HTTP inference returns fallback at 15 seconds without awaiting the provider or retrying.",
            measurements: { deadlineMs: deadline.elapsedMs!, deadlineLimitMs: 15_000, inputLimitBytes: 32 * 1024, outputLimitBytes: OUTPUT_BYTES, forwardedRequests: 2, oversizedInputRequests: 0 },
        }];
    });
}

export async function exactManagementBounds(): Promise<Evidence[]> {
    return fixture(async (runtime, provider) => {
        const template = await runtime.session(provider);
        const minimal = '{"kind":"status","sessionId":"synthetic","ledgerRevision":0,"summary":""}';
        const exactOutput = minimal.slice(0, -2) + "\u00e9".repeat((OUTPUT_BYTES - Buffer.byteLength(minimal) - 1) / 2)
            + "x" + minimal.slice(-2);
        assert.equal(Buffer.byteLength(exactOutput), OUTPUT_BYTES);
        provider.enqueue({ kind: "text", chunks: [minimal] });
        assert.equal((await observed(template, runtime, "x")).outcome, "completed");
        const overhead = Buffer.byteLength(provider.requests[0]!.body) - 1;
        const exactInput = "x".repeat(32 * 1024 - overhead);
        const exactSession = await runtime.session(provider);
        runtime.boundary.managementSessions.add(exactSession.sessionId);
        provider.enqueue({ kind: "text", chunks: [exactOutput] });
        const exact = await managementRequest(new ManagementBudget(), exactSession, exactInput,
            () => runtime.boundary.cancel(exactSession.sessionId));
        assert.equal(exact.outcome, "completed");
        assert.equal(Buffer.byteLength(provider.requests[1]!.body), 32 * 1024);
        assert.equal(Buffer.byteLength(JSON.stringify(exact.proposal)), OUTPUT_BYTES);
        const overSession = await runtime.session(provider);
        runtime.boundary.managementSessions.add(overSession.sessionId);
        const over = await managementRequest(new ManagementBudget(), overSession, exactInput + "x",
            () => runtime.boundary.cancel(overSession.sessionId));
        assert.equal(over.outcome, "provider-error");
        assert.ok(runtime.boundary.observed.some(request => request.bytes === 32 * 1024 + 1 && request.blocked));
        assert.equal(provider.requests.length, 2);
        return [{
            id: "management-exact-byte-boundaries", status: "PASS",
            detail: "Actual outbound JSON body accepted at 32768 bytes and rejected at 32769 before provider egress. Typed multibyte UTF-8 output accepted at exactly 4096 bytes; overflow covered separately.",
            measurements: { acceptedInputBytes: 32768, rejectedInputBytes: 32769, acceptedOutputBytes: 4096, overLimitForwarded: 0 },
        }];
    });
}

export async function unadvertisedTool(): Promise<Evidence[]> {
    return fixture(async (runtime, provider) => {
        provider.enqueue({ kind: "tool", name: "powershell", arguments: '{"command":"Write-Output synthetic-disabled"}' },
            { kind: "text", chunks: ["unavailable tool"] });
        const session = await runtime.session(provider);
        const result = await observed(session, runtime, "R02_APPROVED_UNAVAILABLE_TOOL");
        assert.equal(result.outcome, "completed");
        assert.deepEqual(toolNames(provider.requests[0]!.body), []);
        assert.ok(provider.requests.every(request => !request.body.includes(DENIED_COLLECTION)));
        const events = runtime.events.get(session.sessionId)!;
        const completed = events.filter(event => event.type === "tool.execution_complete");
        assert.equal(completed.length, 1);
        assert.ok(completed.every(event => event.type === "tool.execution_complete" && !event.data.success));
        const diskAfter = await runtime.diskObservations();
        assert.deepEqual(diskAfter.markerFiles, []);
        return [{
            id: "unadvertised-built-in", status: "PASS",
            detail: "A synthetic model proposal for excluded PowerShell produced a failed tool receipt, not execution. Runtime initialization still writes PowerShell startup metadata, so this is not an OS no-write claim.",
            measurements: { advertisedTools: 0, failedToolReceipts: completed.length, successfulBuiltInResults: 0, deniedCollectionOutbound: 0, diskFiles: diskAfter.files },
        }];
    });
}

export async function admissionBudgets(): Promise<Evidence[]> {
    let now = 100;
    const budget = new ManagementBudget(() => now);
    assert.equal(budget.admit("\u00e9".repeat(16 * 1024) + "x", true), "input-limit");
    assert.equal(budget.admit("synthetic", true, true), "local-only");
    for (let index = 0; index < 30; index++) {
        assert.equal(budget.admit("synthetic", true), undefined);
        assert.equal(budget.admit("synthetic", true), "busy");
        budget.finish(true);
    }
    assert.equal(budget.admit("synthetic", true), "quota");
    now += 3_599_999;
    assert.equal(budget.admit("synthetic", true), "quota");
    now++;
    assert.equal(budget.admit("synthetic", true), undefined);
    budget.finish(false);
    assert.equal(budget.admit("synthetic", true), "busy");
    assert.match(budget.deterministicStatus(), /Cancel/);
    return [{
        id: "management-host-admission", status: "PASS",
        detail: "Deterministic host tests: one in-flight request, 30 calls per rolling hour, exact expiry, local-only denial and fail-closed quarantine after unconfirmed termination. Not provider quota evidence.",
        measurements: { remoteCallsPerHour: 30, inFlightLimit: 1, rollingWindowMs: 3_600_000, oversizedUtf8InputBytes: 32769 },
    }];
}

export const cases = [
    initialContext, deniedTool, toolResult, failedToolResult,
    providerErrors, independentLanes, managementEnvelope, exactManagementBounds, unadvertisedTool, admissionBudgets,
];
