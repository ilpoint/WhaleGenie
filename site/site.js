const year = document.getElementById("year");
if (year) year.textContent = new Date().getFullYear();

const video = document.querySelector(".video-frame video");
if (video) {
  video.addEventListener("error", () => {
    video.closest(".video-frame")?.classList.add("missing");
  });
}
