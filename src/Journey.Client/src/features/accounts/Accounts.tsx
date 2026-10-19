import { useEffect, useState, type FormEvent } from "react";
import { ArrowRight } from "lucide-react";
import { QRCodeSVG } from "qrcode.react";
import { api } from "../../shared/state";
import { BrandMark } from "../../shared/ui/BrandMark";
import { BlueprintIllustration } from "../journeys/JourneyPreview";
import { LanguagePicker } from "../../shared/i18n/LanguagePicker";
import { t, useLocale } from "../../shared/i18n/i18n";
import "./accounts.css";

export function AccountScreen({
  route,
  done,
}: {
  route: string;
  done: () => void;
}) {
  useLocale();
  const params = new URLSearchParams(route.split("?")[1] || "");
  const initial = route.split("?")[0];
  const [mode, setMode] = useState(
    [
      "register",
      "forgot-password",
      "reset-password",
      "confirm-email",
      "two-factor",
      "resend-confirmation",
    ].includes(initial)
      ? initial
      : "login",
  );
  const [email, setEmail] = useState(params.get("email") || "");
  const [password, setPassword] = useState("");
  const [name, setName] = useState("");
  const [code, setCode] = useState("");
  const [recovery, setRecovery] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(
    params.has("error")
      ? {
          exists:
            "An account already uses that email. Sign in with your original method or reset your password.",
          email:
            "Your provider did not share an email address. Register with email instead.",
          verify:
            "Confirm your email before signing in. You can request another confirmation below.",
        }[params.get("error")!] ||
          "Social sign-in was cancelled or failed. Please try again."
      : "",
  );
  const [message, setMessage] = useState(
    params.has("message")
      ? "Check your email to confirm your account, then sign in with your chosen provider."
      : "",
  );
  const [options, setOptions] = useState<{
    providers: string[];
    demo: boolean;
  }>({ providers: [], demo: false });
  useEffect(() => {
    api("/auth/providers")
      .then(setOptions)
      .catch(() => {});
  }, []);
  const change = (next: string) => {
    setMode(next);
    setError("");
    setMessage("");
    setPassword("");
    setCode("");
  };
  const title = (
    {
      register: "Create your account",
      login: "Welcome to openhow",
      "forgot-password": "Forgot your password?",
      "reset-password": "Choose a new password",
      "confirm-email": "Confirm your email",
      "two-factor": "Two-step verification",
      "resend-confirmation": "Resend confirmation email",
    } as Record<string, string>
  )[mode];
  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError("");
    setMessage("");
    try {
      const body =
        mode === "confirm-email"
          ? { userId: params.get("userId"), code: params.get("code") }
          : mode === "reset-password"
            ? { email, password, code: params.get("code") }
            : mode === "two-factor"
              ? { code, recovery }
              : { email, password, name };
      const result = await api("/auth/" + mode, body);
      if (result?.requiresTwoFactor) {
        setMode("two-factor");
        setPassword("");
        return;
      }
      if (mode === "login" || mode === "two-factor") {
        done();
        return;
      }
      setMessage(result.message);
      setPassword("");
      if (mode === "reset-password" || mode === "confirm-email") {
        // Remove sensitive tokens from browser history after successful use.
        window.history.replaceState(null, "", "/#login");
        setMode("login");
      }
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  const showEmail = !["confirm-email", "two-factor"].includes(mode);
  const showPassword = ["login", "register", "reset-password"].includes(mode);
  return (
    <main className="login">
      <div className="login-story">
        <a href="#" className="login-home-link">
          {t("landing.back")}
        </a>
        <a href="#" className="brand">
          <BrandMark /> openhow
        </a>
        <span className="eyebrow">{t("THE STORY BEHIND THE WORK")}</span>
        <h1>
          {t("Every breakthrough")}
          <br />
          {t("has a journey.")}
        </h1>
        <p>
          {t("Preserve the conversations.")}
          <br />
          {t("Connect the decisions.")}
          <br />
          {t("Share how you got there.")}
        </p>
        <BlueprintIllustration />
      </div>
      <form onSubmit={submit} className="account-form">
        <LanguagePicker />
        <span className="eyebrow">{t("YOUR WORK, IN CONTEXT")}</span>
        <h2>{t(title)}</h2>
        <p>
          {t(
            mode === "register"
              ? "We'll email a confirmation link before you sign in."
              : mode === "two-factor"
                ? "Enter your authenticator code or a saved recovery code."
                : mode === "confirm-email"
                  ? "Confirm this email address to activate your account."
                  : "Sign in to explore your workspace.",
          )}
        </p>
        {error && (
          <div role="alert" className="account-error">
            {t(error)}
          </div>
        )}
        {message && (
          <div role="status" className="account-message">
            {t(message)}
          </div>
        )}
        {mode === "register" && (
          <label>
            {t("Name")}
            <input
              autoComplete="name"
              required
              maxLength={100}
              value={name}
              onChange={(e) => setName(e.target.value)}
            />
          </label>
        )}
        {showEmail && (
          <label>
            {t("Email")}
            <input
              type="email"
              autoComplete="email"
              required
              maxLength={254}
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </label>
        )}
        {showPassword && (
          <label>
            {t("Password")}
            <input
              aria-label={t("Password")}
              type="password"
              autoComplete={
                mode === "login" ? "current-password" : "new-password"
              }
              required
              minLength={mode === "login" ? undefined : 10}
              maxLength={128}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
            {mode !== "login" && (
              <small>
                {t(
                  "Use at least 10 characters with uppercase, lowercase, a number, and a symbol.",
                )}
              </small>
            )}
          </label>
        )}
        {mode === "two-factor" && (
          <>
            <label>
              {t(recovery ? "Recovery code" : "Authenticator code")}
              <input
                autoComplete="one-time-code"
                inputMode={recovery ? "text" : "numeric"}
                required
                value={code}
                onChange={(e) => setCode(e.target.value)}
              />
            </label>
            <button
              type="button"
              className="text-button"
              onClick={() => {
                setRecovery(!recovery);
                setCode("");
              }}
            >
              {t(recovery ? "Use authenticator" : "Use a recovery code")}
            </button>
          </>
        )}
        <button className="primary" disabled={busy}>
          {t(
            busy
              ? "Please wait…"
              : mode === "register"
                ? "Create account"
                : mode === "login"
                  ? "Open workspace"
                  : mode === "two-factor"
                    ? "Verify and sign in"
                    : mode === "confirm-email"
                      ? "Confirm email"
                      : mode === "reset-password"
                        ? "Save password"
                        : "Send email",
          )}{" "}
          <ArrowRight size={16} />
        </button>
        {["login", "register"].includes(mode) &&
          options.providers.length > 0 && (
            <div className="social-signin">
              <span>{t("Or continue with")}</span>
              {options.providers.map((provider) => (
                <a
                  className="social-provider"
                  key={provider}
                  href={
                    "/api/auth/external/" +
                    provider +
                    "?returnTo=" +
                    encodeURIComponent(params.get("returnTo") || "dashboard")
                  }
                >
                  {provider}
                </a>
              ))}
            </div>
          )}
        <div className="account-links">
          <button
            type="button"
            className="text-button"
            onClick={() => change(mode === "login" ? "register" : "login")}
          >
            {t(
              mode === "login" ? "Sign up" : "Already have an account? Sign in",
            )}
          </button>
          {mode === "login" && (
            <button
              type="button"
              className="text-button"
              onClick={() => change("forgot-password")}
            >
              {t("Forgot your password?")}
            </button>
          )}
          {["login", "register"].includes(mode) && (
            <button
              type="button"
              className="text-button"
              onClick={() => change("resend-confirmation")}
            >
              {t("Resend confirmation email")}
            </button>
          )}
        </div>
        {options.demo && mode === "login" && (
          <div className="demo-note">
            <strong>{t("Explore the demonstration")}</strong>
            <p>{t("Alex is an owner. Sam is an editor. Riley is a viewer.")}</p>
            <div className="row">
              {["alex", "sam", "riley"].map((n) => (
                <button
                  type="button"
                  key={n}
                  onClick={() => {
                    setEmail(n + "@journey.local");
                    setPassword("JourneyDemo!2026");
                  }}
                >
                  {n}
                </button>
              ))}
            </div>
            <small>
              {t(
                "All demo accounts use JourneyDemo!2026. Local prototype only.",
              )}
            </small>
          </div>
        )}
      </form>
    </main>
  );
}

export function AccountSecurity() {
  const [state, setState] = useState<{
    enabled: boolean;
    recoveryCodesLeft: number;
    hasPassword: boolean;
  }>();
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [key, setKey] = useState("");
  const [authenticatorUri, setAuthenticatorUri] = useState("");
  const [codes, setCodes] = useState<string[]>([]);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const refresh = () => api("/auth/mfa").then(setState);
  useEffect(() => {
    refresh().catch((e) => setError(e.message));
  }, []);
  async function perform(operation: string) {
    setBusy(true);
    setError("");
    setMessage("");
    try {
      const result = await api("/auth/mfa/" + operation, { password, code });
      if (operation === "setup") {
        setKey(result.sharedKey);
        setAuthenticatorUri(result.authenticatorUri);
      } else {
        setKey("");
        setAuthenticatorUri("");
        setCodes(result.recoveryCodes || []);
        setPassword("");
        setCode("");
        setMessage(
          operation === "disable"
            ? "Two-step verification disabled."
            : "Two-step verification is enabled. Keep your recovery codes somewhere safe.",
        );
      }
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <section className="panel settings account-security">
      <h2>{t("Account security")}</h2>
      <p>
        {t(
          "Protect your account with an authenticator app and one-time recovery codes.",
        )}
      </p>
      {error && (
        <p className="account-error" role="alert">
          {t(error)}
        </p>
      )}
      {message && (
        <p className="account-message" role="status">
          {t(message)}
        </p>
      )}
      {state && (
        <>
          <strong>
            {t(
              state.enabled
                ? "Two-step verification is on"
                : "Two-step verification is off",
            )}
          </strong>
          {state.enabled && (
            <p>
              {t("Recovery codes remaining")}: {state.recoveryCodesLeft}
            </p>
          )}
          {!state.hasPassword ? (
            <p>
              {t(
                "To manage MFA, first set an account password using Forgot your password on the sign-in screen. Your social login will continue to work.",
              )}
            </p>
          ) : (
            <form
              onSubmit={(e) => {
                e.preventDefault();
                void perform(
                  key ? "enable" : state.enabled ? "recovery-codes" : "setup",
                );
              }}
            >
              <label>
                {t("Current password")}
                <input
                  required
                  type="password"
                  autoComplete="current-password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                />
              </label>
              {key && (
                <div className="authenticator-setup">
                  {authenticatorUri && (
                    <>
                      <p>
                        {t("Scan this QR code with your authenticator app.")}
                      </p>
                      <QRCodeSVG
                        className="authenticator-qr"
                        value={authenticatorUri}
                        size={240}
                        marginSize={4}
                        level="M"
                        bgColor="#ffffff"
                        fgColor="#000000"
                        role="img"
                        title={t("Authenticator setup QR code")}
                      />
                    </>
                  )}
                  <p>
                    {t(
                      "In your authenticator app, add an account using a setup key. Choose time-based codes and enter this key:",
                    )}
                  </p>
                  <code>{key}</code>
                  <p>
                    {t("Then enter the six-digit code to finish enabling MFA.")}
                  </p>
                </div>
              )}
              {(key || state.enabled) && (
                <label>
                  {t("Authenticator code")}
                  <input
                    required
                    autoComplete="one-time-code"
                    inputMode="numeric"
                    value={code}
                    onChange={(e) => setCode(e.target.value)}
                  />
                </label>
              )}
              <div className="row">
                <button className="primary" disabled={busy}>
                  {t(
                    key
                      ? "Enable MFA"
                      : state.enabled
                        ? "Replace recovery codes"
                        : "Set up authenticator",
                  )}
                </button>
                {state.enabled && (
                  <button
                    type="button"
                    disabled={busy || !password || !code}
                    onClick={() => perform("disable")}
                  >
                    {t("Disable MFA")}
                  </button>
                )}
              </div>
            </form>
          )}
        </>
      )}
      {codes.length > 0 && (
        <div className="recovery-codes">
          <h3>{t("Save these recovery codes")}</h3>
          <p>
            {t(
              "Each code works once. These codes replace any previous set and won't be shown again.",
            )}
          </p>
          <pre>{codes.join("\n")}</pre>
          <button onClick={() => setCodes([])}>
            {t("I've saved my codes")}
          </button>
        </div>
      )}
    </section>
  );
}
