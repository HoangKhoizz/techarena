(() => {
  const TOKEN_KEY = "pcstore_pos_token";
  const PROFILE_KEY = "pcstore_pos_profile";

  function getToken() {
    return localStorage.getItem(TOKEN_KEY);
  }
  function getProfile() {
    try {
      return JSON.parse(localStorage.getItem(PROFILE_KEY) || "null");
    } catch {
      return null;
    }
  }
  function clearSession() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(PROFILE_KEY);
  }

  // Chưa mở ca → về trang Đăng nhập ca
  if (!getToken() || !getProfile()) {
    window.location.replace("/Pos/Login");
    return;
  }

  const el = {
    sessionLabel: document.getElementById("sessionLabel"),
    btnLogout: document.getElementById("btnLogout"),
    productSearch: document.getElementById("productSearch"),
    productGrid: document.getElementById("productGrid"),
    productEmpty: document.getElementById("productEmpty"),
    btnRefresh: document.getElementById("btnRefresh"),
    cartList: document.getElementById("cartList"),
    cartTotal: document.getElementById("cartTotal"),
    amountPaid: document.getElementById("amountPaid"),
    changeDue: document.getElementById("changeDue"),
    receiverName: document.getElementById("receiverName"),
    receiverPhone: document.getElementById("receiverPhone"),
    phoneHint: document.getElementById("phoneHint"),
    btnPay: document.getElementById("btnPay"),
    btnClearCart: document.getElementById("btnClearCart"),
    payError: document.getElementById("payError"),
    payOk: document.getElementById("payOk"),
    rcOrderId: document.getElementById("rcOrderId"),
    rcDate: document.getElementById("rcDate"),
    rcCashier: document.getElementById("rcCashier"),
    rcCustomer: document.getElementById("rcCustomer"),
    rcPhone: document.getElementById("rcPhone"),
    rcItems: document.getElementById("rcItems"),
    rcTotal: document.getElementById("rcTotal"),
    rcPaid: document.getElementById("rcPaid"),
    rcChange: document.getElementById("rcChange"),
  };

  /** @type {Map<number, {componentId:number,sku:string,name:string,priceVnd:number,stockQty:number,qty:number}>} */
  const cart = new Map();
  let searchTimer = null;
  let phoneTimer = null;
  /** Giá trị số thực của tiền khách đưa */
  let amountPaidValue = 0;

  const money = (n) =>
    new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND", maximumFractionDigits: 0 }).format(n || 0);

  const formatThousands = (n) =>
    new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 0 }).format(Math.floor(n || 0));

  const profile = getProfile();
  el.sessionLabel.textContent = `${profile.fullName || profile.username} · ${profile.role}`;

  function showError(node, msg) {
    if (!node) return;
    if (!msg) {
      node.hidden = true;
      node.textContent = "";
      return;
    }
    node.hidden = false;
    node.textContent = msg;
  }

  async function api(path, options = {}) {
    const headers = { "Content-Type": "application/json", ...(options.headers || {}) };
    const token = getToken();
    if (token) headers.Authorization = `Bearer ${token}`;

    const res = await fetch(path, { ...options, headers });
    let data = null;
    const text = await res.text();
    if (text) {
      try {
        data = JSON.parse(text);
      } catch {
        data = { raw: text };
      }
    }

    if (!res.ok) {
      if (res.status === 401) {
        clearSession();
        window.location.replace("/Pos/Login");
      }
      const err = new Error((data && (data.error || data.title)) || `HTTP ${res.status}`);
      err.status = res.status;
      err.data = data;
      throw err;
    }
    return data;
  }

  function logout() {
    clearSession();
    window.location.replace("/Pos/Login");
  }

  async function loadProducts(q = "") {
    const query = q ? `?q=${encodeURIComponent(q)}` : "";
    const items = await api(`/api/pos/products${query}`);
    renderProducts(items || []);
  }

  function renderProducts(items) {
    el.productGrid.innerHTML = "";
    el.productEmpty.hidden = items.length > 0;
    for (const p of items) {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "pos-card";
      btn.innerHTML = `
        <div class="sku">${escapeHtml(p.sku)}</div>
        <div class="name">${escapeHtml(p.name)}</div>
        <div class="meta">
          <span class="price">${money(p.priceVnd)}</span>
          <span class="stock">Kho ${p.stockQty}</span>
        </div>`;
      btn.addEventListener("click", () => addToCart(p));
      el.productGrid.appendChild(btn);
    }
  }

  function addToCart(p) {
    const existing = cart.get(p.componentId);
    if (existing) {
      if (existing.qty >= p.stockQty) return;
      existing.qty += 1;
      existing.stockQty = p.stockQty;
    } else {
      cart.set(p.componentId, {
        componentId: p.componentId,
        sku: p.sku,
        name: p.name,
        priceVnd: p.priceVnd,
        stockQty: p.stockQty,
        qty: 1,
      });
    }
    renderCart();
  }

  function changeQty(componentId, delta) {
    const item = cart.get(componentId);
    if (!item) return;
    item.qty += delta;
    if (item.qty <= 0) cart.delete(componentId);
    else if (item.qty > item.stockQty) item.qty = item.stockQty;
    renderCart();
  }

  function cartTotal() {
    let sum = 0;
    for (const item of cart.values()) sum += item.priceVnd * item.qty;
    return sum;
  }

  function renderCart() {
    el.cartList.innerHTML = "";
    if (cart.size === 0) {
      el.cartList.innerHTML = `<p class="pos-empty">Chưa có sản phẩm. Chạm thẻ bên trái để thêm.</p>`;
    } else {
      for (const item of cart.values()) {
        const row = document.createElement("div");
        row.className = "pos-cart-item";
        row.innerHTML = `
          <div class="title">${escapeHtml(item.name)}</div>
          <div class="line-total">${money(item.priceVnd * item.qty)}</div>
          <div class="pos-qty">
            <button type="button" data-act="dec" aria-label="Giảm">−</button>
            <span>${item.qty}</span>
            <button type="button" data-act="inc" aria-label="Tăng">+</button>
            <span class="stock">${money(item.priceVnd)} / sp</span>
          </div>`;
        row.querySelector('[data-act="dec"]').addEventListener("click", () => changeQty(item.componentId, -1));
        row.querySelector('[data-act="inc"]').addEventListener("click", () => changeQty(item.componentId, 1));
        el.cartList.appendChild(row);
      }
    }

    el.cartTotal.textContent = money(cartTotal());
    updateChange();
    el.btnPay.disabled = cart.size === 0;
    showError(el.payError, "");
    showError(el.payOk, "");
  }

  function parseDigits(raw) {
    return Number(String(raw || "").replace(/\D/g, "")) || 0;
  }

  function onAmountPaidInput() {
    const digits = String(el.amountPaid.value || "").replace(/\D/g, "");
    amountPaidValue = digits ? Number(digits) : 0;
    el.amountPaid.value = digits ? formatThousands(amountPaidValue) : "";
    updateChange();
  }

  function updateChange() {
    const total = cartTotal();
    const change = Math.max(0, amountPaidValue - total);
    el.changeDue.textContent = money(change);
  }

  async function lookupCustomerByPhone() {
    const phone = el.receiverPhone.value.trim();
    el.phoneHint.hidden = true;
    el.phoneHint.textContent = "";
    if (phone.replace(/\D/g, "").length < 9) return;

    try {
      const data = await api(`/api/pos/customer-by-phone?phone=${encodeURIComponent(phone)}`);
      if (data && data.found) {
        if (data.fullName && !el.receiverName.value.trim()) {
          el.receiverName.value = data.fullName;
        } else if (data.fullName) {
          el.receiverName.value = data.fullName;
        }
        el.phoneHint.hidden = false;
        el.phoneHint.textContent = `Đã tìm thấy KH: ${data.fullName || data.username || ""}`;
      } else {
        el.phoneHint.hidden = false;
        el.phoneHint.textContent = "Khách mới (chưa có trong hệ thống)";
      }
    } catch {
      /* bỏ qua lỗi lookup */
    }
  }

  function buildCheckoutPayload() {
    const total = cartTotal();
    return {
      receiverName: el.receiverName.value.trim() || "Khách tại quầy",
      receiverPhone: el.receiverPhone.value.trim() || null,
      receiverEmail: null,
      note: null,
      paymentMethodCode: "CASH",
      amountPaid: amountPaidValue > 0 ? amountPaidValue : null,
      items: [...cart.values()].map((x) => ({
        componentId: x.componentId,
        qty: x.qty,
      })),
      _lines: [...cart.values()],
      _total: total,
      _paid: amountPaidValue,
      _change: Math.max(0, amountPaidValue - total),
    };
  }

  function fillReceipt(result, payload) {
    const p = getProfile();
    el.rcOrderId.textContent = `#${result.invoiceId || result.orderId}`;
    el.rcDate.textContent = new Date().toLocaleString("vi-VN");
    el.rcCashier.textContent = p?.fullName || p?.username || "—";
    el.rcCustomer.textContent = result.receiverName || payload.receiverName || "Khách tại quầy";
    el.rcPhone.textContent = result.receiverPhone || payload.receiverPhone || "—";
    el.rcItems.innerHTML = "";
    for (const line of payload._lines) {
      const tr = document.createElement("tr");
      tr.innerHTML = `
        <td>${escapeHtml(line.name)}</td>
        <td class="num">${line.qty}</td>
        <td class="num">${formatThousands(line.priceVnd)}</td>
        <td class="num">${formatThousands(line.priceVnd * line.qty)}</td>`;
      el.rcItems.appendChild(tr);
    }
    el.rcTotal.textContent = formatThousands(result.totalPriceVnd ?? payload._total);
    el.rcPaid.textContent = formatThousands(payload._paid);
    el.rcChange.textContent = formatThousands(result.changeDue ?? payload._change);
  }

  function printReceipt() {
    document.body.classList.add("printing-receipt");
    window.print();
    const done = () => document.body.classList.remove("printing-receipt");
    window.addEventListener("afterprint", done, { once: true });
    setTimeout(done, 800);
  }

  async function checkout() {
    showError(el.payError, "");
    showError(el.payOk, "");
    if (cart.size === 0) return;

    const payload = buildCheckoutPayload();
    const { _lines, _total, _paid, _change, ...body } = payload;

    el.btnPay.disabled = true;
    try {
      const result = await api("/api/invoices/create-offline", {
        method: "POST",
        body: JSON.stringify(body),
      });

      fillReceipt(result, { ...payload, _lines, _total, _paid, _change });
      printReceipt();

      showError(
        el.payOk,
        `Đã tạo HĐ #${result.invoiceId || result.orderId} · Tổng ${money(result.totalPriceVnd)}`
      );

      cart.clear();
      amountPaidValue = 0;
      el.amountPaid.value = "";
      el.receiverPhone.value = "";
      el.receiverName.value = "";
      el.phoneHint.hidden = true;
      renderCart();
      await loadProducts(el.productSearch.value.trim());
    } catch (err) {
      showError(el.payError, err.message || "Thanh toán thất bại.");
      el.btnPay.disabled = cart.size === 0;
    }
  }

  function escapeHtml(s) {
    return String(s ?? "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;");
  }

  el.btnLogout.addEventListener("click", logout);
  el.btnRefresh.addEventListener("click", () => loadProducts(el.productSearch.value.trim()));
  el.btnClearCart.addEventListener("click", () => {
    cart.clear();
    renderCart();
  });
  el.btnPay.addEventListener("click", checkout);
  el.amountPaid.addEventListener("input", onAmountPaidInput);
  el.receiverPhone.addEventListener("input", () => {
    clearTimeout(phoneTimer);
    phoneTimer = setTimeout(lookupCustomerByPhone, 350);
  });
  el.productSearch.addEventListener("input", () => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => {
      loadProducts(el.productSearch.value.trim()).catch((err) => showError(el.payError, err.message));
    }, 220);
  });

  renderCart();
  loadProducts().catch(() => {
    clearSession();
    window.location.replace("/Pos/Login");
  });
})();
