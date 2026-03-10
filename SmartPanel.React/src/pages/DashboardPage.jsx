import { useEffect, useState } from "react";
import { api } from "../api";

export default function DashboardPage() {
  const [data, setData] = useState(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;
    api.getDashboard()
      .then((x) => !cancelled && setData(x))
      .catch((e) => !cancelled && setError(e.message));
    return () => {
      cancelled = true;
    };
  }, []);

  if (error) return <section className="card">Load error: {error}</section>;
  if (!data) return <section className="card">Loading dashboard...</section>;

  return (
    <section className="page">
      <h2>Dashboard</h2>
      <div className="grid-2">
        <article className="card">
          <div className="row"><span>Current Time</span><strong>{new Date(data.currentTime).toLocaleString()}</strong></div>
          <div className="row"><span>Alerts</span><strong>{data.alertsCount}</strong></div>
          <div className="row"><span>House Status</span><strong>{data.houseStatus}</strong></div>
        </article>
        <article className="card">
          <h3>Scenes</h3>
          <div className="chips">
            {(data.scenes ?? []).map((s) => (
              <span key={s.name} className={`chip ${s.isActive ? "chip-active" : ""}`}>{s.name}</span>
            ))}
          </div>
        </article>
      </div>
    </section>
  );
}
