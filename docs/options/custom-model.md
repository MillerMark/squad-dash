---
title: Custom Model
nav_order: 4
parent: Options
---

# Custom Model

The Custom Model tab (also called **BYOK** — Bring Your Own Key/Model) lets you replace the default GitHub Copilot backend with a custom model provider, such as a locally running [Ollama](https://ollama.com) instance or any OpenAI-compatible API endpoint.

Leave all fields blank to use the default GitHub Copilot model.

![Screenshot: Custom Model tab showing Provider URL, Model, and Test Connection fields](images/options-custom-model.png)
> 📸 *Screenshot needed: The Options window on the Custom Model tab — show the Provider URL, Model, Provider Type, and API Key fields, plus the Test Connection button.*

---

## Settings

| Setting | Description |
|---|---|
| **Provider URL** | The base URL of your custom model API, e.g. `http://localhost:11434/v1` for a local Ollama server. Leave blank to use GitHub Copilot. |
| **Model** | The model identifier to request, e.g. `llama3` or `mistral`. Must match a model name the provider recognises. |
| **Provider Type** | The API protocol the provider speaks (e.g. OpenAI-compatible). Select from the dropdown. |
| **API Key** | An API key if your provider requires one. Optional — leave blank for unauthenticated local endpoints. Click **(reveal key)** and hold to temporarily show the value. |

---

## Testing the Connection

Click **Test Connection** to send a quick `GET /models` request to the configured Provider URL. The result is shown inline:

- ✅ **Connected — N model(s): model-name, …** — the endpoint is reachable and returned a list of models.
- ✅ **Reachable (no models listed)** — the endpoint responded but didn't return a model list.
- ❌ **Error message** — the request failed. Check that the Provider URL is correct and the server is running.

---

## Example: Ollama

1. [Install Ollama](https://ollama.com) and pull a model: `ollama pull llama3`
2. Set **Provider URL** to `http://localhost:11434/v1`
3. Set **Model** to `llama3`
4. Set **Provider Type** to **OpenAI compatible**
5. Leave **API Key** blank
6. Click **Test Connection** — you should see the model listed

---

## Related

- **[Routing](../reference/routing.md)** — How SquadDash routes prompts to models
