import { randomUUID } from "node:crypto";

function resolveLocation() {
    try {
        const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
        return {
            timeZone: typeof timeZone === "string" && timeZone.includes("/") ? timeZone : "Etc/UTC",
            timeZoneOffset: -new Date().getTimezoneOffset()
        };
    }
    catch {
        return {
            timeZone: "Etc/UTC",
            timeZoneOffset: 0
        };
    }
}

export async function sendWorkIqMessage(accessToken, text, contextId) {
    const location = resolveLocation();
    const payload = {
        jsonrpc: "2.0",
        id: randomUUID(),
        method: "SendMessage",
        params: {
            message: {
                role: "ROLE_USER",
                messageId: randomUUID(),
                parts: [{ text }],
                metadata: {
                    Location: {
                        timeZone: location.timeZone,
                        timeZoneOffset: location.timeZoneOffset
                    }
                },
                ...(contextId ? { contextId } : {})
            }
        }
    };

    const response = await fetch("https://workiq.svc.cloud.microsoft/a2a/", {
        method: "POST",
        headers: {
            Authorization: `Bearer ${accessToken}`,
            "Content-Type": "application/json",
            "A2A-Version": "1.0"
        },
        body: JSON.stringify(payload)
    });

    if (!response.ok) {
        throw new Error(`Work IQ request failed: ${response.status} ${response.statusText}`);
    }

    return response.json();
}
