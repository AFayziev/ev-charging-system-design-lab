export function AppLoading() {
  return (
    <div className="app-loading" role="status">
      <img
        className="app-loading-icon"
        src="/favicon.svg"
        alt=""
        width={64}
        height={64}
      />
      <span className="app-loading-title" aria-hidden="true">
        EV Charging
      </span>
      <span className="app-loading-message">Loading application…</span>
    </div>
  );
}
