const year = document.getElementById("year");
if (year) year.textContent = new Date().getFullYear();

// The bar starts transparent over the gradient and settles into a frosted strip once the page moves.
const topbar = document.querySelector("[data-topbar]");
if (topbar) {
  const sync = () => topbar.classList.toggle("is-scrolled", window.scrollY > 8);
  sync();
  window.addEventListener("scroll", sync, { passive: true });
}

const video = document.querySelector(".film-frame video");
if (video) {
  // A missing file leaves the cover in place instead of an empty black frame.
  video.addEventListener("error", () => video.closest(".film-frame")?.classList.add("missing"));

  // The clip is decorative: people who asked for less motion keep the still cover.
  if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
    video.removeAttribute("autoplay");
    video.autoplay = false;
    video.pause();
  }
}
