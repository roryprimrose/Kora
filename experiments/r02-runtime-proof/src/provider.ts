import { createServer, type ServerResponse } from "node:http";
import { once } from "node:events";

export type Reply =
    | { kind: "text"; chunks: string[]; delayMs?: number }
    | { kind: "tool"; name: string; arguments?: string }
    | { kind: "error"; status: number }
    | { kind: "hold" };

export class SyntheticProvider {
    readonly requests: { body: string; credentialPresent: boolean; path: string }[] = [];
    readonly responses: ServerResponse[] = [];
    private readonly replies: Reply[] = [];
    private readonly arrivals = new Set<() => void>();
    private readonly timers = new Set<NodeJS.Timeout>();
    private readonly server = createServer(async (request, response) => {
        const chunks: Buffer[] = [];
        for await (const chunk of request) chunks.push(Buffer.from(chunk));
        this.requests.push({
            body: Buffer.concat(chunks).toString("utf8"),
            credentialPresent: request.headers.authorization === "Bearer synthetic-not-a-credential",
            path: request.url ?? "",
        });
        this.responses.push(response);
        for (const notify of this.arrivals) notify();
        const reply = this.replies.shift();
        if (!reply) {
            response.writeHead(500).end('{"error":{"message":"Unscripted synthetic request"}}');
            return;
        }
        if (reply.kind === "error") {
            response.writeHead(reply.status, { "content-type": "application/json" });
            response.end(JSON.stringify({ error: { message: `Synthetic HTTP ${reply.status}` } }));
            return;
        }
        response.writeHead(200, { "content-type": "text/event-stream", "cache-control": "no-cache" });
        response.flushHeaders();
        if (reply.kind === "hold") return;
        const emit = (delta: unknown, finishReason: string | null = null) => {
            response.write(`data: ${JSON.stringify({
                id: "synthetic-completion", object: "chat.completion.chunk",
                created: 1, model: "r02-synthetic",
                choices: [{ index: 0, delta, finish_reason: finishReason }],
            })}\n\n`);
        };
        emit({ role: "assistant" });
        if (reply.kind === "tool") {
            emit({ tool_calls: [{
                index: 0, id: "synthetic-tool-call", type: "function",
                function: { name: reply.name, arguments: reply.arguments ?? "{}" },
            }] });
            emit({}, "tool_calls");
            response.end("data: [DONE]\n\n");
        } else {
            const send = (index: number) => {
                if (response.destroyed) return;
                const content = reply.chunks[index];
                if (content !== undefined) {
                    emit({ content });
                    const timer = setTimeout(() => {
                        this.timers.delete(timer);
                        send(index + 1);
                    }, reply.delayMs ?? 5);
                    this.timers.add(timer);
                } else {
                    emit({}, "stop");
                    response.end("data: [DONE]\n\n");
                }
            };
            send(0);
        }
    });

    baseUrl = "";

    async start(): Promise<void> {
        this.server.listen(0, "127.0.0.1");
        await once(this.server, "listening");
        const address = this.server.address();
        if (!address || typeof address === "string") throw new Error("Missing loopback listener");
        this.baseUrl = `http://127.0.0.1:${address.port}/v1`;
    }

    enqueue(...replies: Reply[]): void {
        this.replies.push(...replies);
    }

    async waitForRequests(count: number, timeoutMs = 10_000): Promise<void> {
        if (this.requests.length >= count) return;
        await new Promise<void>((resolve, reject) => {
            const timer = setTimeout(() => {
                this.arrivals.delete(check);
                reject(new Error("Synthetic provider did not receive the expected request"));
            }, timeoutMs);
            const check = () => {
                if (this.requests.length >= count) {
                    clearTimeout(timer);
                    this.arrivals.delete(check);
                    resolve();
                }
            };
            this.arrivals.add(check);
            check();
        });
    }

    async stop(): Promise<void> {
        for (const timer of this.timers) clearTimeout(timer);
        for (const response of this.responses) response.destroy();
        const closed = once(this.server, "close");
        this.server.close();
        this.server.closeAllConnections();
        await closed;
    }
}
