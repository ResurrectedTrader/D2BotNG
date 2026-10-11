import clsx from "clsx";
import { GameFlavor } from "@/generated/captures_pb";

/** Short names for a game, as the badge and the search filter show them. */
export const FLAVOR_LABELS: Record<GameFlavor, string> = {
  [GameFlavor.LOD]: "LOD",
  [GameFlavor.D2R]: "D2R",
};

/**
 * Which game a character is from. Rendered only where more than one game is present — the caller
 * decides that — since a badge every row carries says nothing.
 */
export function FlavorBadge({ flavor }: { flavor: GameFlavor }) {
  return (
    <span
      className={clsx(
        "rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide",
        flavor === GameFlavor.D2R
          ? "bg-red-900/40 text-red-300"
          : "bg-zinc-700/60 text-zinc-300",
      )}
      title={
        flavor === GameFlavor.D2R
          ? "Diablo II: Resurrected"
          : "Diablo II: Lord of Destruction"
      }
    >
      {FLAVOR_LABELS[flavor] ?? "?"}
    </span>
  );
}
