import { useState } from "react";
import { t } from "../../shared/i18n/i18n";
import { threadTags, topicTags, videoEmbed } from "./annotationParsing";
import "./annotations.css";

export function AnnotationTools({
  body,
  setBody,
}: {
  body: string;
  setBody: (body: string) => void;
}) {
  return (
    <div className="annotation-tools">
      <small>{t("Add #topics and paste YouTube or Vimeo links.")}</small>
      <div className="annotation-topics">
        {["architecture", "operations", "important", "action"].map((tag) => (
          <button
            type="button"
            key={tag}
            disabled={topicTags(body).includes(tag)}
            onClick={() =>
              setBody(
                body + (body && !/\s$/.test(body) ? " " : "") + "#" + tag + " ",
              )
            }
          >
            #{tag}
          </button>
        ))}
      </div>
    </div>
  );
}

export function TopicFilter({
  annotations,
  value,
  onChange,
}: {
  annotations: any[];
  value: string;
  onChange: (value: string) => void;
}) {
  const tags = [...new Set(annotations.flatMap(threadTags))].sort();
  return (
    <select
      aria-label={t("Filter by topic")}
      value={value}
      onChange={(e) => onChange(e.target.value)}
    >
      <option value="">{t("All topics")}</option>
      {[...new Set([...tags, ...(value ? [value] : [])])].map((tag) => (
        <option key={tag} value={tag}>
          #{tag}
        </option>
      ))}
    </select>
  );
}

export function AnnotationTopics({ annotation }: { annotation: any }) {
  const tags = threadTags(annotation);
  return (
    <span className="annotation-topics">
      {tags.map((tag) => (
        <span
          className={
            "annotation-topic topic-" +
            (tag === "important" || tag === "action" ? tag : "default")
          }
          key={tag}
        >
          #{tag}
        </span>
      ))}
      {tags.includes("action") && (
        <strong>
          {t(annotation.resolved ? "Completed" : "Action required")}
        </strong>
      )}
    </span>
  );
}

function Video({ url, embed }: { url: string; embed: string }) {
  const [loaded, setLoaded] = useState(false);
  return (
    <div className="annotation-video">
      {loaded ? (
        <iframe
          src={embed}
          title={t("Embedded video")}
          loading="lazy"
          allow="encrypted-media; picture-in-picture; fullscreen"
          allowFullScreen
          referrerPolicy="strict-origin-when-cross-origin"
        />
      ) : (
        <button type="button" onClick={() => setLoaded(true)}>
          {t("Load video")}
        </button>
      )}
      <a href={url} target="_blank" rel="noreferrer">
        {t("Open video in new tab")}
      </a>
    </div>
  );
}

export function AnnotationContent({ body }: { body: string }) {
  const links = [...new Set(body.match(/https:\/\/[^\s<>"\[\]]+/g) || [])].map(
    (url) => url.replace(/[).,;!?]+$/, ""),
  );
  return (
    <div className="annotation-content">
      <p>{body}</p>
      {links.map((url) => {
        const embed = videoEmbed(url);
        return embed ? <Video key={url} url={url} embed={embed} /> : null;
      })}
    </div>
  );
}
