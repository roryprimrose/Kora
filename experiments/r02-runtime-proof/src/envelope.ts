import { performance } from "node:perf_hooks";
import type { MessageOptions, SessionEvent } from "@github/copilot-sdk";

export const INPUT_BYTES = 32 * 1024;
export const OUTPUT_BYTES = 4 * 1024;
export const DEADLINE_MS = 15_000;
export const CALLS_PER_HOUR = 30;

export type Outcome = "completed" | "provider-error" | "cancelled" | "deadline"
    | "input-limit" | "output-limit" | "invalid-output" | "quota" | "busy" | "local-only";

export interface Proposal {
    kind: "status";
    sessionId: string;
    ledgerRevision: number;
    summary: string;
}

export interface RunSession {
    send(options: MessageOptions): Promise<string>;
    on(handler: (event: SessionEvent) => void): () => void;
    abort(): Promise<void>;
}

export function parseProposal(content: string): Proposal {
    const value: unknown = JSON.parse(content);
    if (typeof value !== "object" || value === null
        || !("kind" in value) || value.kind !== "status"
        || !("sessionId" in value) || typeof value.sessionId !== "string" || !value.sessionId
        || !("ledgerRevision" in value) || !Number.isSafeInteger(value.ledgerRevision)
        || typeof value.ledgerRevision !== "number" || value.ledgerRevision < 0
        || !("summary" in value) || typeof value.summary !== "string"
        || Object.keys(value).sort().join(",") !== "kind,ledgerRevision,sessionId,summary") {
        throw new Error("Invalid synthetic management proposal");
    }
    return {
        kind: value.kind, sessionId: value.sessionId,
        ledgerRevision: value.ledgerRevision, summary: value.summary,
    };
}

export class ManagementBudget {
    private inFlight = false;
    private quarantined = false;
    private readonly calls: number[] = [];

    constructor(private readonly now: () => number = Date.now) {}

    admit(input: string, remote: boolean, localOnly = false): Outcome | undefined {
        if (remote && localOnly) return "local-only";
        if (Buffer.byteLength(input) > INPUT_BYTES) return "input-limit";
        if (this.inFlight || this.quarantined) return "busy";
        const now = this.now();
        while (this.calls[0] !== undefined && this.calls[0] <= now - 3_600_000) this.calls.shift();
        if (remote && this.calls.length >= CALLS_PER_HOUR) return "quota";
        if (remote) this.calls.push(now);
        this.inFlight = true;
        return undefined;
    }

    finish(modelSettled: boolean): void {
        this.inFlight = false;
        this.quarantined ||= !modelSettled;
    }

    deterministicStatus(): string {
        return "Synthetic ledger remains available; choose Queue, Replace current, or Cancel explicitly.";
    }
}

export class ObservedRun {
    readonly started = performance.now();
    readonly result: Promise<{ outcome: Outcome; content: string; elapsedMs: number }>;
    outcome: Outcome | undefined;
    acceptedContent = "";
    suppressedLateEvents = 0;
    abortAcknowledged = false;
    abortFailed = false;
    private finish!: (result: { outcome: Outcome; content: string; elapsedMs: number }) => void;
    private readonly timer: NodeJS.Timeout;
    private readonly unsubscribe: () => void;

    constructor(
        readonly session: RunSession,
        private readonly denyFurtherEgress: () => void,
        private readonly maxOutputBytes: number = OUTPUT_BYTES,
        deadlineMs: number = DEADLINE_MS,
    ) {
        this.result = new Promise(resolve => { this.finish = resolve; });
        this.timer = setTimeout(() => this.cancel("deadline"), deadlineMs);
        this.unsubscribe = session.on(event => {
            if (this.outcome) {
                if (event.type === "assistant.message_delta" || event.type === "assistant.message") {
                    this.suppressedLateEvents++;
                }
                return;
            }
            if (event.type === "assistant.message_delta") {
                const content = this.acceptedContent + event.data.deltaContent;
                if (Buffer.byteLength(content) > this.maxOutputBytes) this.cancel("output-limit");
                else this.acceptedContent = content;
            } else if (event.type === "assistant.message") {
                if (Buffer.byteLength(event.data.content) > this.maxOutputBytes) this.cancel("output-limit");
                else this.acceptedContent = event.data.content;
            } else if (event.type === "session.error") {
                this.complete("provider-error");
            } else if (event.type === "session.idle") {
                this.complete("completed");
            }
        });
    }

    send(input: string): void {
        void this.session.send({ prompt: input }).then(
            undefined,
            () => { this.complete("provider-error"); },
        );
    }

    cancel(reason: "cancelled" | "deadline" | "output-limit" = "cancelled"): void {
        if (this.outcome) return;
        this.denyFurtherEgress();
        this.complete(reason);
        void this.session.abort().then(
            () => { this.abortAcknowledged = true; },
            () => { this.abortFailed = true; },
        );
    }

    private complete(outcome: Outcome): void {
        if (this.outcome) return;
        this.outcome = outcome;
        clearTimeout(this.timer);
        this.finish({
            outcome,
            content: outcome === "completed" ? this.acceptedContent : "",
            elapsedMs: Math.round(performance.now() - this.started),
        });
    }

    dispose(): void {
        clearTimeout(this.timer);
        this.unsubscribe();
    }
}

export async function managementRequest(
    budget: ManagementBudget,
    session: RunSession,
    input: string,
    denyFurtherEgress: () => void,
): Promise<{ outcome: Outcome; proposal?: Proposal; elapsedMs?: number; fallback?: string }> {
    const rejection = budget.admit(input, true);
    if (rejection) return { outcome: rejection, fallback: budget.deterministicStatus() };
    const run = new ObservedRun(session, denyFurtherEgress);
    try {
        run.send(input);
        const result = await run.result;
        budget.finish(result.outcome === "completed" || result.outcome === "provider-error");
        if (result.outcome !== "completed") {
            return { outcome: result.outcome, elapsedMs: result.elapsedMs, fallback: budget.deterministicStatus() };
        }
        try {
            return { outcome: "completed", elapsedMs: result.elapsedMs, proposal: parseProposal(result.content) };
        } catch {
            return { outcome: "invalid-output", elapsedMs: result.elapsedMs, fallback: budget.deterministicStatus() };
        }
    } finally {
        run.dispose();
    }
}
