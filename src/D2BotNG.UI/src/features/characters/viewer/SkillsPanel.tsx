/**
 * SkillsPanel - lists the character's invested skills, grouped by their skill-tree
 * tab (page), with hard points and the +skills bonus from gear. The sender only
 * reports skills with hard points invested, so this is the allocated portion of
 * the class skill tree. Skills outside the class tabs fall into "Other".
 */

import type { Character } from "@/generated/characters_pb";
import { SKILL_NAMES } from "./data/skillNames";
import { CLASS_SKILL_TABS } from "./skillTabs";

export function SkillsPanel({ character }: { character: Character }) {
  const skills = character.skills;
  if (skills.length === 0) {
    return <p className="text-sm text-zinc-500">No skills reported.</p>;
  }

  const byId = new Map<number, { hard: number; soft: number }>();
  for (const s of skills)
    byId.set(s.skillId, { hard: s.hardPoints, soft: s.softPoints });

  const tabs = CLASS_SKILL_TABS[character.charClass] ?? [];
  const known = new Set(tabs.flatMap((t) => t.skillIds));
  const otherIds = [...byId.keys()]
    .filter((id) => !known.has(id))
    .sort((a, b) => a - b);

  const groups = [
    ...tabs
      .map((tab) => ({
        name: tab.name,
        ids: tab.skillIds.filter((id) => byId.has(id)),
      }))
      .filter((g) => g.ids.length > 0),
    ...(otherIds.length ? [{ name: "Other", ids: otherIds }] : []),
  ];

  return (
    <div className="grid gap-x-8 gap-y-4 sm:grid-cols-2 lg:grid-cols-3">
      {groups.map((group) => (
        <div key={group.name}>
          <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-zinc-100">
            {group.name}
          </h3>
          <div className="space-y-0.5">
            {group.ids.map((id) => {
              const name = SKILL_NAMES[id] ?? `Skill ${id}`;
              const pts = byId.get(id)!;
              return (
                <div
                  key={id}
                  className="flex items-center justify-between gap-2 text-sm"
                >
                  <span className="truncate text-zinc-400" title={name}>
                    {name}
                  </span>
                  <span className="flex-shrink-0 font-medium text-zinc-400">
                    {pts.hard}
                    {pts.soft > 0 && (
                      <span className="text-green-400"> +{pts.soft}</span>
                    )}
                  </span>
                </div>
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
}
