import { createConnection } from "node:net";

// Event notifications only: OpenCode's approval must be answered in its own UI.
function send(message) {
  return new Promise((resolve) => {
    const socket = createConnection("\\\\.\\pipe\\xylo-agent");
    let done = false;
    const finish = () => { if (!done) { done = true; socket.destroy(); resolve(); } };
    socket.setTimeout(1000, finish);
    socket.on("error", finish);
    socket.on("data", finish);
    socket.on("connect", () => socket.write(JSON.stringify(message) + "\n"));
  });
}

export const XyloPlugin = async ({ directory }) => ({
  event: async ({ event }) => {
    const p = event.properties || {};
    const session = p.sessionID || p.info?.id || "";
    if (!session) return;
    let kind;
    if (event.type === "session.status" && ["idle", "busy", "retry"].includes(p.status?.type))
      kind = p.status.type === "idle" ? "Stop" : "UserPromptSubmit";
    else if (event.type === "session.idle" || event.type === "session.error") kind = "Stop";
    else if (event.type === "session.deleted") kind = "SessionEnd";
    else if (["permission.updated", "permission.asked", "permission.v2.asked"].includes(event.type)) kind = "PermissionNotice";
    else if (["permission.replied", "permission.v2.replied"].includes(event.type)) kind = "PermissionResolved";
    else return;
    const requestId = kind === "PermissionResolved" ? (p.requestID || p.permissionID || "") : (p.id || "");
    await send({ agent: "OpenCode", event: kind, session, project: directory || "",
                 requestId, tool: p.permission || p.action || p.title || p.type || "Permission" });
  },
});
