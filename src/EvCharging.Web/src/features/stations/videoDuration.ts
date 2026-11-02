// A file the browser cannot make sense of may never report, so the wait is short. The server checks the file
// itself either way.
const probeTimeoutMs = 2_000;

/**
 * The length of a video file in seconds as this browser reads it, or null when the browser cannot read the file in
 * time. This lets the operator hear about a clip that is too long before sending it.
 */
export function readVideoDuration(file: File): Promise<number | null> {
  return new Promise((resolve) => {
    const url = URL.createObjectURL(file);
    const video = document.createElement("video");
    const finish = (seconds: number | null) => {
      clearTimeout(timer);
      video.removeAttribute("src");
      video.load();
      URL.revokeObjectURL(url);
      resolve(seconds);
    };
    const timer = setTimeout(() => finish(null), probeTimeoutMs);
    video.preload = "metadata";
    video.onloadedmetadata = () =>
      finish(Number.isFinite(video.duration) ? video.duration : null);
    video.onerror = () => finish(null);
    video.src = url;
  });
}
