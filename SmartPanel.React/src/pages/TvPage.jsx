import { useEffect, useState } from "react";
import { api } from "../api";

export default function TvPage() {
  const [tvs, setTvs] = useState(null);
  const [status, setStatus] = useState("");
  const [sources, setSources] = useState({});
  const [volumes, setVolumes] = useState({});
  const [expandedTv, setExpandedTv] = useState("");

  const load = async () => {
    const data = await api.getTvs();
    setTvs(data);
    setSources((prev) => {
      const next = { ...prev };
      for (const t of data) {
        if (!next[t.id]) next[t.id] = t.source || t.availableSources?.[0] || "";
      }
      return next;
    });
    setVolumes((prev) => {
      const next = { ...prev };
      for (const t of data) {
        if (next[t.id] == null) next[t.id] = t.volumePercent ?? 20;
      }
      return next;
    });
  };

  useEffect(() => {
    load().catch((e) => setStatus(e.message));
    const timer = setInterval(() => load().catch(() => {}), 15000);
    return () => clearInterval(timer);
  }, []);

  const send = async (payload) => {
    try {
      await api.controlTv(payload);
      setStatus("Command sent.");
      await load();
    } catch (e) {
      setStatus(e.message);
    }
  };

  if (!tvs) return <section className="card">Loading TVs...</section>;

  return (
    <section className="page">
      <div className="row">
        <h2>TV</h2>
        <button className="btn" onClick={() => load().catch((e) => setStatus(e.message))}>Scan now</button>
      </div>
      {status && <div className="card">{status}</div>}
      <div className="tv-grid">
        {tvs.map((t) => {
          const opened = expandedTv === t.id;
          return (
            <article key={t.id} className={`card tv-square ${opened ? "tv-square-expanded" : ""}`}>
              <div className="tv-top">
                <TvIcon />
                <span className={t.online ? "ok" : "bad"}>{t.online ? "Online" : "Offline"}</span>
              </div>
              <strong>{t.name}</strong>
              <div className="small">State: {t.state}</div>
              <div className="controls">
                <button className="btn" onClick={() => setExpandedTv(opened ? "" : t.id)}>
                  {opened ? "Hide panel" : "Open panel"}
                </button>
              </div>

              {opened && (
                <>
                  <div className="controls">
                    <button className="btn" onClick={() => send({ entityId: t.id, powerOn: true })}>Power On</button>
                    <button className="btn" onClick={() => send({ entityId: t.id, powerOn: false })}>Power Off</button>
                    <button className="btn" onClick={() => send({ entityId: t.id, togglePlayPause: true })}>Play/Pause</button>
                  </div>
                  <div className="volume-block">
                    <label>Volume: {volumes[t.id] ?? t.volumePercent ?? 0}%</label>
                    <input
                      type="range"
                      min={0}
                      max={100}
                      value={volumes[t.id] ?? t.volumePercent ?? 0}
                      onChange={(e) => setVolumes((p) => ({ ...p, [t.id]: Number(e.target.value) }))}
                    />
                    <button className="btn" onClick={() => send({ entityId: t.id, volumePercent: Number(volumes[t.id] ?? 20) })}>Apply Volume</button>
                  </div>
                  {t.availableSources?.length > 0 && (
                    <div className="row">
                      <select
                        className="input"
                        value={sources[t.id] || ""}
                        onChange={(e) => setSources((p) => ({ ...p, [t.id]: e.target.value }))}
                      >
                        {t.availableSources.map((s) => <option key={s} value={s}>{s}</option>)}
                      </select>
                      <button className="btn" onClick={() => send({ entityId: t.id, source: sources[t.id] })}>Set source</button>
                    </div>
                  )}
                </>
              )}
            </article>
          );
        })}
      </div>
    </section>
  );
}

function TvIcon() {
  return (
    <svg className="tv-icon" viewBox="0 0 24 24" aria-hidden="true">
      <rect x="3.5" y="5.5" width="17" height="11" rx="2" />
      <path d="M9.2 19h5.6" />
      <path d="M10.3 2.8L12 5.2l1.7-2.4" />
    </svg>
  );
}
