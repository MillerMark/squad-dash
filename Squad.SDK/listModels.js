import { SquadClient } from "@bradygaster/squad-sdk/client";
async function main() {
    const client = new SquadClient({ cwd: process.cwd() });
    try {
        await client.connect();
        const models = await client.listModels();
        process.stdout.write(JSON.stringify(models.map(model => ({
            id: model.id,
            name: model.name
        }))));
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
