import { jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
import { BrandMark } from "../../shared/ui/BrandMark";
import { useEffect, useState } from "react";
import { ArrowRight, GitBranch, FileText, Lightbulb, Rocket, Network, Share2, Library, Search, Settings, ChevronDown, MessageSquare, Quote, ArrowUpRight, Check, Menu, X, } from "lucide-react";
import { t, useLocale } from "../../shared/i18n/i18n";
import { LanguagePicker } from "../../shared/i18n/LanguagePicker";
import "./landing.css";
const stages = [
    {
        title: "landing.idea",
        source: "Markdown",
        icon: Lightbulb,
        detail: "landing.ideaDetail",
    },
    {
        title: "landing.architecture",
        source: "ChatGPT",
        icon: GitBranch,
        detail: "landing.architectureDetail",
    },
    {
        title: "landing.alternative",
        source: "Claude",
        icon: Network,
        detail: "landing.alternativeDetail",
    },
    {
        title: "landing.decision",
        source: "landing.curator",
        icon: FileText,
        detail: "landing.decisionDetail",
    },
    {
        title: "landing.shipped",
        source: "landing.milestone",
        icon: Rocket,
        detail: "landing.shippedDetail",
    },
];
function JourneyDemo() {
    const [selected, setSelected] = useState(3);
    const stage = stages[selected];
    return (_jsxs("div", { className: "oh-preview", children: [_jsxs("aside", { className: "oh-preview-sidebar", "aria-hidden": "true", children: [_jsxs("div", { className: "oh-mini-brand", children: [_jsx(BrandMark, { size: 20 }), " openhow"] }), _jsxs("div", { className: "oh-preview-nav active", children: [_jsx(Library, { size: 16 }), t("Projects")] }), _jsxs("div", { className: "oh-preview-nav", children: [_jsx(Search, { size: 16 }), t("Search")] }), _jsxs("div", { className: "oh-preview-nav", children: [_jsx(Share2, { size: 16 }), t("landing.shared")] }), _jsxs("div", { className: "oh-sidebar-note", children: [_jsx(GitBranch, { size: 24 }), _jsx("strong", { children: t("landing.keepThinking") }), _jsx("span", { children: t("landing.contextMatters") })] }), _jsxs("div", { className: "oh-preview-nav", children: [_jsx(Settings, { size: 16 }), t("Settings")] })] }), _jsxs("div", { className: "oh-preview-main", children: [_jsxs("div", { className: "oh-preview-header", children: [_jsx("strong", { children: t("landing.cloudplay") }), _jsx(ChevronDown, { size: 14 }), _jsx("span", { className: "oh-demo-label", children: t("landing.example") }), _jsxs("a", { href: "#public/cloudplay-demo", children: [t("landing.openDemo"), _jsx(ArrowUpRight, { size: 14 })] })] }), _jsxs("div", { className: "oh-preview-tabs", children: [_jsxs("span", { children: [_jsx(GitBranch, { size: 14 }), t("landing.journey")] }), _jsx("small", { children: t("landing.selectNode") })] }), _jsxs("div", { className: "oh-preview-body", children: [_jsxs("div", { className: "oh-graph", role: "group", "aria-label": t("landing.previewLabel"), children: [_jsxs("svg", { className: "oh-connections", viewBox: "0 0 700 380", preserveAspectRatio: "none", "aria-hidden": "true", children: [_jsx("path", { d: "M140 190 C170 190 160 95 210 95 M140 190 C170 190 160 285 210 285 M340 95 C390 95 365 190 410 190 M340 285 C390 285 365 190 410 190 M535 190 L565 190" }), _jsx("circle", { cx: "140", cy: "190", r: "4" }), _jsx("circle", { cx: "410", cy: "190", r: "4" })] }), stages.map((item, index) => (_jsxs("button", { type: "button", className: `oh-node oh-node-${index} ${selected === index ? "is-selected" : ""}`, "aria-pressed": selected === index, onClick: () => setSelected(index), children: [_jsx(item.icon, { size: 22 }), _jsx("strong", { children: t(item.title) }), _jsx("span", { children: t(item.source) }), _jsxs("div", { className: "oh-node-lines", "aria-hidden": "true", children: [_jsx("i", {}), _jsx("i", {}), _jsx("i", {})] })] }, item.title)))] }), _jsxs("aside", { className: "oh-evidence", "aria-live": "polite", "aria-atomic": "true", children: [_jsxs("div", { className: "oh-evidence-kicker", children: [_jsx(FileText, { size: 16 }), t("landing.behindDecision")] }), _jsx("h3", { children: t(stage.title) }), _jsx("span", { className: "oh-evidence-source", children: t(stage.source) }), _jsxs("blockquote", { children: [_jsx(Quote, { size: 18 }), _jsx("p", { children: t(stage.detail) })] }), _jsxs("div", { className: "oh-annotation", children: [_jsx(MessageSquare, { size: 15 }), _jsx("strong", { children: t("landing.context") }), _jsx("p", { children: t("landing.exampleNote") })] })] })] })] })] }));
}
export function LandingPage() {
    useLocale();
    const [menuOpen, setMenuOpen] = useState(false);
    const [projectLinks, setProjectLinks] = useState({});
    useEffect(() => {
        const controller = new AbortController();
        fetch("/api/site/links", { signal: controller.signal })
            .then((r) => (r.ok ? r.json() : {}))
            .then((links) => {
            if (!controller.signal.aborted)
                setProjectLinks(links);
        })
            .catch(() => { });
        return () => controller.abort();
    }, []);
    const howItWorks = () => {
        setMenuOpen(false);
        document.getElementById("how-it-works")?.scrollIntoView({
            behavior: matchMedia("(prefers-reduced-motion: reduce)").matches
                ? "instant"
                : "smooth",
        });
    };
    return (_jsxs("div", { className: "oh-landing", children: [_jsx("a", { className: "oh-skip", href: "#landing-content", onClick: (e) => {
                    e.preventDefault();
                    document.getElementById("landing-content")?.focus();
                }, children: t("landing.skip") }), _jsxs("header", { className: "oh-header", children: [_jsxs("a", { href: "#", className: "oh-brand", "aria-label": "openhow", children: [_jsx(BrandMark, { size: 29 }), _jsx("span", { children: "openhow" })] }), _jsxs("nav", { className: "oh-desktop-nav", "aria-label": t("landing.navigation"), children: [_jsx("button", { onClick: howItWorks, children: t("landing.how") }), _jsx("a", { href: "#public/cloudplay-demo", children: t("landing.exploreJourney") })] }), _jsxs("div", { className: "oh-header-actions", children: [_jsx("a", { className: "oh-sign-in", href: "#login", children: t("Sign in") }), _jsx("a", { className: "oh-button oh-primary oh-header-cta", href: "#register", children: t("landing.start") }), _jsx("button", { className: "oh-menu-toggle", "aria-expanded": menuOpen, "aria-controls": "oh-mobile-nav", "aria-label": t(menuOpen ? "Close menu" : "Navigation menu"), onClick: () => setMenuOpen(!menuOpen), children: menuOpen ? _jsx(X, {}) : _jsx(Menu, {}) })] }), menuOpen && (_jsxs("nav", { id: "oh-mobile-nav", className: "oh-mobile-nav", "aria-label": t("landing.navigation"), children: [_jsx("button", { onClick: howItWorks, children: t("landing.how") }), _jsx("a", { href: "#public/cloudplay-demo", children: t("landing.exploreJourney") }), _jsx("a", { href: "#register", children: t("landing.start") })] }))] }), _jsxs("main", { id: "landing-content", tabIndex: -1, children: [_jsxs("section", { className: "oh-hero", "aria-labelledby": "oh-title", children: [_jsx("p", { className: "oh-eyebrow", children: t("landing.eyebrow") }), _jsxs("h1", { id: "oh-title", children: [t("landing.headline"), _jsx("br", {}), _jsx("span", { children: t("landing.headlineEnd") })] }), _jsx("p", { className: "oh-hero-copy", children: t("landing.description") }), _jsxs("div", { className: "oh-hero-actions", children: [_jsxs("a", { href: "#register", className: "oh-button oh-primary", children: [t("landing.start"), _jsx(ArrowRight, { size: 17 })] }), _jsxs("a", { href: "#public/cloudplay-demo", className: "oh-button oh-secondary", children: [t("landing.exploreDemo"), _jsx(ArrowRight, { size: 17 })] })] }), _jsx("p", { className: "oh-reassurance", children: t("landing.reassurance") })] }), _jsxs("section", { className: "oh-showcase", "aria-label": t("landing.previewLabel"), children: [_jsx(JourneyDemo, {}), _jsxs("div", { className: "oh-imports", children: [_jsx("p", { children: t("landing.imports") }), _jsxs("div", { children: [_jsxs("span", { children: [_jsx(Network, {}), "ChatGPT"] }), _jsxs("span", { children: [_jsx("span", { className: "oh-claude", "aria-hidden": "true", children: "\u2733" }), "Claude"] }), _jsxs("span", { children: [_jsx(FileText, {}), "JSON"] }), _jsxs("span", { children: [_jsx(FileText, {}), "Markdown"] })] })] })] }), _jsxs("section", { className: "oh-how", id: "how-it-works", "aria-labelledby": "oh-how-title", children: [_jsx("p", { className: "oh-eyebrow", children: t("landing.how") }), _jsxs("h2", { id: "oh-how-title", children: [t("landing.keepThinking"), _jsx("br", {}), t("landing.wholeJourney")] }), _jsxs("div", { className: "oh-features", children: [_jsxs("article", { children: [_jsx("span", { className: "oh-feature-icon", children: _jsx(FileText, {}) }), _jsx("h3", { children: t("landing.preserve") }), _jsx("p", { children: t("landing.preserveBody") }), _jsx("div", { className: "oh-source-list", "aria-hidden": "true", children: ["ChatGPT", "Claude", "JSON", "Markdown"].map((source, i) => (_jsxs("div", { children: [_jsx(FileText, { size: 16 }), _jsx("span", { children: source }), _jsx("i", { style: { width: `${65 - i * 9}%` } })] }, source))) })] }), _jsxs("article", { children: [_jsx("span", { className: "oh-feature-icon", children: _jsx(Network, {}) }), _jsx("h3", { children: t("landing.connect") }), _jsx("p", { children: t("landing.connectBody") }), _jsxs("div", { className: "oh-mini-graph", "aria-hidden": "true", children: [_jsx("span", { children: _jsx(MessageSquare, {}) }), _jsx("i", {}), _jsx("span", { children: _jsx(GitBranch, {}) }), _jsx("i", {}), _jsx("span", { children: _jsx(Check, {}) })] })] }), _jsxs("article", { children: [_jsx("span", { className: "oh-feature-icon", children: _jsx(Share2, {}) }), _jsx("h3", { children: t("landing.share") }), _jsx("p", { children: t("landing.shareBody") }), _jsxs("div", { className: "oh-share-example", children: [_jsxs("div", { children: [_jsx(Check, { size: 16 }), t("landing.selectedMessages")] }), _jsxs("div", { children: [_jsx(Check, { size: 16 }), t("landing.reviewRedactions")] }), _jsxs("a", { href: "#public/cloudplay-demo", children: [t("landing.viewExample"), _jsx(ArrowUpRight, { size: 16 })] })] })] })] })] }), (projectLinks.journeyUrl || projectLinks.repositoryUrl) && (_jsxs("section", { className: "oh-project-links", "aria-labelledby": "oh-project-title", children: [_jsxs("div", { children: [_jsx("p", { className: "oh-eyebrow", children: t("landing.builtInOpen") }), _jsx("h2", { id: "oh-project-title", children: t("landing.buildingOpenhow") }), _jsx("p", { children: t("landing.behindOpenhow") })] }), _jsxs("div", { className: "oh-project-actions", children: [projectLinks.journeyUrl && (_jsxs("a", { href: projectLinks.journeyUrl, className: "oh-button oh-secondary", rel: "noopener noreferrer", children: [_jsx(GitBranch, { size: 18 }), t("landing.followBuild"), _jsx(ArrowUpRight, { size: 16 })] })), projectLinks.repositoryUrl && (_jsxs("a", { href: projectLinks.repositoryUrl, className: "oh-button oh-secondary", target: "_blank", rel: "noopener noreferrer", children: [_jsx(FileText, { size: 18 }), t("landing.viewRepository"), _jsx(ArrowUpRight, { size: 16 })] }))] })] })), _jsxs("section", { className: "oh-bottom-cta", children: [_jsxs("svg", { viewBox: "0 0 1200 200", preserveAspectRatio: "none", "aria-hidden": "true", children: [_jsx("path", { d: "M0 100 C160 30 200 220 400 130 S750 120 880 150 S1080 30 1200 60" }), _jsx("circle", { cx: "90", cy: "95", r: "6" }), _jsx("circle", { cx: "1090", cy: "76", r: "6" })] }), _jsx("h2", { children: t("landing.finalCta") }), _jsxs("a", { className: "oh-button oh-primary", href: "#register", children: [t("landing.start"), _jsx(ArrowRight, { size: 17 })] })] })] }), _jsxs("footer", { className: "oh-footer", children: [_jsxs("div", { children: [_jsxs("a", { href: "#", className: "oh-brand", children: [_jsx(BrandMark, { size: 23 }), _jsx("span", { children: "openhow" })] }), _jsx("p", { children: t("landing.footer") })] }), _jsxs("nav", { "aria-label": t("landing.navigation"), children: [_jsx("button", { onClick: howItWorks, children: t("landing.how") }), _jsx("a", { href: "#public/cloudplay-demo", children: t("landing.exploreDemo") }), _jsx("a", { href: "#login", children: t("Sign in") })] }), _jsx(LanguagePicker, {})] })] }));
}
