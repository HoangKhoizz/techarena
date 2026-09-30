(() => {
  const toggle = document.getElementById("taChatToggle");
  const panel = document.getElementById("taChatPanel");
  const closeBtn = document.getElementById("taChatClose");
  const form = document.getElementById("taChatForm");
  const input = document.getElementById("taChatInput");
  const messages = document.getElementById("taChatMessages");
  if (!toggle || !panel || !form || !input || !messages) return;

  let sending = false;

  const scrollToBottom = () => {
    messages.scrollTop = messages.scrollHeight;
  };

  const addMessage = (text, who) => {
    const row = document.createElement("div");
    row.className = `ta-chat-msg ${who}`;
    const bubble = document.createElement("div");
    bubble.className = "ta-chat-bubble";
    bubble.textContent = text;
    row.appendChild(bubble);
    messages.appendChild(row);
    scrollToBottom();
    return bubble;
  };

  const parseSseChunk = (raw) => {
    const events = [];
    const lines = raw.split("\n");
    for (const line of lines) {
      if (!line.startsWith("data: ")) continue;
      const data = line.slice(6).trim();
      if (!data || data === "[DONE]") continue;
      try {
        events.push(JSON.parse(data));
      } catch (error) {
        console.error("[Chat] SSE parse error:", error, data);
      }
    }
    return events;
  };

  const streamChatReply = async (message, bubble, row) => {
    const res = await fetch("/api/chat", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Accept: "text/event-stream"
      },
      body: JSON.stringify({ message })
    });

    if (!res.ok) {
      const raw = await res.text();
      console.error("[Chat] API /api/chat tra ve HTTP", res.status, raw);
      bubble.textContent = "Không kết nối được tới trợ lý AI. Bạn thử lại sau nhé!";
      return;
    }

    if (!res.body) {
      bubble.textContent = "Trình duyệt không hỗ trợ streaming response.";
      return;
    }

    bubble.textContent = "";
    row.classList.remove("typing");

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";
    let hasText = false;

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const parts = buffer.split("\n\n");
      buffer = parts.pop() || "";

      for (const part of parts) {
        const events = parseSseChunk(part);
        for (const event of events) {
          if (event.error) {
            console.error("[Chat] SSE error:", event.error);
            if (!hasText) bubble.textContent = event.error;
            continue;
          }
          if (event.text) {
            hasText = true;
            bubble.textContent += event.text;
            scrollToBottom();
          }
        }
      }
    }

    if (!hasText && !bubble.textContent) {
      bubble.textContent = "Xin lỗi, mình chưa trả lời được câu này.";
    }
  };

  toggle.addEventListener("click", () => {
    panel.classList.toggle("open");
    if (panel.classList.contains("open")) setTimeout(() => input.focus(), 100);
  });
  if (closeBtn) closeBtn.addEventListener("click", () => panel.classList.remove("open"));

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const text = input.value.trim();
    if (!text || sending) return;

    addMessage(text, "user");
    input.value = "";
    sending = true;

    const typingBubble = addMessage("Đang trả lời...", "bot typing");
    const typingRow = typingBubble.parentElement;

    try {
      await streamChatReply(text, typingBubble, typingRow);
    } catch (error) {
      console.error("[Chat] Loi khi goi /api/chat:", error);
      typingBubble.textContent = "Không kết nối được tới trợ lý AI. Bạn thử lại sau nhé!";
    } finally {
      typingRow.classList.remove("typing");
      sending = false;
      scrollToBottom();
    }
  });
})();
