(() => {
  const menuToggle = document.getElementById("taMenuToggle");
  const mobileNav = document.getElementById("taMobileNav");
  if (menuToggle && mobileNav) {
    menuToggle.addEventListener("click", () => {
      const isOpen = mobileNav.classList.toggle("open");
      menuToggle.classList.toggle("is-open", isOpen);
      menuToggle.setAttribute("aria-label", isOpen ? "Đóng menu" : "Mở menu");
    });
    mobileNav.querySelectorAll("a").forEach((link) => {
      link.addEventListener("click", () => {
        mobileNav.classList.remove("open");
        menuToggle.classList.remove("is-open");
        menuToggle.setAttribute("aria-label", "Mở menu");
      });
    });
  }

  const searchToggle = document.getElementById("taSearchToggle");
  const searchOverlay = document.getElementById("taSearchOverlay");
  if (searchToggle && searchOverlay) {
    searchToggle.addEventListener("click", () => {
      searchOverlay.classList.add("open");
      const input = document.getElementById("headerSearchKeyword");
      if (input) setTimeout(() => input.focus(), 100);
    });
    searchOverlay.addEventListener("click", (e) => {
      if (e.target === searchOverlay) searchOverlay.classList.remove("open");
    });
    document.addEventListener("keydown", (e) => {
      if (e.key === "Escape") searchOverlay.classList.remove("open");
    });
  }
})();

(() => {
  const form = document.getElementById("headerSearchForm");
  const keywordInput = document.getElementById("headerSearchKeyword");
  const categorySelect = document.getElementById("headerSearchCategory");
  const suggest = document.getElementById("headerSearchSuggest");
  if (!form || !keywordInput || !categorySelect || !suggest) return;

  let timer = null;
  let activeIndex = -1;
  let items = [];
  let controller = null;

  const closeSuggest = () => {
    suggest.classList.add("d-none");
    suggest.innerHTML = "";
    activeIndex = -1;
    items = [];
  };

  const render = (rows) => {
    if (!rows || rows.length === 0) {
      closeSuggest();
      return;
    }

    suggest.innerHTML = "";
    rows.forEach((row, idx) => {
      const link = document.createElement("a");
      link.className = "ttg-suggest-item";
      const slug = row.slug || "san-pham";
      link.href = `/san-pham/${encodeURIComponent(slug)}-${encodeURIComponent(row.componentId)}`;
      link.dataset.idx = String(idx);
      link.innerHTML = `
        <img src="${row.imageUrl}" alt="${row.name}" />
        <div class="ttg-suggest-text">
          <div class="ttg-suggest-name">${row.name}</div>
          <div class="ttg-suggest-price">${Number(row.priceVnd).toLocaleString("vi-VN")} đ</div>
        </div>
      `;
      suggest.appendChild(link);
    });

    suggest.classList.remove("d-none");
    items = Array.from(suggest.querySelectorAll(".ttg-suggest-item"));
  };

  const setActive = (next) => {
    if (items.length === 0) return;
    activeIndex = (next + items.length) % items.length;
    items.forEach((x, i) => x.classList.toggle("active", i === activeIndex));
  };

  const fetchSuggest = async () => {
    const keyword = keywordInput.value.trim();
    if (keyword.length < 2) {
      closeSuggest();
      return;
    }

    if (controller) controller.abort();
    controller = new AbortController();
    const params = new URLSearchParams({
      keyword,
      categoryCode: categorySelect.value || ""
    });

    try {
      const res = await fetch(`/Catalog/Suggest?${params.toString()}`, { signal: controller.signal });
      if (!res.ok) return;
      const data = await res.json();
      render(data);
    } catch (_) {
      // Ignore request abort/network errors for suggest.
    }
  };

  keywordInput.addEventListener("input", () => {
    clearTimeout(timer);
    timer = setTimeout(fetchSuggest, 220);
  });

  keywordInput.addEventListener("keydown", (e) => {
    if (suggest.classList.contains("d-none")) return;
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setActive(activeIndex + 1);
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setActive(activeIndex - 1);
    } else if (e.key === "Enter" && activeIndex >= 0 && items[activeIndex]) {
      e.preventDefault();
      window.location.href = items[activeIndex].href;
    } else if (e.key === "Escape") {
      closeSuggest();
    }
  });

  document.addEventListener("click", (e) => {
    if (!form.contains(e.target)) closeSuggest();
  });

  form.addEventListener("submit", () => {
    closeSuggest();
    const overlay = document.getElementById("taSearchOverlay");
    if (overlay) overlay.classList.remove("open");
  });
})();

(() => {
  const placeholder = "/images/placeholder-product.svg";
  const attachFallback = (img) => {
    if (!img || img.dataset.fallbackBound === "1") return;
    img.dataset.fallbackBound = "1";
    img.addEventListener("error", () => {
      if (img.src.endsWith("placeholder-product.svg")) return;
      img.onerror = null;
      img.src = placeholder;
    }, { once: false });
  };

  document.querySelectorAll("img.ta-product-img:not(.ta-hero-3d-img), .catalog-thumb img, .catalog-detail-image img, .cart-item-thumb img, .build-item-thumb img, .ttg-notify-thumb, .ttg-suggest-item img").forEach(attachFallback);

  const observer = new MutationObserver((mutations) => {
    mutations.forEach((m) => {
      m.addedNodes.forEach((node) => {
        if (!(node instanceof Element)) return;
        if (node.matches?.("img")) attachFallback(node);
        node.querySelectorAll?.("img").forEach(attachFallback);
      });
    });
  });
  observer.observe(document.body, { childList: true, subtree: true });
})();
