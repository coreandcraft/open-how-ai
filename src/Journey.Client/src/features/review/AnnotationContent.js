import { jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
import { useState } from "react";
import { t } from "../../shared/i18n/i18n";
import { threadTags, topicTags, videoEmbed } from "./annotationParsing";
import "./annotations.css";
export function AnnotationTools({ body, setBody, }) {
    return (_jsxs("div", { className: "annotation-tools", children: [_jsx("small", { children: t("Add #topics and paste YouTube or Vimeo links.") }), _jsx("div", { className: "annotation-topics", children: ["architecture", "operations", "important", "action"].map((tag) => (_jsxs("button", { type: "button", disabled: topicTags(body).includes(tag), onClick: () => setBody(body + (body && !/\s$/.test(body) ? " " : "") + "#" + tag + " "), children: ["#", tag] }, tag))) })] }));
}
export function TopicFilter({ annotations, value, onChange, }) {
    const tags = [...new Set(annotations.flatMap(threadTags))].sort();
    return (_jsxs("select", { "aria-label": t("Filter by topic"), value: value, onChange: (e) => onChange(e.target.value), children: [_jsx("option", { value: "", children: t("All topics") }), [...new Set([...tags, ...(value ? [value] : [])])].map((tag) => (_jsxs("option", { value: tag, children: ["#", tag] }, tag)))] }));
}
export function AnnotationTopics({ annotation }) {
    const tags = threadTags(annotation);
    return (_jsxs("span", { className: "annotation-topics", children: [tags.map((tag) => (_jsxs("span", { className: "annotation-topic topic-" +
                    (tag === "important" || tag === "action" ? tag : "default"), children: ["#", tag] }, tag))), tags.includes("action") && (_jsx("strong", { children: t(annotation.resolved ? "Completed" : "Action required") }))] }));
}
function Video({ url, embed }) {
    const [loaded, setLoaded] = useState(false);
    return (_jsxs("div", { className: "annotation-video", children: [loaded ? (_jsx("iframe", { src: embed, title: t("Embedded video"), loading: "lazy", allow: "encrypted-media; picture-in-picture; fullscreen", allowFullScreen: true, referrerPolicy: "strict-origin-when-cross-origin" })) : (_jsx("button", { type: "button", onClick: () => setLoaded(true), children: t("Load video") })), _jsx("a", { href: url, target: "_blank", rel: "noreferrer", children: t("Open video in new tab") })] }));
}
export function AnnotationContent({ body }) {
    const links = [...new Set(body.match(/https:\/\/[^\s<>"\[\]]+/g) || [])].map((url) => url.replace(/[).,;!?]+$/, ""));
    return (_jsxs("div", { className: "annotation-content", children: [_jsx("p", { children: body }), links.map((url) => {
                const embed = videoEmbed(url);
                return embed ? _jsx(Video, { url: url, embed: embed }, url) : null;
            })] }));
}
