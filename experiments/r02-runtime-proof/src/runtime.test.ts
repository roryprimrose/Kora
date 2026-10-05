import { test } from "node:test";
import { cases } from "./cases.js";

for (const run of cases) {
    test(`R02 conformance: ${run.name}`, { timeout: 90_000 }, async () => {
        await run();
    });
}
