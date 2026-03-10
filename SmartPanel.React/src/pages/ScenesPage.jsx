import { useEffect, useState } from "react";
import { api } from "../api";

export default function ScenesPage() {
  const [scenes, setScenes] = useState(null);
  const [error, setError] = useState("");

  async function load() {
    try {
      setScenes(await api.getScenes());
      setError("");
    } catch (e) {
      setError(e.message);
    }
  }

  useEffect(() => {
    load();
  }, []);

  async function activate(name) {
    try {
      await api.activateScene(name);
      await load();
    } catch (e) {
      setError(e.message);
    }
  }

  if (error) return <section className="card">Error: {error}</section>;
  if (!scenes) return <section className="card">Loading scenes...</section>;

  return (
    <section className="page">
      <h2>Scenes</h2>
      <div className="chips">
        {scenes.map((s) => (
          <button key={s.name} className={`btn ${s.isActive ? "btn-active" : ""}`} onClick={() => activate(s.name)}>
            {s.name}
          </button>
        ))}
      </div>
    </section>
  );
}
