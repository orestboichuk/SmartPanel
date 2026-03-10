import { useEffect, useState } from "react";
import { api } from "../api";

export default function CamerasPage() {
  const [cameras, setCameras] = useState(null);
  const [error, setError] = useState("");
  const [selectedCameraId, setSelectedCameraId] = useState("");
  const [refreshToken, setRefreshToken] = useState(Date.now());
  const [liveMode, setLiveMode] = useState(false);
  const [streamError, setStreamError] = useState("");
  const [streamLoading, setStreamLoading] = useState(false);
  const [snapshotTicker, setSnapshotTicker] = useState(0);

  const load = async () => {
    const data = await api.getCameras();
    setCameras(data);
    if (!selectedCameraId && data.length > 0) {
      setSelectedCameraId(data[0].id);
    }
  };

  useEffect(() => {
    load().catch((e) => setError(e.message));
  }, []);

  useEffect(() => {
    if (liveMode || !selectedCameraId) return undefined;
    const timer = setInterval(() => setSnapshotTicker((x) => x + 1), 2000);
    return () => clearInterval(timer);
  }, [liveMode, selectedCameraId]);

  const selected = (cameras || []).find((x) => x.id === selectedCameraId) || null;
  const streamSrc = selected ? resolveCameraStreamUrl(selected) : "";
  const snapshotSrc = selected ? resolveCameraSnapshotUrl(selected) : "";

  if (error) return <section className="card">Load error: {error}</section>;
  if (!cameras) return <section className="card">Loading cameras...</section>;
  if (cameras.length === 0) return <section className="card">No cameras discovered.</section>;

  return (
    <section className="page">
      <div className="row">
        <h2>Cameras</h2>
        <button className="btn" onClick={() => load().catch((e) => setError(e.message))}>Scan now</button>
      </div>

      <div className="camera-layout">
        <div className="camera-list">
          {cameras.map((c) => (
            <button
              key={c.id}
              className={`card camera-list-item ${selectedCameraId === c.id ? "camera-list-item-active" : ""}`}
              onClick={() => {
                setSelectedCameraId(c.id);
                setRefreshToken(Date.now());
                setLiveMode(false);
                setStreamError("");
                setStreamLoading(false);
              }}>
              <div className="row"><strong>{c.name}</strong><span className={c.online ? "ok" : "bad"}>{c.online ? "Online" : "Offline"}</span></div>
              <div className="small">{c.id}</div>
            </button>
          ))}
        </div>

        <article className="card camera-viewer">
          {!selected && <div className="small">Select camera.</div>}
          {selected && (
            <>
              <div className="row">
                <strong>{selected.name}</strong>
                <div className="controls">
                  <button
                    className="btn"
                    onClick={() => {
                      setLiveMode((v) => !v);
                      setStreamError("");
                      setStreamLoading(!liveMode);
                      setRefreshToken(Date.now());
                    }}>
                    {liveMode ? "Stop live" : "Start live"}
                  </button>
                  <button
                    className="btn"
                    onClick={() => {
                      if (liveMode) setStreamLoading(true);
                      setRefreshToken(Date.now());
                    }}>
                    Reload
                  </button>
                </div>
              </div>
              <div className="camera-stage">
                {!liveMode && (
                  <img
                    key={`snap-main-${selected.id}-${refreshToken}-${snapshotTicker}`}
                    src={appendCacheBuster(snapshotSrc, `${refreshToken}-${snapshotTicker}`)}
                    alt={selected.name}
                  />
                )}
                {liveMode && (
                  <img
                    key={`${selected.id}-${refreshToken}`}
                    src={appendCacheBuster(streamSrc, refreshToken)}
                    alt={selected.name}
                    onLoad={() => {
                      setStreamLoading(false);
                      setStreamError("");
                    }}
                    onError={() => {
                      setStreamLoading(false);
                      setStreamError("Live stream unavailable right now. Showing snapshot.");
                      setLiveMode(false);
                    }}
                  />
                )}
              </div>
              {liveMode && streamLoading && <div className="small">Starting live stream...</div>}
              {streamError && <div className="small bad">{streamError}</div>}
              <div className="small">Snapshot fallback (auto refresh 2s):</div>
              <img className="camera-snapshot" src={appendCacheBuster(snapshotSrc, `${refreshToken}-${snapshotTicker}`)} alt={`${selected.name} snapshot`} />
            </>
          )}
        </article>
      </div>
    </section>
  );
}

function resolveCameraStreamUrl(camera) {
  const raw = String(camera?.streamUrl || "").trim();
  if (raw.startsWith("/api/cameras/stream?")) {
    return raw;
  }
  return `/api/cameras/stream?entityId=${encodeURIComponent(camera.id)}`;
}

function resolveCameraSnapshotUrl(camera) {
  const raw = String(camera?.snapshotUrl || "").trim();
  if (raw.startsWith("/api/cameras/snapshot?")) {
    return raw;
  }
  return `/api/cameras/snapshot?entityId=${encodeURIComponent(camera.id)}`;
}

function appendCacheBuster(url, token) {
  const separator = url.includes("?") ? "&" : "?";
  return `${url}${separator}_=${token}`;
}
