import { BrandMark } from "../../shared/ui/BrandMark";
import { useEffect, useState } from "react";
import {
  ArrowRight,
  GitBranch,
  FileText,
  Lightbulb,
  Rocket,
  Network,
  Share2,
  Library,
  Search,
  Settings,
  ChevronDown,
  MessageSquare,
  Quote,
  ArrowUpRight,
  Check,
  Menu,
  X,
} from "lucide-react";
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
  return (
    <div className="oh-preview">
      <aside className="oh-preview-sidebar" aria-hidden="true">
        <div className="oh-mini-brand">
          <BrandMark size={20} /> openhow
        </div>
        <div className="oh-preview-nav active">
          <Library size={16} />
          {t("Projects")}
        </div>
        <div className="oh-preview-nav">
          <Search size={16} />
          {t("Search")}
        </div>
        <div className="oh-preview-nav">
          <Share2 size={16} />
          {t("landing.shared")}
        </div>
        <div className="oh-sidebar-note">
          <GitBranch size={24} />
          <strong>{t("landing.keepThinking")}</strong>
          <span>{t("landing.contextMatters")}</span>
        </div>
        <div className="oh-preview-nav">
          <Settings size={16} />
          {t("Settings")}
        </div>
      </aside>
      <div className="oh-preview-main">
        <div className="oh-preview-header">
          <strong>{t("landing.cloudplay")}</strong>
          <ChevronDown size={14} />
          <span className="oh-demo-label">{t("landing.example")}</span>
          <a href="#public/cloudplay-demo">
            {t("landing.openDemo")}
            <ArrowUpRight size={14} />
          </a>
        </div>
        <div className="oh-preview-tabs">
          <span>
            <GitBranch size={14} />
            {t("landing.journey")}
          </span>
          <small>{t("landing.selectNode")}</small>
        </div>
        <div className="oh-preview-body">
          <div
            className="oh-graph"
            role="group"
            aria-label={t("landing.previewLabel")}
          >
            <svg
              className="oh-connections"
              viewBox="0 0 700 380"
              preserveAspectRatio="none"
              aria-hidden="true"
            >
              <path d="M140 190 C170 190 160 95 210 95 M140 190 C170 190 160 285 210 285 M340 95 C390 95 365 190 410 190 M340 285 C390 285 365 190 410 190 M535 190 L565 190" />
              <circle cx="140" cy="190" r="4" />
              <circle cx="410" cy="190" r="4" />
            </svg>
            {stages.map((item, index) => (
              <button
                type="button"
                key={item.title}
                className={`oh-node oh-node-${index} ${selected === index ? "is-selected" : ""}`}
                aria-pressed={selected === index}
                onClick={() => setSelected(index)}
              >
                <item.icon size={22} />
                <strong>{t(item.title)}</strong>
                <span>{t(item.source)}</span>
                <div className="oh-node-lines" aria-hidden="true">
                  <i />
                  <i />
                  <i />
                </div>
              </button>
            ))}
          </div>
          <aside className="oh-evidence" aria-live="polite" aria-atomic="true">
            <div className="oh-evidence-kicker">
              <FileText size={16} />
              {t("landing.behindDecision")}
            </div>
            <h3>{t(stage.title)}</h3>
            <span className="oh-evidence-source">{t(stage.source)}</span>
            <blockquote>
              <Quote size={18} />
              <p>{t(stage.detail)}</p>
            </blockquote>
            <div className="oh-annotation">
              <MessageSquare size={15} />
              <strong>{t("landing.context")}</strong>
              <p>{t("landing.exampleNote")}</p>
            </div>
          </aside>
        </div>
      </div>
    </div>
  );
}

export function LandingPage() {
  useLocale();
  const [menuOpen, setMenuOpen] = useState(false);
  const [projectLinks, setProjectLinks] = useState<{
    journeyUrl?: string | null;
    repositoryUrl?: string | null;
  }>({});
  useEffect(() => {
    const controller = new AbortController();
    fetch("/api/site/links", { signal: controller.signal })
      .then((r) => (r.ok ? r.json() : {}))
      .then((links) => {
        if (!controller.signal.aborted) setProjectLinks(links);
      })
      .catch(() => {});
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
  return (
    <div className="oh-landing">
      <a
        className="oh-skip"
        href="#landing-content"
        onClick={(e) => {
          e.preventDefault();
          document.getElementById("landing-content")?.focus();
        }}
      >
        {t("landing.skip")}
      </a>
      <header className="oh-header">
        <a href="#" className="oh-brand" aria-label="openhow">
          <BrandMark size={29} />
          <span>openhow</span>
        </a>
        <nav className="oh-desktop-nav" aria-label={t("landing.navigation")}>
          <button onClick={howItWorks}>{t("landing.how")}</button>
          <a href="#public/cloudplay-demo">{t("landing.exploreJourney")}</a>
        </nav>
        <div className="oh-header-actions">
          <a className="oh-sign-in" href="#login">
            {t("Sign in")}
          </a>
          <a className="oh-button oh-primary oh-header-cta" href="#register">
            {t("landing.start")}
          </a>
          <button
            className="oh-menu-toggle"
            aria-expanded={menuOpen}
            aria-controls="oh-mobile-nav"
            aria-label={t(menuOpen ? "Close menu" : "Navigation menu")}
            onClick={() => setMenuOpen(!menuOpen)}
          >
            {menuOpen ? <X /> : <Menu />}
          </button>
        </div>
        {menuOpen && (
          <nav
            id="oh-mobile-nav"
            className="oh-mobile-nav"
            aria-label={t("landing.navigation")}
          >
            <button onClick={howItWorks}>{t("landing.how")}</button>
            <a href="#public/cloudplay-demo">{t("landing.exploreJourney")}</a>
            <a href="#register">{t("landing.start")}</a>
          </nav>
        )}
      </header>
      <main id="landing-content" tabIndex={-1}>
        <section className="oh-hero" aria-labelledby="oh-title">
          <p className="oh-eyebrow">{t("landing.eyebrow")}</p>
          <h1 id="oh-title">
            {t("landing.headline")}
            <br />
            <span>{t("landing.headlineEnd")}</span>
          </h1>
          <p className="oh-hero-copy">{t("landing.description")}</p>
          <div className="oh-hero-actions">
            <a href="#register" className="oh-button oh-primary">
              {t("landing.start")}
              <ArrowRight size={17} />
            </a>
            <a href="#public/cloudplay-demo" className="oh-button oh-secondary">
              {t("landing.exploreDemo")}
              <ArrowRight size={17} />
            </a>
          </div>
          <p className="oh-reassurance">{t("landing.reassurance")}</p>
        </section>
        <section className="oh-showcase" aria-label={t("landing.previewLabel")}>
          <JourneyDemo />
          <div className="oh-imports">
            <p>{t("landing.imports")}</p>
            <div>
              <span>
                <Network />
                ChatGPT
              </span>
              <span>
                <span className="oh-claude" aria-hidden="true">
                  ✳
                </span>
                Claude
              </span>
              <span>
                <FileText />
                JSON
              </span>
              <span>
                <FileText />
                Markdown
              </span>
            </div>
          </div>
        </section>
        <section
          className="oh-how"
          id="how-it-works"
          aria-labelledby="oh-how-title"
        >
          <p className="oh-eyebrow">{t("landing.how")}</p>
          <h2 id="oh-how-title">
            {t("landing.keepThinking")}
            <br />
            {t("landing.wholeJourney")}
          </h2>
          <div className="oh-features">
            <article>
              <span className="oh-feature-icon">
                <FileText />
              </span>
              <h3>{t("landing.preserve")}</h3>
              <p>{t("landing.preserveBody")}</p>
              <div className="oh-source-list" aria-hidden="true">
                {["ChatGPT", "Claude", "JSON", "Markdown"].map((source, i) => (
                  <div key={source}>
                    <FileText size={16} />
                    <span>{source}</span>
                    <i style={{ width: `${65 - i * 9}%` }} />
                  </div>
                ))}
              </div>
            </article>
            <article>
              <span className="oh-feature-icon">
                <Network />
              </span>
              <h3>{t("landing.connect")}</h3>
              <p>{t("landing.connectBody")}</p>
              <div className="oh-mini-graph" aria-hidden="true">
                <span>
                  <MessageSquare />
                </span>
                <i />
                <span>
                  <GitBranch />
                </span>
                <i />
                <span>
                  <Check />
                </span>
              </div>
            </article>
            <article>
              <span className="oh-feature-icon">
                <Share2 />
              </span>
              <h3>{t("landing.share")}</h3>
              <p>{t("landing.shareBody")}</p>
              <div className="oh-share-example">
                <div>
                  <Check size={16} />
                  {t("landing.selectedMessages")}
                </div>
                <div>
                  <Check size={16} />
                  {t("landing.reviewRedactions")}
                </div>
                <a href="#public/cloudplay-demo">
                  {t("landing.viewExample")}
                  <ArrowUpRight size={16} />
                </a>
              </div>
            </article>
          </div>
        </section>
        {(projectLinks.journeyUrl || projectLinks.repositoryUrl) && (
          <section
            className="oh-project-links"
            aria-labelledby="oh-project-title"
          >
            <div>
              <p className="oh-eyebrow">{t("landing.builtInOpen")}</p>
              <h2 id="oh-project-title">{t("landing.buildingOpenhow")}</h2>
              <p>{t("landing.behindOpenhow")}</p>
            </div>
            <div className="oh-project-actions">
              {projectLinks.journeyUrl && (
                <a
                  href={projectLinks.journeyUrl}
                  className="oh-button oh-secondary"
                  rel="noopener noreferrer"
                >
                  <GitBranch size={18} />
                  {t("landing.followBuild")}
                  <ArrowUpRight size={16} />
                </a>
              )}
              {projectLinks.repositoryUrl && (
                <a
                  href={projectLinks.repositoryUrl}
                  className="oh-button oh-secondary"
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  <FileText size={18} />
                  {t("landing.viewRepository")}
                  <ArrowUpRight size={16} />
                </a>
              )}
            </div>
          </section>
        )}
        <section className="oh-bottom-cta">
          <svg
            viewBox="0 0 1200 200"
            preserveAspectRatio="none"
            aria-hidden="true"
          >
            <path d="M0 100 C160 30 200 220 400 130 S750 120 880 150 S1080 30 1200 60" />
            <circle cx="90" cy="95" r="6" />
            <circle cx="1090" cy="76" r="6" />
          </svg>
          <h2>{t("landing.finalCta")}</h2>
          <a className="oh-button oh-primary" href="#register">
            {t("landing.start")}
            <ArrowRight size={17} />
          </a>
        </section>
      </main>
      <footer className="oh-footer">
        <div>
          <a href="#" className="oh-brand">
            <BrandMark size={23} />
            <span>openhow</span>
          </a>
          <p>{t("landing.footer")}</p>
        </div>
        <nav aria-label={t("landing.navigation")}>
          <button onClick={howItWorks}>{t("landing.how")}</button>
          <a href="#public/cloudplay-demo">{t("landing.exploreDemo")}</a>
          <a href="#login">{t("Sign in")}</a>
        </nav>
        <LanguagePicker />
      </footer>
    </div>
  );
}
