export async function handlePing(req: unknown) {
  return req ?? "pong";
}

export const routeConfig = {
  method: "GET"
};
