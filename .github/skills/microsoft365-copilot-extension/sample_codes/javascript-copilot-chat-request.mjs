function resolveCopilotTimeZone() {
    try {
        const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
        return typeof timeZone === "string" && timeZone.includes("/") ? timeZone : "Etc/UTC";
    }
    catch {
        return "Etc/UTC";
    }
}

function getFetch() {
    if (typeof fetch !== "function") {
        throw new Error("This sample requires a runtime with global fetch, such as Node.js 18+, or a fetch polyfill.");
    }

    return fetch;
}

async function createConversation(accessToken) {
    const fetchImpl = getFetch();
    const response = await fetchImpl("https://graph.microsoft.com/beta/copilot/conversations", {
        method: "POST",
        headers: {
            Authorization: `Bearer ${accessToken}`,
            "Content-Type": "application/json"
        },
        body: "{}"
    });

    if (!response.ok) {
        throw new Error(`Failed to create conversation: ${response.status} ${response.statusText}`);
    }

    const json = await response.json();
    if (!json.id) {
        throw new Error("Copilot conversation response did not include an id.");
    }

    return json.id;
}

export async function sendCopilotChatMessage(accessToken, prompt, conversationId) {
    const activeConversationId = conversationId || await createConversation(accessToken);
    const fetchImpl = getFetch();

    const payload = {
        message: { text: prompt },
        locationHint: {
            timeZone: resolveCopilotTimeZone()
        },
        additionalContext: [
            {
                description: "Pinned context",
                text: "Summarize the latest architecture notes before answering."
            }
        ],
        contextualResources: {
            files: [
                {
                    uri: "https://contoso.sharepoint.com/sites/engineering/Shared%20Documents/Architecture.md"
                }
            ]
        }
    };

    const response = await fetchImpl(
        `https://graph.microsoft.com/beta/copilot/conversations/${activeConversationId}/chat`,
        {
            method: "POST",
            headers: {
                Authorization: `Bearer ${accessToken}`,
                "Content-Type": "application/json"
            },
            body: JSON.stringify(payload)
        });

    if (!response.ok) {
        throw new Error(`Copilot chat request failed: ${response.status} ${response.statusText}`);
    }

    return response.json();
}
