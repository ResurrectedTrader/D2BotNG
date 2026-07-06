import { useEffect, useState } from "react";
import { fileClient } from "@/lib/grpc-client";

export interface EntryScriptOption {
  value: string;
  label: string;
}

export function useEntryScripts(
  d2bsPath: string | undefined,
): EntryScriptOption[] {
  const [options, setOptions] = useState<EntryScriptOption[]>([]);

  useEffect(() => {
    // Clear any previously-loaded scripts when there's no d2bs path (e.g. switching to
    // a framework that hasn't set one) so stale options from the prior path don't linger.
    if (!d2bsPath) {
      setOptions([]);
      return;
    }

    const path = d2bsPath;
    async function load() {
      try {
        const d2bsListing = await fileClient.listDirectory({ path });

        const botDirs = d2bsListing.entries
          .filter((e) => e.isDirectory && e.name.toLowerCase().endsWith("bot"))
          .map((e) => e.name)
          .sort((a, b) => a.localeCompare(b));

        if (botDirs.length === 0) {
          setOptions([]);
          return;
        }

        const botPath = `${path}/${botDirs[0]}`;
        const botListing = await fileClient.listDirectory({ path: botPath });
        const dbjFiles = botListing.entries
          .filter(
            (e) => !e.isDirectory && e.name.toLowerCase().endsWith(".dbj"),
          )
          .map((e) => e.name)
          .sort((a, b) => a.localeCompare(b));

        setOptions(dbjFiles.map((name) => ({ value: name, label: name })));
      } catch (err) {
        console.error("Failed to load entry scripts:", err);
        setOptions([]);
      }
    }
    load();
  }, [d2bsPath]);

  return options;
}
