import { SquadClient } from "@bradygaster/squad-sdk/client";

function pushOptionalStringField(target: Record<string, unknown>, key: string, value: unknown) {
    if (typeof value !== "string")
        return;
    const trimmed = value.trim();
    if (trimmed.length > 0)
        target[key] = trimmed;
}

function pushOptionalNumberField(target: Record<string, unknown>, key: string, value: unknown) {
    if (typeof value === "number" && Number.isFinite(value))
        target[key] = value;
}

function pushOptionalStringArrayField(target: Record<string, unknown>, key: string, value: unknown) {
    if (!Array.isArray(value))
        return;
    const normalized = value
        .filter(item => typeof item === "string")
        .map(item => item.trim())
        .filter(item => item.length > 0);
    if (normalized.length > 0)
        target[key] = normalized;
}

async function main() {
    const client = new SquadClient({ cwd: process.cwd() });
    try {
        await client.connect();
        const models = await client.listModels();
        process.stdout.write(JSON.stringify(models.map(model => {
            const serialized: Record<string, unknown> = {
                id: model.id,
                name: model.name
            };
            pushOptionalStringField(serialized, "category", (model as { category?: unknown }).category);
            pushOptionalNumberField(serialized, "inputPricePerMillionTokens", (model as { inputPricePerMillionTokens?: unknown }).inputPricePerMillionTokens);
            pushOptionalNumberField(serialized, "outputPricePerMillionTokens", (model as { outputPricePerMillionTokens?: unknown }).outputPricePerMillionTokens);
            pushOptionalStringField(serialized, "releaseStatus", (model as { releaseStatus?: unknown }).releaseStatus);
            const supportedReasoningEfforts = (model as {
                supportedReasoningEfforts?: unknown;
                supportedReasoningEffort?: unknown;
            }).supportedReasoningEfforts
                ?? (model as { supportedReasoningEffort?: unknown }).supportedReasoningEffort;
            pushOptionalStringArrayField(serialized, "supportedReasoningEfforts", supportedReasoningEfforts);
            return serialized;
        })));
    }
    finally {
        await client.disconnect().catch(() => undefined);
    }
}

main().catch(error => {
    const message = error instanceof Error ? error.message : String(error);
    process.stderr.write(message);
    process.exitCode = 1;
});
