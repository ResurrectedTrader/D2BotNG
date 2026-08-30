/** The Stats & Skills tab — two panels, both over the shared contract. */

import { Card, CardContent } from "@/components/ui";
import { PanelTitle } from "./CharacterChrome";
import { StatsPanel } from "./StatsPanel";
import { SkillsPanel } from "./SkillsPanel";
import type { SkillLevels, StatValue } from "./contracts";

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
          <PanelTitle>Skills</PanelTitle>
          <SkillsPanel skills={skills} charClass={charClass} />
        </CardContent>
      </Card>
    </div>
  );
}
