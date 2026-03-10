import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "../api";

export default function RoomsPage() {
  const [rooms, setRooms] = useState([]);
  const [tvs, setTvs] = useState([]);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [hours, setHours] = useState(24);
  const [selected, setSelected] = useState(null);
  const [targetInput, setTargetInput] = useState("");
  const [message, setMessage] = useState("");
  const [tvSources, setTvSources] = useState({});
  const [tvVolumes, setTvVolumes] = useState({});
  const [toasts, setToasts] = useState([]);
  const [activityLog, setActivityLog] = useState([]);
  const [expandedRoom, setExpandedRoom] = useState("");
  const [activeTvControlId, setActiveTvControlId] = useState("");
  const [assignDrafts, setAssignDrafts] = useState({});
  const [dragDeviceId, setDragDeviceId] = useState("");
  const [showUnassigned, setShowUnassigned] = useState(true);
  const previousTrackedRef = useRef(new Map());

  const roomsWithTvs = useMemo(() => mergeRoomsAndTvs(rooms, tvs), [rooms, tvs]);
  const roomNamesForAssign = useMemo(
    () => rooms.map((r) => r.name).filter((name) => normalize(name) !== "unassigned"),
    [rooms]
  );

  const filteredRooms = useMemo(() => {
    const q = search.trim().toLowerCase();

    return roomsWithTvs
      .map((r) => {
        const sensorsByStatus = (r.sensors || [])
          .filter((s) => statusFilter === "all" || (statusFilter === "online" ? s.online : !s.online));
        const sensors = sensorsByStatus
          .filter((s) => !q || s.name.toLowerCase().includes(q) || s.id.toLowerCase().includes(q) || s.type.toLowerCase().includes(q));

        const tvByStatus = (r.tvDevices || [])
          .filter((t) => statusFilter === "all" || (statusFilter === "online" ? t.online : !t.online));
        const tvDevices = tvByStatus
          .filter((t) => !q || t.name.toLowerCase().includes(q) || t.id.toLowerCase().includes(q));

        const roomMatches = !q || r.name.toLowerCase().includes(q);
        if (sensors.length === 0 && tvDevices.length === 0 && !roomMatches) return null;

        return {
          ...r,
          sensors: sensors.length > 0 ? sensors : sensorsByStatus,
          tvDevices: tvDevices.length > 0 ? tvDevices : tvByStatus
        };
      })
      .filter(Boolean);
  }, [roomsWithTvs, search, statusFilter]);

  const climateModes = selected?.availableModes?.length ? selected.availableModes : ["off", "cool", "heat", "fan_only", "dry", "auto"];
  const historyPoints = (selected?.history || []).filter((x) => x.value != null).sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp));
  const analytics = useMemo(() => buildAnalytics(filteredRooms), [filteredRooms]);
  const unassignedRoom = useMemo(
    () => filteredRooms.find((room) => normalize(room.name) === "unassigned") || null,
    [filteredRooms]
  );
  const unassignedGroups = useMemo(
    () => buildUnassignedGroups(unassignedRoom),
    [unassignedRoom]
  );
  const visibleRooms = useMemo(
    () => filteredRooms.filter((room) => normalize(room.name) !== "unassigned"),
    [filteredRooms]
  );

  const loadRooms = async () => {
    const [roomData, tvData] = await Promise.all([api.getRooms(), api.getTvs()]);
    setRooms(roomData);
    setTvs(tvData);
    setTvSources((prev) => {
      const next = { ...prev };
      for (const tv of tvData) {
        if (!next[tv.id]) next[tv.id] = tv.source || tv.availableSources?.[0] || "";
      }
      return next;
    });
    setTvVolumes((prev) => {
      const next = { ...prev };
      for (const tv of tvData) {
        if (next[tv.id] == null) next[tv.id] = tv.volumePercent ?? 20;
      }
      return next;
    });
    handleSensorToasts(roomData);
  };

  useEffect(() => {
    loadRooms().catch((e) => setMessage(e.message));
    const timer = setInterval(() => loadRooms().catch(() => {}), 15000);
    return () => clearInterval(timer);
  }, []);

  useEffect(() => {
    if (selected?.entityId) {
      loadDetails(selected.entityId);
    }
  }, [hours]);

  const loadDetails = async (entityId) => {
    try {
      const d = await api.getSensorDetails(entityId, hours);
      setSelected(d);
      setTargetInput(d?.targetTemperature?.toString() ?? d?.currentValue?.toString() ?? "");
      setMessage("");
    } catch (e) {
      setMessage(e.message);
    }
  };

  const sendClimate = async (payload) => {
    if (!selected) return;
    try {
      const prepared = prepareClimatePayload(payload, selected);
      await api.controlClimate({ entityId: selected.entityId, ...prepared });
      setMessage("Climate command sent.");
      await loadDetails(selected.entityId);
      await loadRooms();
    } catch (e) {
      setMessage(e.message);
    }
  };

  const sendTvCommand = async (payload) => {
    try {
      await api.controlTv(payload);
      setMessage("TV command sent.");
      await loadRooms();
    } catch (e) {
      setMessage(e.message);
    }
  };

  const applyTvVolume = async (tvId) => {
    const volumePercent = Number(tvVolumes[tvId] ?? 20);
    await sendTvCommand({ entityId: tvId, volumePercent });
  };

  const assignDeviceToRoom = async (entityId, roomName) => {
    try {
      await api.assignDeviceRoom({ entityId, roomName });
      setMessage(`Assigned ${entityId} to ${roomName}.`);
      setAssignDrafts((prev) => ({ ...prev, [entityId]: roomName }));
      setExpandedRoom(roomName);
      setSearch("");
      setStatusFilter("all");
      await loadRooms();
    } catch (e) {
      setMessage(e.message);
    }
  };

  const assignGroupToRoom = async (group, roomName) => {
    try {
      const tasks = group.items.map((item) => api.assignDeviceRoom({ entityId: item.entityId, roomName }));
      const results = await Promise.allSettled(tasks);
      const okCount = results.filter((x) => x.status === "fulfilled").length;
      const failed = results.length - okCount;
      if (failed === 0) {
        setMessage(`Assigned ${okCount} devices to ${roomName}.`);
      } else {
        setMessage(`Assigned ${okCount} devices to ${roomName}, failed: ${failed}.`);
      }
      setExpandedRoom(roomName);
      setSearch("");
      setStatusFilter("all");
      await loadRooms();
    } catch (e) {
      setMessage(e.message);
    }
  };

  const enqueueToast = (text, kind = "info") => {
    const id = `${Date.now()}-${Math.random()}`;
    setToasts((prev) => [...prev, { id, text, kind }]);
    setTimeout(() => {
      setToasts((prev) => prev.filter((x) => x.id !== id));
    }, 4500);
  };

  const appendActivityLog = (roomName, sensorName, action) => {
    const item = {
      id: `${Date.now()}-${Math.random()}`,
      roomName,
      sensorName,
      action,
      time: new Date()
    };

    setActivityLog((prev) => [item, ...prev].slice(0, 120));
  };

  const handleSensorToasts = (roomData) => {
    const tracked = new Map();
    for (const room of roomData) {
      for (const sensor of room.sensors || []) {
        if (sensor.type === "motion" || sensor.type === "door") {
          tracked.set(sensor.id, {
            name: sensor.name,
            roomName: room.name,
            type: sensor.type,
            status: (sensor.status || "").toLowerCase()
          });
        }
      }
    }

    if (previousTrackedRef.current.size > 0) {
      for (const [id, current] of tracked.entries()) {
        const prev = previousTrackedRef.current.get(id);
        if (!prev || prev.status === current.status) continue;

        if (current.type === "motion") {
          if (current.status.includes("detected") || current.status.includes("on")) {
            enqueueToast(`Motion detected: ${current.name}`, "warn");
            appendActivityLog(current.roomName, current.name, "Motion detected");
          } else {
            enqueueToast(`Motion cleared: ${current.name}`, "ok");
            appendActivityLog(current.roomName, current.name, "Motion cleared");
          }
        }

        if (current.type === "door") {
          if (current.status.includes("open") || current.status.includes("on")) {
            enqueueToast(`Door opened: ${current.name}`, "warn");
            appendActivityLog(current.roomName, current.name, "Door opened");
          } else if (current.status.includes("closed") || current.status.includes("off")) {
            enqueueToast(`Door closed: ${current.name}`, "ok");
            appendActivityLog(current.roomName, current.name, "Door closed");
          }
        }
      }
    }

    previousTrackedRef.current = tracked;
  };

  return (
    <section className="page rooms-page">
      <div className="page-header-row">
        <h2>Rooms</h2>
        <button className="btn btn-big" onClick={() => loadRooms().catch((e) => setMessage(e.message))}>Scan now</button>
      </div>

      <div className="card filters">
        <input className="input" placeholder="Search room/device" value={search} onChange={(e) => setSearch(e.target.value)} />
        <select className="input" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
          <option value="all">All</option>
          <option value="online">Online only</option>
          <option value="offline">Offline only</option>
        </select>
        <select className="input" value={hours} onChange={(e) => setHours(Number(e.target.value))}>
          <option value={6}>6h</option>
          <option value={12}>12h</option>
          <option value={24}>24h</option>
          <option value={48}>48h</option>
        </select>
      </div>

      <div className="analytics-grid">
        <article className="card analytic-card">
          <span className="analytic-label">Avg Temperature</span>
          <strong>{analytics.avgTemp ?? "-"} C</strong>
        </article>
        <article className="card analytic-card">
          <span className="analytic-label">Avg Humidity</span>
          <strong>{analytics.avgHumidity ?? "-"} %</strong>
        </article>
        <article className="card analytic-card">
          <span className="analytic-label">Temp Range</span>
          <strong>{analytics.tempRange}</strong>
        </article>
        <article className="card analytic-card">
          <span className="analytic-label">Humidity Range</span>
          <strong>{analytics.humidityRange}</strong>
        </article>
      </div>

      {message && <div className="card status-card">{message}</div>}

      {unassignedRoom && ((unassignedRoom.sensors?.length || 0) + (unassignedRoom.tvDevices?.length || 0) > 0) && (
        <article className="card unassigned-panel">
          <div className="row">
            <h3>Unassigned devices</h3>
            <div className="controls">
              <span className="chip chip-active">{(unassignedRoom.sensors?.length || 0) + (unassignedRoom.tvDevices?.length || 0)} items</span>
              <button className="btn" onClick={() => setShowUnassigned((v) => !v)}>
                {showUnassigned ? "Hide list" : "Show list"}
              </button>
            </div>
          </div>
          <div className="small">Assign a room to move device out of this list.</div>
          {showUnassigned && <div className="unassigned-grid">
            {unassignedGroups.map((group) => {
              const defaultRoom = assignDrafts[group.id] || roomNamesForAssign[0] || "";
              return (
                <div key={group.id} className="unassigned-item">
                  <div className="unassigned-title-row">
                    <strong>{group.title}</strong>
                    <span className="small">{group.items.length} entities</span>
                  </div>
                  <div className="small">{group.items.map((x) => x.kind).join(" | ")}</div>
                  <div className="unassigned-entities">
                    {group.items.slice(0, 4).map((item) => (
                      <div key={item.entityId} className="small">{item.name} - {item.entityId}</div>
                    ))}
                    {group.items.length > 4 && <div className="small">+{group.items.length - 4} more...</div>}
                  </div>
                  <div className="assign-row">
                    <select
                      className="input"
                      value={defaultRoom}
                      onChange={(e) => setAssignDrafts((prev) => ({ ...prev, [group.id]: e.target.value }))}>
                      {roomNamesForAssign.map((name) => (
                        <option key={name} value={name}>{name}</option>
                      ))}
                    </select>
                    <button className="btn" onClick={() => assignGroupToRoom(group, defaultRoom)}>Assign all</button>
                  </div>
                </div>
              );
            })}
          </div>}
        </article>
      )}

      <div className="rooms-layout">
        <article className="card details-panel">
          <h3>Sensor details</h3>
          {!selected && <div className="small">Select sensor in room list.</div>}
          {selected && (
            <>
              <div className="details-grid">
                <div className="detail-row"><span>Name</span><strong>{selected.displayName}</strong></div>
                <div className="detail-row"><span>Room</span><strong>{selected.roomName}</strong></div>
                <div className="detail-row"><span>State</span><strong>{selected.displayState}</strong></div>
                <div className="detail-row"><span>Current</span><strong>{selected.currentValue != null ? `${selected.currentValue} ${selected.unit}` : selected.displayState}</strong></div>
                <div className="detail-row"><span>Requested</span><strong>{new Date(selected.requestedAt).toLocaleTimeString()}</strong></div>
              </div>

              {selected.type === "climate" && (
                <div className="climate-box">
                  <div className="controls">
                    <button className="btn btn-big" onClick={() => sendClimate({ powerOn: true, hvacMode: pickPowerOnMode(selected) })}>Power On</button>
                    <button className="btn btn-big" onClick={() => sendClimate({ powerOn: false })}>Power Off</button>
                    {climateModes.map((m) => (
                      <button key={m} className={`btn ${selected.currentMode === m ? "btn-active" : ""}`} onClick={() => sendClimate({ hvacMode: m })}>
                        {m}
                      </button>
                    ))}
                  </div>
                  <div className="controls">
                    <button className="btn" onClick={() => setTargetInput((Number(targetInput || 0) - 0.5).toString())}>-0.5</button>
                    <input className="input input-small" value={targetInput} onChange={(e) => setTargetInput(e.target.value)} />
                    <button className="btn" onClick={() => setTargetInput((Number(targetInput || 0) + 0.5).toString())}>+0.5</button>
                    <button className="btn btn-big" onClick={() => sendClimate({ targetTemperature: Number(targetInput) })}>Apply</button>
                  </div>
                </div>
              )}

              {historyPoints.length >= 2 && <HistoryChart points={historyPoints} unit={selected.unit} />}
            </>
          )}
        </article>

        <article className="card">
          <h3>Rooms</h3>
          <div className="rooms-board">
            {visibleRooms.map((room) => {
              const roomIsExpanded = expandedRoom === room.name;
              const temp = getRoomMetric(room, "temp");
              const humidity = getRoomMetric(room, "humidity");

              return (
                <div
                  key={room.name}
                  className={`room-card-large ${roomIsExpanded ? "room-card-expanded" : ""}`}
                  onDragOver={(e) => e.preventDefault()}
                  onDrop={(e) => {
                    e.preventDefault();
                    const entityId = e.dataTransfer.getData("text/plain") || dragDeviceId;
                    if (!entityId) return;
                    assignDeviceToRoom(entityId, room.name);
                    setDragDeviceId("");
                  }}>
                  <div className="room-card-header">
                    <h4>{room.name}</h4>
                    <span>{(room.sensors?.length || 0) + (room.tvDevices?.length || 0)} devices</span>
                  </div>

                  <div className="room-quick-metrics">
                    <span className="chip">{temp != null ? `${temp.toFixed(1)} C` : "Temp -"}</span>
                    <span className="chip">{humidity != null ? `${humidity.toFixed(1)} %` : "Humidity -"}</span>
                    <button className="btn room-open-btn" onClick={() => setExpandedRoom(roomIsExpanded ? "" : room.name)}>
                      {roomIsExpanded ? "Collapse" : "Open room"}
                    </button>
                  </div>

                  {roomIsExpanded && (
                    <div className="device-grid">
                      {(room.sensors || []).map((s) => (
                        <div
                          key={s.id}
                          className={`sensor sensor-large ${selected?.entityId === s.id ? "sensor-active" : ""}`}>
                          <button
                            className="sensor-main"
                            draggable
                            onDragStart={(e) => {
                              setDragDeviceId(s.id);
                              e.dataTransfer.setData("text/plain", s.id);
                            }}
                            onClick={() => loadDetails(s.id)}>
                            <span className="sensor-title">{s.name}</span>
                            <span className="sensor-meta-line">{readableSensorType(s.type)}</span>
                            <span className={s.online ? "ok" : "bad"}>{s.online ? "Online" : "Offline"}</span>
                          </button>

                        </div>
                      ))}

                      {(room.tvDevices || []).map((tv) => {
                        const controlsOpened = activeTvControlId === tv.id;
                        return (
                          <div
                            key={tv.id}
                            className="tv-room-card"
                            draggable
                            onDragStart={(e) => {
                              setDragDeviceId(tv.id);
                              e.dataTransfer.setData("text/plain", tv.id);
                            }}>
                            <div className="row">
                              <strong>{tv.name}</strong>
                              <span className={tv.online ? "ok" : "bad"}>{tv.online ? "Online" : "Offline"}</span>
                            </div>
                            <div className="small">State: {tv.state}</div>
                            {roomNamesForAssign.length > 0 && (
                              <div className="assign-row">
                                <select
                                  className="input"
                                  value={assignDrafts[tv.id] || tv.roomName || roomNamesForAssign[0]}
                                  onChange={(e) => setAssignDrafts((prev) => ({ ...prev, [tv.id]: e.target.value }))}>
                                  {roomNamesForAssign.map((name) => (
                                    <option key={name} value={name}>{name}</option>
                                  ))}
                                </select>
                                <button className="btn" onClick={() => assignDeviceToRoom(tv.id, assignDrafts[tv.id] || tv.roomName || roomNamesForAssign[0])}>
                                  Assign
                                </button>
                              </div>
                            )}
                            <div className="controls">
                              <button
                                className="btn"
                                onClick={() => setActiveTvControlId(controlsOpened ? "" : tv.id)}>
                                {controlsOpened ? "Hide controls" : "Open controls"}
                              </button>
                            </div>

                            {controlsOpened && (
                              <>
                                <div className="controls">
                                  <button className="btn btn-big" onClick={() => sendTvCommand({ entityId: tv.id, powerOn: true })}>Power On</button>
                                  <button className="btn btn-big" onClick={() => sendTvCommand({ entityId: tv.id, powerOn: false })}>Power Off</button>
                                  <button className="btn btn-big" onClick={() => sendTvCommand({ entityId: tv.id, togglePlayPause: true })}>Play/Pause</button>
                                </div>
                                <div className="volume-block">
                                  <label>Volume: {tvVolumes[tv.id] ?? tv.volumePercent ?? 0}%</label>
                                  <input
                                    type="range"
                                    min={0}
                                    max={100}
                                    value={tvVolumes[tv.id] ?? tv.volumePercent ?? 0}
                                    onChange={(e) => setTvVolumes((prev) => ({ ...prev, [tv.id]: Number(e.target.value) }))}
                                  />
                                  <button className="btn btn-big" onClick={() => applyTvVolume(tv.id)}>Apply Volume</button>
                                </div>
                                {tv.availableSources?.length > 0 && (
                                  <div className="controls">
                                    <select
                                      className="input"
                                      value={tvSources[tv.id] || ""}
                                      onChange={(e) => setTvSources((prev) => ({ ...prev, [tv.id]: e.target.value }))}>
                                      {tv.availableSources.map((source) => <option key={source} value={source}>{source}</option>)}
                                    </select>
                                    <button className="btn btn-big" onClick={() => sendTvCommand({ entityId: tv.id, source: tvSources[tv.id] })}>Set source</button>
                                  </div>
                                )}
                              </>
                            )}
                          </div>
                        );
                      })}
                    </div>
                  )}

                  {activityLog.filter((x) => x.roomName === room.name).length > 0 && (
                    <div className="activity-block">
                      <h5>Activity log</h5>
                      <div className="activity-list">
                        {activityLog
                          .filter((x) => x.roomName === room.name)
                          .slice(0, 10)
                          .map((entry) => (
                            <div key={entry.id} className="activity-row">
                              <span><strong>{entry.sensorName}</strong> - {entry.action}</span>
                              <span>{entry.time.toLocaleTimeString()}</span>
                            </div>
                          ))}
                      </div>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </article>
      </div>

      <div className="toast-stack">
        {toasts.map((t) => (
          <div key={t.id} className={`toast toast-${t.kind}`}>
            {t.text}
          </div>
        ))}
      </div>
    </section>
  );
}

function getRoomMetric(room, metric) {
  const values = (room.sensors || [])
    .filter((s) => metric === "temp" ? isTemperatureSensor(s) : isHumiditySensor(s))
    .map((s) => Number(s.value))
    .filter((v) => Number.isFinite(v));

  if (values.length === 0) return null;
  return values.reduce((a, b) => a + b, 0) / values.length;
}

function buildUnassignedGroups(unassignedRoom) {
  if (!unassignedRoom) return [];
  const entries = [
    ...((unassignedRoom.sensors || []).map((s) => ({
      entityId: s.id,
      name: s.name,
      kind: readableSensorType(s.type)
    }))),
    ...((unassignedRoom.tvDevices || []).map((tv) => ({
      entityId: tv.id,
      name: tv.name,
      kind: "TV"
    })))
  ];

  const groups = new Map();
  for (const entry of entries) {
    const key = deriveGroupKey(entry.entityId);
    const group = groups.get(key) || {
      id: key,
      title: humanizeGroupKey(key),
      items: []
    };
    group.items.push(entry);
    groups.set(key, group);
  }

  return [...groups.values()]
    .sort((a, b) => b.items.length - a.items.length || a.title.localeCompare(b.title));
}

function deriveGroupKey(entityId) {
  const part = String(entityId || "").toLowerCase().split(".").slice(1).join(".") || entityId;
  let key = part;
  const suffixes = [
    "_motion_tracking",
    "_sound_detection",
    "_motion_recording",
    "_recording",
    "_indicator_light",
    "_floodlight",
    "_motion_alarm",
    "_temperature",
    "_humidity",
    "_autofocus",
    "_wiper",
    "_ir_lamp",
    "_flip",
    "_motion"
  ];
  for (const suffix of suffixes) {
    if (key.endsWith(suffix)) {
      key = key.slice(0, -suffix.length);
    }
  }
  return key;
}

function humanizeGroupKey(key) {
  return String(key || "")
    .replace(/[_\.]+/g, " ")
    .replace(/\b\w/g, (x) => x.toUpperCase())
    .trim();
}

function isTemperatureSensor(sensor) {
  const type = String(sensor?.type || "").toLowerCase();
  const unit = String(sensor?.unit || "").toLowerCase();
  return type === "temp" || unit.includes("c");
}

function isHumiditySensor(sensor) {
  const type = String(sensor?.type || "").toLowerCase();
  const unit = String(sensor?.unit || "").toLowerCase();
  return type === "humidity" || unit.includes("%");
}

function readableSensorType(type) {
  const value = String(type || "").toLowerCase();
  if (value === "temp") return "Temperature";
  if (value === "humidity") return "Humidity";
  if (value === "climate") return "Conditioner";
  return value;
}

function prepareClimatePayload(payload, selected) {
  const next = { ...payload };

  if (typeof next.hvacMode === "string") {
    const available = (selected?.availableModes || []).map((x) => normalizeMode(x));
    const normalized = normalizeMode(next.hvacMode);
    if (available.length > 0 && !available.includes(normalized)) {
      delete next.hvacMode;
    } else {
      next.hvacMode = normalized;
    }
  }

  if (next.targetTemperature != null) {
    const parsed = Number(String(next.targetTemperature).replace(",", "."));
    if (Number.isFinite(parsed)) {
      next.targetTemperature = parsed;
    } else {
      delete next.targetTemperature;
    }
  }

  return next;
}

function pickPowerOnMode(selected) {
  const available = (selected?.availableModes || []).map((x) => normalizeMode(x));
  if (available.length === 0) {
    return "cool";
  }

  const current = normalizeMode(selected?.currentMode);
  if (current && current !== "off" && available.includes(current)) {
    return current;
  }

  const preferred = ["cool", "heat", "auto", "fan_only", "dry"];
  const best = preferred.find((x) => available.includes(x));
  return best || available[0];
}

function normalizeMode(mode) {
  return String(mode || "").trim().toLowerCase();
}

function mergeRoomsAndTvs(rooms, tvs) {
  const result = rooms.map((r) => ({ ...r, tvDevices: [] }));
  const roomByName = new Map(result.map((r) => [normalize(r.name), r]));
  let unassigned = roomByName.get("unassigned");

  for (const tv of tvs) {
    const explicitRoomKey = normalize(tv.roomName);
    const key = explicitRoomKey || pickRoomKeyForTv(tv, result);
    let room = key ? roomByName.get(key) : null;
    if (!room) {
      if (!unassigned) {
        unassigned = { name: "Unassigned", sensors: [], tvDevices: [] };
        result.push(unassigned);
        roomByName.set("unassigned", unassigned);
      }
      room = unassigned;
    }
    room.tvDevices.push(tv);
  }

  return result;
}

function pickRoomKeyForTv(tv, rooms) {
  const source = `${tv.name} ${tv.id}`.toLowerCase();
  for (const room of rooms) {
    const roomKey = normalize(room.name);
    if (!roomKey || roomKey === "unassigned") continue;
    const tokens = roomKey.split(" ").filter((x) => x.length > 2);
    if (tokens.some((token) => source.includes(token))) return roomKey;
  }
  return null;
}

function normalize(value) {
  return (value || "").toLowerCase().replace(/[^a-z0-9 ]/g, " ").replace(/\s+/g, " ").trim();
}

function buildAnalytics(rooms) {
  const tempValues = [];
  const humidityValues = [];

  for (const room of rooms) {
    for (const sensor of room.sensors || []) {
      if (sensor.value == null) continue;
      if (isTemperatureSensor(sensor)) tempValues.push(Number(sensor.value));
      if (isHumiditySensor(sensor)) humidityValues.push(Number(sensor.value));
    }
  }

  const avgTemp = tempValues.length ? (tempValues.reduce((a, b) => a + b, 0) / tempValues.length).toFixed(1) : null;
  const avgHumidity = humidityValues.length ? (humidityValues.reduce((a, b) => a + b, 0) / humidityValues.length).toFixed(1) : null;

  return {
    avgTemp,
    avgHumidity,
    tempRange: tempValues.length ? `${Math.min(...tempValues).toFixed(1)} - ${Math.max(...tempValues).toFixed(1)} C` : "-",
    humidityRange: humidityValues.length ? `${Math.min(...humidityValues).toFixed(1)} - ${Math.max(...humidityValues).toFixed(1)} %` : "-"
  };
}

function HistoryChart({ points, unit }) {
  const values = points.map((p) => Number(p.value));
  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = Math.max(max - min, 0.001);
  const minT = new Date(points[0].timestamp).getTime();
  const maxT = new Date(points[points.length - 1].timestamp).getTime();
  const total = Math.max(maxT - minT, 1);

  const linePoints = points.map((p) => {
    const x = ((new Date(p.timestamp).getTime() - minT) / total) * 300;
    const y = 110 - ((Number(p.value) - min) / span) * 100;
    return { x, y };
  });

  const polyline = linePoints.map((p) => `${p.x.toFixed(2)},${p.y.toFixed(2)}`).join(" ");
  const areaPath = `M ${linePoints[0].x.toFixed(2)} 110 L ${polyline.replaceAll(",", " ")} L ${linePoints[linePoints.length - 1].x.toFixed(2)} 110 Z`;

  return (
    <div className="history-block">
      <svg className="chart" viewBox="0 0 300 120" preserveAspectRatio="none">
        <defs>
          <linearGradient id="histFill" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="rgba(46,125,230,0.28)" />
            <stop offset="100%" stopColor="rgba(46,125,230,0.03)" />
          </linearGradient>
        </defs>
        <path d={areaPath} fill="url(#histFill)" />
        <polyline fill="none" stroke="var(--primary)" strokeWidth="2.6" points={polyline} />
      </svg>
      <div className="row small">
        <span>{new Date(points[0].timestamp).toLocaleTimeString()}</span>
        <span>Min/Max: {min.toFixed(1)}{unit} / {max.toFixed(1)}{unit}</span>
        <span>{new Date(points[points.length - 1].timestamp).toLocaleTimeString()}</span>
      </div>
    </div>
  );
}
