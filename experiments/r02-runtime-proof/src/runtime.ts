import { mkdtemp, mkdir, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { resolve, join } from "node:path";
import {
    CopilotClient, CopilotRequestHandler, RuntimeConnection,
    type CopilotRequestContext, type CopilotSession, type SessionConfig, type SessionEvent,
} from "@github/copilot-sdk";
import { MemoryFs } from "./memory-fs.js";
import { SyntheticProvider } from "./provider.js";
import { validateCandidate } from "./candidate.js";

export const DENIED_INITIAL = "R02_DENIED_INITIAL_CONTEXT";
export const DENIED_RESULT = "R02_DENIED_TOOL_RESULT";
export const DENIED_COLLECTION = "R02_DENIED_DISCOVERED_INSTRUCTION";
export const AUTH_SENTINEL = "synthetic-not-a-credential";
export const deniedMarkers = [DENIED_INITIAL, DENIED_RESULT, DENIED_COLLECTION];

export class EgressBoundary extends CopilotRequestHandler {
    readonly observed: { sessionId: string | undefined; bytes: number; blocked: boolean }[] = [];
    readonly managementSessions = new Set<string>();
    private readonly managementRequests = new Set<string>();
    private readonly cancelled = new Set<string>();
    private readonly destinations = new Map<string, string>();

    approveDestination(sessionId: string, baseUrl: string): void {
        const url = new URL(baseUrl);
        if (url.hostname !== "127.0.0.1" || url.protocol !== "http:") {
            throw new Error("This proof permits only its synthetic loopback providers");
        }
        this.destinations.set(sessionId, url.origin);
    }

    cancel(sessionId: string): void {
        this.cancelled.add(sessionId);
    }

    protected override async sendRequest(request: Request, context: CopilotRequestContext): Promise<Response> {
        const body = await request.clone().text();
        const bytes = Buffer.byteLength(body);
        const id = context.sessionId;
        const management = id !== undefined && this.managementSessions.has(id);
        const blocked = !id || new URL(request.url).origin !== this.destinations.get(id)
            || deniedMarkers.some(marker => body.includes(marker))
            || body.includes(AUTH_SENTINEL)
            || this.cancelled.has(id)
            || (management && (bytes > 32 * 1024 || this.managementRequests.has(id)));
        this.observed.push({ sessionId: id, bytes, blocked });
        if (blocked) {
            return Response.json({ error: { message: "Host egress policy denied request" } }, { status: 403 });
        }
        if (management) this.managementRequests.add(id);
        return super.sendRequest(request, context);
    }

    protected override async openWebSocket(): Promise<never> {
        throw new Error("WebSocket egress is not admitted by this HTTP-only proof");
    }
}

export class RuntimeFixture {
    readonly boundary = new EgressBoundary();
    readonly stores: MemoryFs[] = [];
    readonly events = new Map<string, SessionEvent[]>();
    readonly sessions: CopilotSession[] = [];
    client!: CopilotClient;
    root = "";
    workspace = "";

    async start(): Promise<void> {
        const executable = await validateCandidate();
        const runtimeRoot = resolve(".runtime");
        await mkdir(runtimeRoot, { recursive: true });
        this.root = await mkdtemp(join(runtimeRoot, "trial-"));
        this.workspace = join(this.root, "workspace");
        await mkdir(this.workspace);
        await writeFile(join(this.workspace, "AGENTS.md"), DENIED_COLLECTION);
        const home = join(this.root, "home");
        await mkdir(home);
        const env: Record<string, string | undefined> = {};
        for (const key of ["PATH", "SystemRoot", "WINDIR", "COMSPEC", "PATHEXT"]) {
            env[key] = process.env[key];
        }
        Object.assign(env, {
            HOME: home, USERPROFILE: home, APPDATA: home, LOCALAPPDATA: home,
            TEMP: this.root, TMP: this.root,
            COPILOT_TELEMETRY_DISABLED: "1", OTEL_SDK_DISABLED: "true",
            POWERSHELL_TELEMETRY_OPTOUT: "1", POWERSHELL_UPDATECHECK: "Off",
        });
        this.client = new CopilotClient({
            connection: RuntimeConnection.forStdio({ path: executable }),
            mode: "empty",
            workingDirectory: this.workspace,
            baseDirectory: join(this.root, "copilot"),
            env,
            useLoggedInUser: false,
            logLevel: "none",
            requestHandler: this.boundary,
            sessionFs: {
                initialCwd: this.workspace,
                sessionStatePath: "Q:\\r02-virtual-session",
                conventions: "windows",
                capabilities: { sqlite: false },
            },
        });
        await this.client.start();
    }

    async session(provider: SyntheticProvider, overrides: Partial<SessionConfig> = {}): Promise<CopilotSession> {
        const store = new MemoryFs();
        this.stores.push(store);
        const session = await this.client.createSession({
            model: "gpt-4.1",
            provider: {
                type: "openai", wireApi: "completions", baseUrl: provider.baseUrl,
                apiKey: AUTH_SENTINEL,
            },
            systemMessage: { mode: "replace", content: "Use only synthetic input and the admitted test tools." },
            availableTools: [],
            excludedTools: ["builtin:*", "mcp:*"],
            tools: [],
            streaming: true,
            memory: { enabled: false },
            infiniteSessions: { enabled: false },
            largeOutput: { enabled: false },
            enableConfigDiscovery: false,
            skipCustomInstructions: true,
            enableSkills: false,
            enableFileHooks: false,
            enableHostGitOperations: false,
            enableOnDemandInstructionDiscovery: false,
            enableSessionStore: false,
            enableSessionTelemetry: false,
            enableFileChangeTracking: false,
            skipEmbeddingRetrieval: true,
            embeddingCacheStorage: "in-memory",
            mcpOAuthTokenStorage: "in-memory",
            remoteSession: "off",
            mcpServers: {},
            customAgents: [],
            skillDirectories: [],
            pluginDirectories: [],
            instructionDirectories: [],
            requestExtensions: false,
            requestCanvasRenderer: false,
            manageScheduleEnabled: false,
            onPermissionRequest: () => ({ kind: "denied-interactively-by-user" }),
            onAutoModeSwitchRequest: () => "no",
            hooks: {
                onErrorOccurred: () => ({ errorHandling: "abort" }),
            },
            createSessionFsProvider: () => store,
            ...overrides,
        });
        this.sessions.push(session);
        this.boundary.approveDestination(session.sessionId, provider.baseUrl);
        const events: SessionEvent[] = [];
        this.events.set(session.sessionId, events);
        session.on(event => events.push(event));
        return session;
    }

    async diskObservations(): Promise<{ files: string[]; markerFiles: string[] }> {
        const files: string[] = [];
        const markerFiles: string[] = [];
        const walk = async (directory: string) => {
            for (const entry of await readdir(directory, { withFileTypes: true })) {
                const file = join(directory, entry.name);
                if (entry.isDirectory()) await walk(file);
                else {
                    const relative = file.slice(this.root.length + 1);
                    files.push(relative);
                    if (relative !== "workspace\\AGENTS.md") {
                        const content = await readFile(file);
                        if ([...deniedMarkers, "R02_APPROVED"].some(marker => content.includes(Buffer.from(marker)))) {
                            markerFiles.push(relative);
                        }
                    }
                }
            }
        };
        await walk(this.root);
        return { files, markerFiles };
    }

    async stop(): Promise<void> {
        try {
            if (this.client) {
                const errors = await this.client.stop();
                if (errors.length > 0) throw new Error("Runtime did not acknowledge clean shutdown");
            }
        } finally {
            for (const store of this.stores) store.clear();
            if (this.root) await rm(this.root, { recursive: true, force: true });
        }
    }
}
