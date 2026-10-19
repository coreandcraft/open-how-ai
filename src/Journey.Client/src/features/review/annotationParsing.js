export function topicTags(body) {
    // Tags start at a word boundary, excluding URL fragments and Markdown headings.
    return [
        ...new Set(Array.from(body.matchAll(/(?:^|\s)#([\p{L}\p{N}][\p{L}\p{N}_-]*)/gu), (m) => m[1].toLowerCase())),
    ];
}
export function threadTags(annotation) {
    return [
        ...new Set((annotation.comments || []).flatMap((c) => topicTags(c.body ?? c.text ?? ""))),
    ];
}
export function videoEmbed(value) {
    try {
        const url = new URL(value);
        if (url.protocol !== "https:" || url.username || url.password || url.port)
            return null;
        const host = url.hostname.toLowerCase();
        const parts = url.pathname.split("/").filter(Boolean);
        if ([
            "youtube.com",
            "www.youtube.com",
            "m.youtube.com",
            "youtu.be",
            "www.youtube-nocookie.com",
        ].includes(host)) {
            const id = host === "youtu.be"
                ? parts[0]
                : url.pathname === "/watch"
                    ? url.searchParams.get("v")
                    : ["embed", "shorts", "live"].includes(parts[0])
                        ? parts[1]
                        : null;
            if (!id || !/^[\w-]{11}$/.test(id))
                return null;
            const embed = new URL("https://www.youtube-nocookie.com/embed/" + id);
            const time = url.searchParams.get("t") || url.searchParams.get("start") || "";
            const match = time.match(/^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s?)?$/);
            if (match && time)
                embed.searchParams.set("start", String(Number(match[1] || 0) * 3600 +
                    Number(match[2] || 0) * 60 +
                    Number(match[3] || 0)));
            return embed.href;
        }
        if (["vimeo.com", "www.vimeo.com", "player.vimeo.com"].includes(host)) {
            const id = host === "player.vimeo.com" && parts[0] === "video"
                ? parts[1]
                : parts[0];
            if (!id || !/^\d+$/.test(id))
                return null;
            const embed = new URL("https://player.vimeo.com/video/" + id);
            const hash = url.searchParams.get("h") ||
                (host !== "player.vimeo.com" ? parts[1] : null);
            if (hash && /^[a-zA-Z0-9]+$/.test(hash))
                embed.searchParams.set("h", hash);
            return embed.href;
        }
    }
    catch {
        /* Unsupported links remain ordinary text. */
    }
    return null;
}
