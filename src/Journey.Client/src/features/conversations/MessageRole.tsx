import {
  Code2,
  MessageSquare,
  Settings2,
  Sparkles,
  UserRound,
  Wrench,
} from "lucide-react";
import { t } from "../../shared/i18n/i18n";

export function MessageRole({ role }: { role: string }) {
  const Icon =
    role === "user"
      ? UserRound
      : role === "assistant"
        ? Sparkles
        : role === "system"
          ? Settings2
          : role === "developer"
            ? Code2
            : role === "tool" || role === "function"
              ? Wrench
              : MessageSquare;
  return (
    <span className="message-role">
      <Icon size={16} aria-hidden="true" />
      <span>{t(role)}</span>
    </span>
  );
}
