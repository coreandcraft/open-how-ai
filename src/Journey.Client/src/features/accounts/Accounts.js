import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "react/jsx-runtime";
import { useEffect, useState } from "react";
import { ArrowRight } from "lucide-react";
import { QRCodeSVG } from "qrcode.react";
import { api } from "../../shared/state";
import { BrandMark } from "../../shared/ui/BrandMark";
import { BlueprintIllustration } from "../journeys/JourneyPreview";
import { LanguagePicker } from "../../shared/i18n/LanguagePicker";
import { t, useLocale } from "../../shared/i18n/i18n";
import "./accounts.css";
export function AccountScreen({ route, done, }) {
    useLocale();
    const params = new URLSearchParams(route.split("?")[1] || "");
    const initial = route.split("?")[0];
    const [mode, setMode] = useState([
        "register",
        "forgot-password",
        "reset-password",
        "confirm-email",
        "two-factor",
        "resend-confirmation",
    ].includes(initial)
        ? initial
        : "login");
    const [email, setEmail] = useState(params.get("email") || "");
    const [password, setPassword] = useState("");
    const [name, setName] = useState("");
    const [code, setCode] = useState("");
    const [recovery, setRecovery] = useState(false);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(params.has("error")
        ? {
            exists: "An account already uses that email. Sign in with your original method or reset your password.",
            email: "Your provider did not share an email address. Register with email instead.",
            verify: "Confirm your email before signing in. You can request another confirmation below.",
        }[params.get("error")] ||
            "Social sign-in was cancelled or failed. Please try again."
        : "");
    const [message, setMessage] = useState(params.has("message")
        ? "Check your email to confirm your account, then sign in with your chosen provider."
        : "");
    const [options, setOptions] = useState({ providers: [], demo: false });
    useEffect(() => {
        api("/auth/providers")
            .then(setOptions)
            .catch(() => { });
    }, []);
    const change = (next) => {
        setMode(next);
        setError("");
        setMessage("");
        setPassword("");
        setCode("");
    };
    const title = {
        register: "Create your account",
        login: "Welcome to openhow",
        "forgot-password": "Forgot your password?",
        "reset-password": "Choose a new password",
        "confirm-email": "Confirm your email",
        "two-factor": "Two-step verification",
        "resend-confirmation": "Resend confirmation email",
    }[mode];
    async function submit(event) {
        event.preventDefault();
        setBusy(true);
        setError("");
        setMessage("");
        try {
            const body = mode === "confirm-email"
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
        }
        catch (e) {
            setError(e.message);
        }
        finally {
            setBusy(false);
        }
    }
    const showEmail = !["confirm-email", "two-factor"].includes(mode);
    const showPassword = ["login", "register", "reset-password"].includes(mode);
    return (_jsxs("main", { className: "login", children: [_jsxs("div", { className: "login-story", children: [_jsx("a", { href: "#", className: "login-home-link", children: t("landing.back") }), _jsxs("a", { href: "#", className: "brand", children: [_jsx(BrandMark, {}), " openhow"] }), _jsx("span", { className: "eyebrow", children: t("THE STORY BEHIND THE WORK") }), _jsxs("h1", { children: [t("Every breakthrough"), _jsx("br", {}), t("has a journey.")] }), _jsxs("p", { children: [t("Preserve the conversations."), _jsx("br", {}), t("Connect the decisions."), _jsx("br", {}), t("Share how you got there.")] }), _jsx(BlueprintIllustration, {})] }), _jsxs("form", { onSubmit: submit, className: "account-form", children: [_jsx(LanguagePicker, {}), _jsx("span", { className: "eyebrow", children: t("YOUR WORK, IN CONTEXT") }), _jsx("h2", { children: t(title) }), _jsx("p", { children: t(mode === "register"
                            ? "We'll email a confirmation link before you sign in."
                            : mode === "two-factor"
                                ? "Enter your authenticator code or a saved recovery code."
                                : mode === "confirm-email"
                                    ? "Confirm this email address to activate your account."
                                    : "Sign in to explore your workspace.") }), error && (_jsx("div", { role: "alert", className: "account-error", children: t(error) })), message && (_jsx("div", { role: "status", className: "account-message", children: t(message) })), mode === "register" && (_jsxs("label", { children: [t("Name"), _jsx("input", { autoComplete: "name", required: true, maxLength: 100, value: name, onChange: (e) => setName(e.target.value) })] })), showEmail && (_jsxs("label", { children: [t("Email"), _jsx("input", { type: "email", autoComplete: "email", required: true, maxLength: 254, value: email, onChange: (e) => setEmail(e.target.value) })] })), showPassword && (_jsxs("label", { children: [t("Password"), _jsx("input", { "aria-label": t("Password"), type: "password", autoComplete: mode === "login" ? "current-password" : "new-password", required: true, minLength: mode === "login" ? undefined : 10, maxLength: 128, value: password, onChange: (e) => setPassword(e.target.value) }), mode !== "login" && (_jsx("small", { children: t("Use at least 10 characters with uppercase, lowercase, a number, and a symbol.") }))] })), mode === "two-factor" && (_jsxs(_Fragment, { children: [_jsxs("label", { children: [t(recovery ? "Recovery code" : "Authenticator code"), _jsx("input", { autoComplete: "one-time-code", inputMode: recovery ? "text" : "numeric", required: true, value: code, onChange: (e) => setCode(e.target.value) })] }), _jsx("button", { type: "button", className: "text-button", onClick: () => {
                                    setRecovery(!recovery);
                                    setCode("");
                                }, children: t(recovery ? "Use authenticator" : "Use a recovery code") })] })), _jsxs("button", { className: "primary", disabled: busy, children: [t(busy
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
                                                    : "Send email"), " ", _jsx(ArrowRight, { size: 16 })] }), ["login", "register"].includes(mode) &&
                        options.providers.length > 0 && (_jsxs("div", { className: "social-signin", children: [_jsx("span", { children: t("Or continue with") }), options.providers.map((provider) => (_jsx("a", { className: "social-provider", href: "/api/auth/external/" +
                                    provider +
                                    "?returnTo=" +
                                    encodeURIComponent(params.get("returnTo") || "dashboard"), children: provider }, provider)))] })), _jsxs("div", { className: "account-links", children: [_jsx("button", { type: "button", className: "text-button", onClick: () => change(mode === "login" ? "register" : "login"), children: t(mode === "login" ? "Sign up" : "Already have an account? Sign in") }), mode === "login" && (_jsx("button", { type: "button", className: "text-button", onClick: () => change("forgot-password"), children: t("Forgot your password?") })), ["login", "register"].includes(mode) && (_jsx("button", { type: "button", className: "text-button", onClick: () => change("resend-confirmation"), children: t("Resend confirmation email") }))] }), options.demo && mode === "login" && (_jsxs("div", { className: "demo-note", children: [_jsx("strong", { children: t("Explore the demonstration") }), _jsx("p", { children: t("Alex is an owner. Sam is an editor. Riley is a viewer.") }), _jsx("div", { className: "row", children: ["alex", "sam", "riley"].map((n) => (_jsx("button", { type: "button", onClick: () => {
                                        setEmail(n + "@journey.local");
                                        setPassword("JourneyDemo!2026");
                                    }, children: n }, n))) }), _jsx("small", { children: t("All demo accounts use JourneyDemo!2026. Local prototype only.") })] }))] })] }));
}
export function AccountSecurity() {
    const [state, setState] = useState();
    const [password, setPassword] = useState("");
    const [code, setCode] = useState("");
    const [key, setKey] = useState("");
    const [authenticatorUri, setAuthenticatorUri] = useState("");
    const [codes, setCodes] = useState([]);
    const [error, setError] = useState("");
    const [message, setMessage] = useState("");
    const [busy, setBusy] = useState(false);
    const refresh = () => api("/auth/mfa").then(setState);
    useEffect(() => {
        refresh().catch((e) => setError(e.message));
    }, []);
    async function perform(operation) {
        setBusy(true);
        setError("");
        setMessage("");
        try {
            const result = await api("/auth/mfa/" + operation, { password, code });
            if (operation === "setup") {
                setKey(result.sharedKey);
                setAuthenticatorUri(result.authenticatorUri);
            }
            else {
                setKey("");
                setAuthenticatorUri("");
                setCodes(result.recoveryCodes || []);
                setPassword("");
                setCode("");
                setMessage(operation === "disable"
                    ? "Two-step verification disabled."
                    : "Two-step verification is enabled. Keep your recovery codes somewhere safe.");
            }
            await refresh();
        }
        catch (e) {
            setError(e.message);
        }
        finally {
            setBusy(false);
        }
    }
    return (_jsxs("section", { className: "panel settings account-security", children: [_jsx("h2", { children: t("Account security") }), _jsx("p", { children: t("Protect your account with an authenticator app and one-time recovery codes.") }), error && (_jsx("p", { className: "account-error", role: "alert", children: t(error) })), message && (_jsx("p", { className: "account-message", role: "status", children: t(message) })), state && (_jsxs(_Fragment, { children: [_jsx("strong", { children: t(state.enabled
                            ? "Two-step verification is on"
                            : "Two-step verification is off") }), state.enabled && (_jsxs("p", { children: [t("Recovery codes remaining"), ": ", state.recoveryCodesLeft] })), !state.hasPassword ? (_jsx("p", { children: t("To manage MFA, first set an account password using Forgot your password on the sign-in screen. Your social login will continue to work.") })) : (_jsxs("form", { onSubmit: (e) => {
                            e.preventDefault();
                            void perform(key ? "enable" : state.enabled ? "recovery-codes" : "setup");
                        }, children: [_jsxs("label", { children: [t("Current password"), _jsx("input", { required: true, type: "password", autoComplete: "current-password", value: password, onChange: (e) => setPassword(e.target.value) })] }), key && (_jsxs("div", { className: "authenticator-setup", children: [authenticatorUri && (_jsxs(_Fragment, { children: [_jsx("p", { children: t("Scan this QR code with your authenticator app.") }), _jsx(QRCodeSVG, { className: "authenticator-qr", value: authenticatorUri, size: 240, marginSize: 4, level: "M", bgColor: "#ffffff", fgColor: "#000000", role: "img", title: t("Authenticator setup QR code") })] })), _jsx("p", { children: t("In your authenticator app, add an account using a setup key. Choose time-based codes and enter this key:") }), _jsx("code", { children: key }), _jsx("p", { children: t("Then enter the six-digit code to finish enabling MFA.") })] })), (key || state.enabled) && (_jsxs("label", { children: [t("Authenticator code"), _jsx("input", { required: true, autoComplete: "one-time-code", inputMode: "numeric", value: code, onChange: (e) => setCode(e.target.value) })] })), _jsxs("div", { className: "row", children: [_jsx("button", { className: "primary", disabled: busy, children: t(key
                                            ? "Enable MFA"
                                            : state.enabled
                                                ? "Replace recovery codes"
                                                : "Set up authenticator") }), state.enabled && (_jsx("button", { type: "button", disabled: busy || !password || !code, onClick: () => perform("disable"), children: t("Disable MFA") }))] })] }))] })), codes.length > 0 && (_jsxs("div", { className: "recovery-codes", children: [_jsx("h3", { children: t("Save these recovery codes") }), _jsx("p", { children: t("Each code works once. These codes replace any previous set and won't be shown again.") }), _jsx("pre", { children: codes.join("\n") }), _jsx("button", { onClick: () => setCodes([]), children: t("I've saved my codes") })] }))] }));
}
