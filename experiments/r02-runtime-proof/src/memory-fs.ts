import { win32 as path } from "node:path";
import type { SessionFsFileInfo, SessionFsProvider } from "@github/copilot-sdk";

export class MemoryFs implements SessionFsProvider {
    readonly files = new Map<string, string>();
    private readonly directories = new Set<string>();
    writes = 0;

    private key(value: string): string {
        return path.resolve(value).toLowerCase();
    }

    private missing(): never {
        throw Object.assign(new Error("Synthetic session file not found"), { code: "ENOENT" });
    }

    async readFile(value: string): Promise<string> {
        return this.files.get(this.key(value)) ?? this.missing();
    }

    async writeFile(value: string, content: string): Promise<void> {
        await this.mkdir(path.dirname(value), true);
        this.files.set(this.key(value), content);
        this.writes++;
    }

    async appendFile(value: string, content: string): Promise<void> {
        await this.writeFile(value, (this.files.get(this.key(value)) ?? "") + content);
    }

    async exists(value: string): Promise<boolean> {
        return this.files.has(this.key(value)) || this.directories.has(this.key(value));
    }

    async stat(value: string): Promise<SessionFsFileInfo> {
        const key = this.key(value);
        if (!await this.exists(key)) this.missing();
        const time = new Date(0).toISOString();
        return {
            isFile: this.files.has(key),
            isDirectory: this.directories.has(key),
            size: Buffer.byteLength(this.files.get(key) ?? ""),
            mtime: time,
            birthtime: time,
        };
    }

    async mkdir(value: string, recursive: boolean): Promise<void> {
        const key = this.key(value);
        this.directories.add(key);
        const parent = path.dirname(key);
        if (recursive && parent !== key) await this.mkdir(parent, true);
    }

    async readdir(value: string): Promise<string[]> {
        return (await this.readdirWithTypes(value)).map(entry => entry.name);
    }

    async readdirWithTypes(value: string): Promise<{ name: string; type: "file" | "directory" }[]> {
        const key = this.key(value);
        if (!this.directories.has(key)) this.missing();
        return [...this.directories, ...this.files.keys()]
            .filter(entry => entry !== key && path.dirname(entry) === key)
            .map(entry => ({
                name: path.basename(entry),
                type: this.files.has(entry) ? "file" : "directory",
            }));
    }

    async rm(value: string, recursive: boolean, force: boolean): Promise<void> {
        const key = this.key(value);
        if (!force && !await this.exists(key)) this.missing();
        for (const entry of [...this.files.keys(), ...this.directories]) {
            if (entry === key || (recursive && entry.startsWith(key + "\\"))) {
                this.files.delete(entry);
                this.directories.delete(entry);
            }
        }
    }

    async rename(source: string, destination: string): Promise<void> {
        const content = await this.readFile(source);
        await this.writeFile(destination, content);
        await this.rm(source, false, false);
    }

    clear(): void {
        this.files.clear();
        this.directories.clear();
    }
}
