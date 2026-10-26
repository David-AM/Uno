export const API =
  import.meta.env.VITE_API_URL ||
  `${window.location.protocol}//${window.location.hostname}:5124/api`;
export const HUB = `${API.replace(/\/api\/?$/, "")}/hubs/multiplayer`;

export async function request(path, method = "GET", body) {
  const response = await fetch(`${API}${path}`, {
    method,
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  const result = await response.json().catch(() => ({}));
  if (!response.ok)
    throw new Error(result.error || "The request could not be completed.");
  return result;
}
