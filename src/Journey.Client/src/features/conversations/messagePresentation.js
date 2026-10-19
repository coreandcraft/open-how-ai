const callKinds = new Set(["tool_use", "tool_call", "function_call", "tool"]);
const resultKinds = new Set(["tool_result", "function_call_output"]);
function record(value) {
    if (typeof value === "string") {
        // Large payloads remain available verbatim without parsing them to build a label.
        if (value.length > 48_000)
            return {};
        try {
            return record(JSON.parse(value));
        }
        catch {
            return {};
        }
    }
    return value !== null && typeof value === "object" && !Array.isArray(value)
        ? value
        : {};
}
function text(...values) {
    return (values.find((value) => typeof value === "string" && !!value.trim()) ?? "");
}
export function describeTool(block) {
    const raw = payloadRecord(block);
    const kind = block.kind.toLowerCase();
    const type = text(raw.type).toLowerCase();
    const isResult = resultKinds.has(kind) || resultKinds.has(type);
    const isCall = !isResult && (callKinds.has(kind) || callKinds.has(type));
    const fn = record(raw.function);
    const name = text(raw.name, raw.tool, fn.name);
    const input = record(raw.input ?? raw.arguments ?? raw.args ?? fn.arguments);
    const path = text(input.path, input.file_path, input.filePath, input.target_file);
    const target = path
        ? path.split(/[\\/]/).filter(Boolean).pop() || path
        : text(input.command, input.cmd, input.pattern, input.query, input.url);
    const key = name.toLowerCase().split(/[./]/).pop() || "";
    const icon = /^(read|read_file|readfile)$/.test(key)
        ? "read"
        : /^(write|write_file|writefile|edit|edit_file|apply_patch|multiedit)$/.test(key)
            ? "edit"
            : /^(grep|glob|search|web_search|file_search)$/.test(key)
                ? "search"
                : /^(bash|shell|terminal|exec|exec_command|run_command)$/.test(key)
                    ? "run"
                    : "tool";
    return { isCall, isResult, name, target, path, icon, input };
}
function payloadRecord(block) {
    return { ...record(block.metadata), ...record(block.text) };
}
function looksStructured(value) {
    const trimmed = value.trim();
    return trimmed.startsWith("{") || trimmed.startsWith("[");
}
export function sourceText(block) {
    const metadata = block.metadata || "";
    const text = block.text || "";
    // Prefer the structured original so a short extracted preview does not hide JSON.
    if (looksStructured(metadata))
        return metadata;
    if (looksStructured(text))
        return text;
    return text || metadata;
}
function previewText(...values) {
    return text(...values)
        .replace(/\s+/g, " ")
        .trim()
        .slice(0, 160);
}
function previewFrom(value) {
    if (typeof value === "string")
        return previewText(value);
    if (Array.isArray(value)) {
        for (const item of value) {
            const preview = previewFrom(item);
            if (preview)
                return preview;
        }
        return "";
    }
    if (value === null || typeof value !== "object")
        return "";
    const raw = record(value);
    return (previewText(raw.summary, raw.content, raw.text, raw.message, raw.title) ||
        (raw.chunks !== undefined ? previewFrom(raw.chunks) : "") ||
        (raw.thoughts !== undefined ? previewFrom(raw.thoughts) : ""));
}
function humanize(type) {
    return type
        .split(/[_-]+/)
        .filter(Boolean)
        .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
        .join(" ");
}
function activityLabel(type) {
    if (/^(thoughts?|thinking)$/.test(type))
        return "Thinking";
    if (/^(reasoning|reasoning_content|agent_reasoning)$/.test(type))
        return "Reasoning";
    if (/^(reasoning_recaps?|recap)$/.test(type))
        return "Reasoning recap";
    if (type === "execution_output")
        return "Execution output";
    if (type === "computer_output")
        return "Computer output";
    if (type === "system_error")
        return "System error";
    if (!type || type === "unknown")
        return "Preserved source";
    return humanize(type);
}
export function describeActivity(block) {
    const raw = payloadRecord(block);
    const type = (text(raw.content_type, raw.type) || block.kind).toLowerCase();
    const isProgress = /^(thoughts?|thinking|reasoning|reasoning_content|agent_reasoning)$/.test(type);
    const isResult = /^(reasoning_recaps?|recap|execution_output|computer_output|system_error)$/.test(type);
    return {
        isProgress,
        isResult,
        tone: isProgress ? "progress" : isResult ? "result" : "source",
        label: activityLabel(type),
        preview: previewFrom(raw) || previewText(block.text),
    };
}
export function groupSourceBlocks(blocks) {
    const groups = [];
    for (const block of [...blocks].sort((a, b) => a.order - b.order)) {
        // Only adjacent calls are grouped, so narration and results keep their source order.
        const tools = block.kind !== "text" &&
            block.kind !== "markdown" &&
            describeTool(block).isCall;
        const previous = groups.at(-1);
        if (tools && previous?.tools)
            previous.blocks.push(block);
        else
            groups.push({ tools, blocks: [block] });
    }
    return groups;
}
