import { t } from "../../shared/i18n/i18n";
import { preview } from "../../shared/state";

type PreviewNode = {
  id: string;
  position?: { x: number; y: number };
  data?: { title?: string; kind?: string; provider?: string };
};

/** A read-only preview of the saved journey, with its real nodes and connections. */
export function JourneyPreview({ journey }: { journey: any }) {
  const saved = preview(journey);
  const nodes: PreviewNode[] = (saved.nodes || []).filter(
    (n: PreviewNode) => n.data?.kind !== "group",
  );
  if (!nodes.length)
    return (
      <div className="preview-empty">
        {t("A new story waiting to be connected.")}
      </div>
    );
  const positions = nodes.map((node, index) => ({
    ...node,
    x: Number.isFinite(node.position?.x) ? node.position!.x : index * 300,
    y: Number.isFinite(node.position?.y) ? node.position!.y : 0,
  }));
  const minX = Math.min(...positions.map((n) => n.x));
  const minY = Math.min(...positions.map((n) => n.y));
  const width = Math.max(...positions.map((n) => n.x)) - minX + 300;
  const height = Math.max(...positions.map((n) => n.y)) - minY + 120;
  const byId = new Map(positions.map((n) => [n.id, n]));
  return (
    <svg
      className="journey-preview"
      viewBox={`${minX - 25} ${minY - 25} ${width + 50} ${height + 50}`}
      aria-hidden="true"
    >
      {(saved.edges || []).map(
        (edge: { id?: string; source: string; target: string }, i: number) => {
          const from = byId.get(edge.source),
            to = byId.get(edge.target);
          if (!from || !to) return null;
          const x = from.x + 260,
            y = from.y + 45,
            endX = to.x,
            endY = to.y + 45;
          const bend = Math.max(45, Math.abs(endX - x) / 2);
          return (
            <path
              key={edge.id || i}
              d={`M${x},${y} C${x + bend},${y} ${endX - bend},${endY} ${endX},${endY}`}
            />
          );
        },
      )}
      {positions.map((node) => (
        <g key={node.id}>
          <rect x={node.x} y={node.y} width="260" height="90" rx="12" />
          <circle cx={node.x + 20} cy={node.y + 26} r="5" />
          <text className="preview-kind" x={node.x + 35} y={node.y + 31}>
            {node.data?.provider || t(node.data?.kind || "note")}
          </text>
          <text x={node.x + 16} y={node.y + 62}>
            {(node.data?.title || t("Untitled")).slice(0, 27)}
            {(node.data?.title?.length || 0) > 27 ? "…" : ""}
          </text>
        </g>
      ))}
    </svg>
  );
}

export function BlueprintIllustration() {
  const nodes = [
    [24, 110, "Research"],
    [210, 35, "Explore"],
    [210, 185, "Decision"],
    [396, 110, "Build"],
  ] as const;
  return (
    <svg
      className="blueprint-illustration"
      viewBox="0 0 540 280"
      aria-hidden="true"
    >
      <path d="M144 136 C178 136 175 61 210 61 M144 136 C178 136 175 211 210 211 M330 61 C365 61 360 136 396 136 M330 211 C365 211 360 136 396 136" />
      {nodes.map(([x, y, label]) => (
        <g key={label}>
          <rect x={x} y={y} width="120" height="52" rx="8" />
          <circle cx={x + 18} cy={y + 26} r="4" />
          <text x={x + 31} y={y + 31}>
            {t(label)}
          </text>
        </g>
      ))}
    </svg>
  );
}
