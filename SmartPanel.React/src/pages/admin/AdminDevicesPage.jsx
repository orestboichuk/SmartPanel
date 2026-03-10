import { useEffect, useMemo, useState } from "react";
import { api } from "../../api";

const EMPTY_FORM = {
  id: 0,
  entityId: "",
  displayName: "",
  roomName: "",
  type: "sensor",
  isVisible: true,
  sortOrder: 0
};

export default function AdminDevicesPage() {
  const [devices, setDevices] = useState([]);
  const [form, setForm] = useState(EMPTY_FORM);
  const [message, setMessage] = useState("");

  const load = async () => {
    try {
      const data = await api.getAdminDevices();
      setDevices(data);
      setMessage("");
    } catch (e) {
      setMessage(e.message);
    }
  };

  useEffect(() => {
    load().catch(() => {});
  }, []);

  const sorted = useMemo(
    () => [...devices].sort((a, b) => a.roomName.localeCompare(b.roomName) || a.sortOrder - b.sortOrder || a.displayName.localeCompare(b.displayName)),
    [devices]
  );

  const submit = async (event) => {
    event.preventDefault();
    try {
      await api.upsertAdminDevice(form.id || 0, {
        entityId: form.entityId,
        displayName: form.displayName,
        roomName: form.roomName,
        type: form.type,
        isVisible: form.isVisible,
        sortOrder: Number(form.sortOrder) || 0
      });
      setForm(EMPTY_FORM);
      await load();
      setMessage("Saved.");
    } catch (e) {
      setMessage(e.message);
    }
  };

  return (
    <section className="page">
      <h2>Admin: Devices Registry</h2>
      {message && <div className="card status-card">{message}</div>}

      <form className="card admin-form" onSubmit={submit}>
        <input className="input" placeholder="Entity ID (sensor.xxx)" value={form.entityId} onChange={(e) => setForm((s) => ({ ...s, entityId: e.target.value }))} />
        <input className="input" placeholder="Display Name" value={form.displayName} onChange={(e) => setForm((s) => ({ ...s, displayName: e.target.value }))} />
        <input className="input" placeholder="Room Name" value={form.roomName} onChange={(e) => setForm((s) => ({ ...s, roomName: e.target.value }))} />
        <input className="input" placeholder="Type (sensor/light/tv/...)" value={form.type} onChange={(e) => setForm((s) => ({ ...s, type: e.target.value }))} />
        <input className="input" type="number" placeholder="Sort Order" value={form.sortOrder} onChange={(e) => setForm((s) => ({ ...s, sortOrder: e.target.value }))} />
        <label className="row">
          <span>Visible in panel</span>
          <input type="checkbox" checked={form.isVisible} onChange={(e) => setForm((s) => ({ ...s, isVisible: e.target.checked }))} />
        </label>
        <div className="controls">
          <button className="btn btn-big" type="submit">Save</button>
          <button className="btn" type="button" onClick={() => setForm(EMPTY_FORM)}>Reset</button>
        </div>
      </form>

      <article className="card">
        <h3>Current devices</h3>
        <div className="admin-table">
          <div className="admin-row admin-row-head">
            <span>Entity</span>
            <span>Name</span>
            <span>Room</span>
            <span>Type</span>
            <span>Visible</span>
            <span>Sort</span>
          </div>
          {sorted.map((d) => (
            <button key={d.id} className="admin-row admin-row-button" onClick={() => setForm({
              id: d.id,
              entityId: d.entityId,
              displayName: d.displayName,
              roomName: d.roomName,
              type: d.type,
              isVisible: d.isVisible,
              sortOrder: d.sortOrder
            })}>
              <span>{d.entityId}</span>
              <span>{d.displayName}</span>
              <span>{d.roomName}</span>
              <span>{d.type}</span>
              <span>{d.isVisible ? "yes" : "no"}</span>
              <span>{d.sortOrder}</span>
            </button>
          ))}
        </div>
      </article>
    </section>
  );
}
