import { createReadStream } from "node:fs";
import { readFile } from "node:fs/promises";
import { createHash } from "node:crypto";
import { resolve } from "node:path";

export const candidate = {
    sdk: "1.0.16",
    runtime: "1.0.90",
    node: "v24.16.0",
    executableSha256: "7021cf1f25eb6b75e64c05e8f805747c62dd420dc8760659660291809e92603a",
    payloadSha256: "41ebb48367f96c984babde61afb68f22a85ab8fd9c037fdccf1778881c4bba05",
} as const;

async function hash(file: string): Promise<string> {
    const digest = createHash("sha256");
    for await (const chunk of createReadStream(file)) digest.update(chunk);
    return digest.digest("hex");
}

export function assertCandidateHost(platform: string, arch: string, node: string): void {
    if (platform !== "win32" || arch !== "x64" || node !== candidate.node) {
        throw new Error("This candidate proof requires Windows x64 and exactly Node 24.16.0");
    }
}

export async function validateCandidate(): Promise<string> {
    assertCandidateHost(process.platform, process.arch, process.version);
    const metadata: unknown = JSON.parse(await readFile(
        resolve("node_modules\\@github\\copilot-sdk\\package.json"), "utf8"));
    if (typeof metadata !== "object" || metadata === null || !("version" in metadata)
        || metadata.version !== candidate.sdk || !("copilotCliVersion" in metadata)
        || metadata.copilotCliVersion !== candidate.runtime) {
        throw new Error("Installed SDK metadata does not match the pinned candidate");
    }
    const directory = resolve("node_modules\\@github\\copilot-sdk-win32-x64\\prebuilds\\win32-x64");
    const executable = resolve(directory, "copilot-runtime.exe");
    const [executableHash, payloadHash] = await Promise.all([
        hash(executable), hash(resolve(directory, "runtime.node")),
    ]);
    if (executableHash !== candidate.executableSha256 || payloadHash !== candidate.payloadSha256) {
        throw new Error("Bundled runtime bytes do not match the recorded candidate");
    }
    return executable;
}
