/** The Stats & Skills tab — two panels, both over the shared contract. */

import { Card, CardContent } from "@/components/ui";
import { PanelTitle } from "./CharacterChrome";
import { SKILL_POINTS, StatsPanel } from "./StatsPanel";
import { SkillsPanel } from "./SkillsPanel";
import type { SkillLevels, StatValue } from "./contracts";

/** Unspent skill points, beside the panel they would be spent in; nothing when there are none. */
function UnspentSkillPoints({ stats }: { stats: StatValue[] }) {
  const points = stats.find((s) => s.id === SKILL_POINTS)?.value ?? 0n;
  if (points <= 0n) return null;
  return (
    <span className="text-xs font-medium text-amber-400">
      {points.toLocaleString()} unspent
    </span>
  );
}

export function StatsSkillsTab({
  stats,
  difficulty,
  skills,
  charClass,
}: {
  stats: StatValue[];
  difficulty: number;
  skills: SkillLevels[];
  charClass: number;
}) {
  return (
    <div className="space-y-4">
      <Card>
        <CardContent>
          <PanelTitle>Stats</PanelTitle>
          <StatsPanel stats={stats} difficulty={difficulty} />
        </CardContent>
      </Card>
      <Card>
        <CardContent>
          <PanelTitle right={<UnspentSkillPoints stats={stats} />}>
            Skills
          </PanelTitle>
          <SkillsPanel skills={skills} charClass={charClass} />
        </CardContent>
      </Card>
    </div>
  );
}
