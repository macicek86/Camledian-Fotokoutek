// Progressive enhancement for /admin/* pages: destructive-action confirmations + the gallery
// lightbox. Every form/link this touches already works without JS (plain POSTs / <a href>), so if
// this script fails to load the admin console degrades gracefully rather than breaking.
(function () {
  "use strict";

  document.addEventListener("submit", function (event) {
    var form = event.target;
    if (form instanceof HTMLFormElement && form.hasAttribute("data-confirm")) {
      if (!window.confirm(form.getAttribute("data-confirm"))) {
        event.preventDefault();
      }
    }
  });

  var grid = document.querySelector(".photo-grid");
  if (!grid) return;

  var cards = Array.prototype.slice.call(grid.querySelectorAll(".photo-card"));
  if (!cards.length) return;

  var lightbox = document.createElement("div");
  lightbox.className = "lightbox";
  lightbox.innerHTML =
    '<button type="button" class="lb-close" aria-label="Zavřít">&times;</button>' +
    '<img alt="">' +
    '<div class="lb-caption"><div class="name"></div><div class="when"></div></div>' +
    '<div class="lb-controls">' +
    '<button type="button" class="lb-nav lb-prev" aria-label="Předchozí fotka">&larr;</button>' +
    '<a class="btn small lb-open" target="_blank" rel="noopener">Otevřít původní</a>' +
    '<button type="button" class="btn small danger lb-delete">Smazat</button>' +
    '<button type="button" class="lb-nav lb-next" aria-label="Další fotka">&rarr;</button>' +
    "</div>";
  document.body.appendChild(lightbox);

  var img = lightbox.querySelector("img");
  var nameEl = lightbox.querySelector(".name");
  var whenEl = lightbox.querySelector(".when");
  var openLink = lightbox.querySelector(".lb-open");
  var deleteBtn = lightbox.querySelector(".lb-delete");
  var current = 0;

  function render(i) {
    current = (i + cards.length) % cards.length;
    var card = cards[current];
    img.src = card.dataset.full || "";
    nameEl.textContent = card.dataset.name || "";
    whenEl.textContent = card.dataset.when || "";
    openLink.href = card.dataset.public || "#";
    deleteBtn.style.display = card.dataset.deleteUrl ? "" : "none";
  }

  function open(i) {
    render(i);
    lightbox.classList.add("open");
  }

  function close() {
    lightbox.classList.remove("open");
  }

  cards.forEach(function (card, i) {
    card.addEventListener("click", function (event) {
      // Delete button lives inside the card but outside the .thumb link — let its own click
      // (and confirm()) proceed instead of hijacking it into a lightbox-open.
      if (event.target.closest(".card-actions")) return;
      event.preventDefault();
      open(i);
    });
  });

  lightbox.querySelector(".lb-close").addEventListener("click", close);
  lightbox.addEventListener("click", function (event) {
    if (event.target === lightbox) close();
  });
  lightbox.querySelector(".lb-prev").addEventListener("click", function () {
    render(current - 1);
  });
  lightbox.querySelector(".lb-next").addEventListener("click", function () {
    render(current + 1);
  });
  deleteBtn.addEventListener("click", function () {
    var url = cards[current].dataset.deleteUrl;
    if (!url) return;
    if (!window.confirm("Opravdu trvale smazat tuto fotografii?")) return;
    var form = document.createElement("form");
    form.method = "post";
    form.action = url;
    document.body.appendChild(form);
    form.submit();
  });

  document.addEventListener("keydown", function (event) {
    if (!lightbox.classList.contains("open")) return;
    if (event.key === "Escape") close();
    if (event.key === "ArrowLeft") render(current - 1);
    if (event.key === "ArrowRight") render(current + 1);
  });
})();
