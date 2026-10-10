import { mkdir, writeFile } from "node:fs/promises";
import { cpus, platform, release } from "node:os";
import { cases, type Evidence } from "./cases.js";
import { candidate, validateCandidate } from "./candidate.js";

// A mismatched host must not replace historical evidence with unqualified rows.
await validateCandidate();
const rows: Evidence[] = [];
for (const run of cases) {
    try {
        const evidence = await run();
        rows.push(...evidence);
        for (const row of evidence) console.log(`${row.status}: ${row.id}`);
    } catch {
        rows.push({
            id: run.name, status: "FAIL",
            detail: "Harness assertion or runtime operation failed. Run npm test for the focused diagnostic; no exception payload is copied into the report.",
            measurements: {},
        });
        console.error(`FAIL: ${run.name}`);
    }
}
rows.push({
    id: "global-egress-and-diagnostics", status: "BLOCKED",
    detail: "Supported handler observes model HTTP only. No OS-level all-network/file trace was performed; background service, crash dump, OS diagnostics and undeclared destinations are not proven absent.",
    measurements: { productionEnabled: false },
}, {
    id: "hosted-auth-account-terms-and-service", status: "BLOCKED",
    detail: "User approved no-account loopback only. No live login, account tier, provider terms eligibility, service quota/rate limit, cost or hosted concurrency was validated.",
    measurements: { liveInferenceCalls: 0, provisionedResources: 0, modelChargesUSD: 0 },
}, {
    id: "dotnet-adapter-parity", status: "BLOCKED",
    detail: "Node candidate only. Kora's production .NET adapter/composition is deliberately untouched; equivalent .NET control points require separate evidence.",
    measurements: { productionEnabled: false },
});
await mkdir("evidence", { recursive: true });
await writeFile("evidence\\results.json", JSON.stringify({
    recordedAt: new Date().toISOString(),
    sdk: "@github/copilot-sdk@1.0.16",
    bundledRuntime: "1.0.90",
    executableSha256: candidate.executableSha256,
    payloadSha256: candidate.payloadSha256,
    node: process.version,
    npm: process.env.npm_config_user_agent?.match(/^npm\/(\S+)/)?.[1] ?? "unobserved",
    typescript: "5.9.3",
    environment: { platform: platform(), osRelease: release(), arch: process.arch, cpu: cpus()[0]?.model },
    data: "synthetic only",
    provider: "in-process scripted HTTP/SSE loopback; not a live model",
    productionReady: false,
    rows,
}, null, 2) + "\n");
console.log("Recorded content-minimized evidence\\results.json");
if (rows.some(row => row.status !== "PASS")) process.exitCode = 2;
