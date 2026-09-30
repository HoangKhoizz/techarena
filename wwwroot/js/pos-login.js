(() => {
  const TOKEN_KEY = "pcstore_pos_token";
  const PROFILE_KEY = "pcstore_pos_profile";

  // Đã mở ca → vào thẳng quầy
  if (localStorage.getItem(TOKEN_KEY) && localStorage.getItem(PROFILE_KEY)) {
    window.location.replace("/Pos");
    return;
  }

  const form = document.getElementById("loginForm");
  const user = document.getElementById("loginUser");
  const pass = document.getElementById("loginPass");
  const err = document.getElementById("loginError");

  function showError(msg) {
    if (!msg) {
      err.hidden = true;
      err.textContent = "";
      return;
    }
    err.hidden = false;
    err.textContent = msg;
  }

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    showError("");
    try {
      const res = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          usernameOrEmail: user.value.trim(),
          password: pass.value,
        }),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.error || `HTTP ${res.status}`);

      localStorage.setItem(TOKEN_KEY, data.token);
      localStorage.setItem(
        PROFILE_KEY,
        JSON.stringify({
          staffId: data.staffId,
          username: data.username,
          fullName: data.fullName,
          role: data.role,
        })
      );
      window.location.replace("/Pos");
    } catch (ex) {
      showError(ex.message || "Đăng nhập thất bại.");
    }
  });
})();
