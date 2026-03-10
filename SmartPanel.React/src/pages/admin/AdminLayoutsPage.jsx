import { useEffect, useState } from "react";
import { api } from "../../api";

const EMPTY_FORM = {
  id: 0,
  name: "",
  route: "/panel",
  layoutJson: "{}",
  isDefault: false
};

export default function AdminLayoutsPage() {
  const [layouts, setLayouts] = useState([]);
  const [form, setForm] = useState(EMPTY_FORM);
  const [message, setMessage] = useState("");

  const load = async () => {
    try {
      const data = await api.getAdminLayouts();
      setLayouts(data);
      setMessage("");
    } catch (e) {
      setMessage(e.message);
    }
  };

  useEffect(() => {
    load().catch(() => {});
  }, []);

  const submit = async (event) => {
    event.preventDefault();
    try {
      await api.upsertAdminLayout(form.id || 0, {
        name: form.name,
        route: form.route,
        layoutJson: form.layoutJson,
        isDefault: form.isDefault
      });
      setForm(EMPTY_FORM);
      await load();
      setMessage("Layout saved.");
    } catch (e) {
      setMessage(e.message);
    }
  };

  return (
    <section className="page">
      <h2>Admin: Panel Layouts</h2>
      {message && <div className="card status-card">{message}</div>}

      <form className="card admin-form" onSubmit={submit}>
        <input className="input" placeholder="Layout name" value={form.name} onChange={(e) => setForm((s) => ({ ...s, name: e.target.value }))} />
        <input className="input" placeholder="Route (/panel/...)" value={form.route} onChange={(e) => setForm((s) => ({ ...s, route: e.target.value }))} />
        <textarea className="input admin-textarea" value={form.layoutJson} onChange={(e) => setForm((s) => ({ ...s, layoutJson: e.target.value }))} />
        <label className="row">
          <span>Default layout</span>
          <input type="checkbox" checked={form.isDefault} onChange={(e) => setForm((s) => ({ ...s, isDefault: e.target.checked }))} />
        </label>
        <div className="controls">
          <button className="btn btn-big" type="submit">Save</button>
          <button className="btn" type="button" onClick={() => setForm(EMPTY_FORM)}>Reset</button>
        </div>
      </form>

      <article className="card">
        <h3>Current layouts</h3>
        <div className="admin-table">
          <div className="admin-row admin-row-head">
            <span>Name</span>
            <span>Route</span>
            <span>Default</span>
            <span>Updated</span>
          </div>
          {layouts.map((layout) => (
            <button key={layout.id} className="admin-row admin-row-button" onClick={() => setForm({
              id: layout.id,
              name: layout.name,
              route: layout.route,
              layoutJson: layout.layoutJson,
              isDefault: layout.isDefault
            })}>
              <span>{layout.name}</span>
              <span>{layout.route}</span>
              <span>{layout.isDefault ? "yes" : "no"}</span>
              <span>{new Date(layout.updatedAtUtc).toLocaleString()}</span>
            </button>
          ))}
        </div>
      </article>
    </section>
  );
}
