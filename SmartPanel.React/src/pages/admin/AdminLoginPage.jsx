import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../../api";

export default function AdminLoginPage() {
  const navigate = useNavigate();
  const [username, setUsername] = useState("admin");
  const [password, setPassword] = useState("admin12345");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  const submit = async (event) => {
    event.preventDefault();
    setLoading(true);
    setError("");

    try {
      const response = await api.login({ username, password });
      api.setAuthToken(response.token);
      navigate("/admin/devices");
    } catch (e) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <section className="page">
      <h2>Admin Login</h2>
      <form className="card admin-form" onSubmit={submit}>
        <label>
          Username
          <input className="input" value={username} onChange={(e) => setUsername(e.target.value)} />
        </label>
        <label>
          Password
          <input className="input" type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
        </label>
        <button className="btn btn-big" type="submit" disabled={loading}>
          {loading ? "Signing in..." : "Sign in"}
        </button>
        {error && <p className="bad">{error}</p>}
      </form>
    </section>
  );
}
