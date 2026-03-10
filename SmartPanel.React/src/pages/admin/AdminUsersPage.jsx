import { useEffect, useState } from "react";
import { api } from "../../api";

export default function AdminUsersPage() {
  const [users, setUsers] = useState([]);
  const [message, setMessage] = useState("");

  useEffect(() => {
    api.getAdminUsers()
      .then((data) => setUsers(data))
      .catch((e) => setMessage(e.message));
  }, []);

  return (
    <section className="page">
      <h2>Admin: Users</h2>
      {message && <div className="card status-card">{message}</div>}
      <article className="card">
        <div className="admin-table">
          <div className="admin-row admin-row-head">
            <span>Username</span>
            <span>Roles</span>
            <span>Status</span>
            <span>Updated</span>
          </div>
          {users.map((user) => (
            <div className="admin-row" key={user.id}>
              <span>{user.username}</span>
              <span>{(user.roles || []).join(", ") || "-"}</span>
              <span>{user.isActive ? "active" : "disabled"}</span>
              <span>{new Date(user.updatedAtUtc).toLocaleString()}</span>
            </div>
          ))}
        </div>
      </article>
    </section>
  );
}
