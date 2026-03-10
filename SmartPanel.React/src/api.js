const apiBase = import.meta.env.VITE_API_BASE_URL ?? "";
const REQUEST_TIMEOUT_MS = 12000;
const AUTH_TOKEN_KEY = "smartpanel-admin-token";

function getAuthToken() {
  return localStorage.getItem(AUTH_TOKEN_KEY) ?? "";
}

function setAuthToken(token) {
  if (!token) {
    localStorage.removeItem(AUTH_TOKEN_KEY);
    return;
  }

  localStorage.setItem(AUTH_TOKEN_KEY, token);
}

async function request(path, options = {}) {
  const controller = options.signal ? null : new AbortController();
  const timeout = controller
    ? setTimeout(() => controller.abort(), options.timeoutMs ?? REQUEST_TIMEOUT_MS)
    : null;

  let response;
  try {
    const token = options.withAuth ? getAuthToken() : "";
    response = await fetch(`${apiBase}${path}`, {
      headers: {
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(options.headers ?? {})
      },
      ...options,
      signal: options.signal ?? controller?.signal
    });
  } catch (error) {
    if (error?.name === "AbortError") {
      throw new Error(`Request timed out for ${path}.`);
    }
    throw error;
  } finally {
    if (timeout) clearTimeout(timeout);
  }

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `${response.status} ${response.statusText}`);
  }

  const contentType = response.headers.get("content-type") ?? "";
  if (contentType.includes("application/json")) {
    return response.json();
  }

  return null;
}

export const api = {
  getAuthToken,
  setAuthToken,
  login: (payload) => request("/api/auth/login", { method: "POST", body: JSON.stringify(payload) }),
  getCurrentUser: () => request("/api/auth/me", { withAuth: true }),
  getDashboard: () => request("/api/dashboard"),
  getRooms: () => request("/api/rooms"),
  getCameras: () => request("/api/cameras"),
  getScenes: () => request("/api/scenes"),
  activateScene: (sceneName) => request(`/api/scenes/${encodeURIComponent(sceneName)}/activate`, { method: "POST" }),
  getSensorDetails: (entityId, hours = 24) =>
    request(`/api/sensors/details?entityId=${encodeURIComponent(entityId)}&hours=${hours}`),
  controlClimate: (payload) => request("/api/sensors/climate/control", { method: "POST", body: JSON.stringify(payload) }),
  assignDeviceRoom: (payload) => request("/api/rooms/assign", { method: "POST", body: JSON.stringify(payload) }),
  getTvs: () => request("/api/tvs"),
  controlTv: (payload) => request("/api/tvs/control", { method: "POST", body: JSON.stringify(payload) }),
  getAdminDevices: () => request("/api/admin/devices", { withAuth: true }),
  upsertAdminDevice: (id, payload) => request(`/api/admin/devices/${id}`, { withAuth: true, method: "PUT", body: JSON.stringify(payload) }),
  getAdminLayouts: () => request("/api/admin/layouts", { withAuth: true }),
  upsertAdminLayout: (id, payload) => request(`/api/admin/layouts/${id}`, { withAuth: true, method: "PUT", body: JSON.stringify(payload) }),
  getAdminUsers: () => request("/api/admin/users", { withAuth: true })
};
