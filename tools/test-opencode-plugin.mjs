import { createServer } from "node:net";
import { XyloPlugin } from "../integrations/opencode/xylo.js";

const received = [];
const server = createServer((socket) => {
  socket.once("data", (bytes) => {
    received.push(JSON.parse(bytes.toString()));
    socket.end("{}\n");
  });
});
await new Promise((resolve, reject) => server.once("error", reject).listen("\\\\.\\pipe\\xylo-agent", resolve));
try {
  const plugin = await XyloPlugin({ directory: "synthetic-project" });
  await plugin.event({ event: { type: "session.status", properties: { sessionID: "s1", status: { type: "busy" } } } });
  await plugin.event({ event: { type: "permission.asked", properties: { sessionID: "s1", id: "p1", permission: "bash", metadata: { secret: "must-not-be-forwarded" } } } });
  await plugin.event({ event: { type: "permission.replied", properties: { sessionID: "s1", requestID: "p1" } } });
  await plugin.event({ event: { type: "session.idle", properties: { sessionID: "s1" } } });
  const kinds = received.map((item) => item.event).join(",");
  if (kinds !== "UserPromptSubmit,PermissionNotice,PermissionResolved,Stop") throw new Error(kinds);
  if (received[1].requestId !== "p1" || JSON.stringify(received).includes("must-not-be-forwarded")) throw new Error("Unexpected permission payload");
  console.log("OpenCode activity and permission notice events passed.");
} finally {
  server.close();
}
