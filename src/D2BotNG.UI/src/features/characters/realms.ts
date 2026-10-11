import { GameFlavor } from "@/generated/captures_pb";

/**
 * The Battle.net realms, by the spellings they turn up in — kolbot's config keys (`useast`), the
 * old D2Bot profile editor's (`East`), and the realm names themselves — normalised to lower case
 * with the spaces dropped.
 */
const REALM_NAMES: Record<string, string> = {
  useast: "US East",
  east: "US East",
  uswest: "US West",
  west: "US West",
  europe: "Europe",
  asia: "Asia",
};

/**
 * How a captured realm reads. Empty is single player — the only game that reports no realm. A
 * value that is none of the four known realms (a private server's) is shown as reported.
 */
export function realmLabel(realm: string): string {
  if (!realm) return "Single player";
  return REALM_NAMES[realm.toLowerCase().replace(/\s+/g, "")] ?? realm;
}

/**
 * Whether a character's realm says anything. Only on LOD: a D2R character is not bound to a realm
 * — it can log into any of them — so what one reported is just where it last played.
 */
export function realmMatters(flavor: GameFlavor): boolean {
  return flavor === GameFlavor.LOD;
}
