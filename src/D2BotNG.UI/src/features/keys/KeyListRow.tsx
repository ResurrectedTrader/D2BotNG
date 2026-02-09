/**
 * KeyListRow component
 *
 * A row in the key list that can be expanded to show individual keys.
 */

import { useCallback } from "react";
import { Badge, Button } from "@/components/ui";
import { useHoldKey, useReleaseKey } from "@/hooks";
import type { KeyList, CDKey } from "@/generated/keys_pb";
import type { KeyUsage } from "@/generated/events_pb";
import {
  ArrowPathIcon,
  ChevronRightIcon,
  PencilIcon,
  TrashIcon,
  LockClosedIcon,
  LockOpenIcon,
} from "@heroicons/react/24/outline";

export interface KeyListRowProps {
  keyList: KeyList;
  usage: KeyUsage[];
  isExpanded: boolean;
  onToggle: () => void;
  onEdit: (keyList: KeyList) => void;
  onDelete: (keyList: KeyList) => void;
}

/**
 * Mask a CD key for display (show first 4 and last 4 characters)
 */
function maskKey(key: string): string {
  if (key.length <= 8) return key;
  const start = key.substring(0, 4);
  const end = key.substring(key.length - 4);
  const middle = "*".repeat(Math.max(0, key.length - 8));
  return start + middle + end;
}

/**
 * Get the status of a key
 */
function getKeyStatus(
  key: CDKey,
  usedByProfile: string,
): "available" | "in-use" | "held" {
  if (key.held) return "held";
  if (usedByProfile) return "in-use";
  return "available";
}

/**
 * Get badge variant for key status
 */
function getStatusVariant(
  status: "available" | "in-use" | "held",
): "green" | "yellow" | "gray" {
  switch (status) {
    case "available":
      return "green";
    case "in-use":
      return "yellow";
    case "held":
      return "gray";
  }
}

export function KeyListRow({
  keyList,
  usage,
  isExpanded,
  onToggle,
  onEdit,
  onDelete,
}: KeyListRowProps) {
  const holdKey = useHoldKey();
  const releaseKey = useReleaseKey();

  // Build a lookup from key name to profile name using the usage array
  const usageMap = new Map(usage.map((u) => [u.keyName, u.profileName]));

  const handleHoldKey = useCallback(
    (keyName: string) => {
      holdKey.mutate({ keyListName: keyList.name, keyName });
    },
    [holdKey, keyList.name],
  );

  const handleReleaseKey = useCallback(
    (keyName: string) => {
      releaseKey.mutate({ keyListName: keyList.name, keyName });
    },
    [releaseKey, keyList.name],
  );

  const keyCount = keyList.keys.length;
  const availableCount = keyList.keys.filter(
    (k) => !k.held && !usageMap.get(k.name),
  ).length;

  return (
    <>
      {/* Row header */}
      <div
        className="flex items-center justify-between px-4 py-3 cursor-pointer hover:bg-zinc-800/50 transition-colors"
        onClick={onToggle}
      >
        <div className="flex items-center gap-3">
          <ChevronRightIcon
            className={`h-4 w-4 text-zinc-400 transition-transform ${isExpanded ? "rotate-90" : ""}`}
          />
          <div>
            <span className="font-medium text-zinc-100">{keyList.name}</span>
            <span className="ml-3 text-sm text-zinc-500">
              {keyCount} {keyCount === 1 ? "key" : "keys"} ({availableCount}{" "}
              available)
            </span>
          </div>
        </div>

        <div
          className="flex items-center gap-1"
          onClick={(e) => e.stopPropagation()}
        >
          <Button
            variant="ghost"
            size="sm"
            onClick={() => onEdit(keyList)}
            aria-label="Edit key list"
          >
            <PencilIcon className="h-4 w-4" />
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => onDelete(keyList)}
            aria-label="Delete key list"
          >
            <TrashIcon className="h-4 w-4" />
          </Button>
        </div>
      </div>

      {/* Expanded content */}
      {isExpanded && (
        <div className="bg-zinc-900/50">
          {keyList.keys.length > 0 ? (
            <div className="divide-y divide-zinc-800/50">
              {keyList.keys.map((key) => {
                const usedByProfile = usageMap.get(key.name) ?? "";
                const status = getKeyStatus(key, usedByProfile);
                const isHolding =
                  holdKey.isPending &&
                  holdKey.variables?.keyListName === keyList.name &&
                  holdKey.variables?.keyName === key.name;
                const isReleasing =
                  releaseKey.isPending &&
                  releaseKey.variables?.keyListName === keyList.name &&
                  releaseKey.variables?.keyName === key.name;

                return (
                  <div
                    key={key.name}
                    className="flex items-center justify-between px-4 py-2 pl-11 hover:bg-zinc-800/30 transition-colors"
                  >
                    <div className="flex-1 min-w-0">
                      <div className="flex flex-col gap-1 sm:flex-row sm:items-center sm:gap-6">
                        <div className="sm:w-32 shrink-0">
                          <span className="text-sm font-medium text-zinc-100">
                            {key.name}
                          </span>
                        </div>
                        <div className="sm:flex-1">
                          <span className="text-xs text-zinc-500 mr-2">
                            Classic:
                          </span>
                          <code className="text-sm font-mono text-zinc-300">
                            {maskKey(key.classic)}
                          </code>
                        </div>
                        <div className="sm:flex-1">
                          <span className="text-xs text-zinc-500 mr-2">
                            Expansion:
                          </span>
                          <code className="text-sm font-mono text-zinc-300">
                            {maskKey(key.expansion)}
                          </code>
                        </div>
                      </div>
                    </div>

                    <div className="flex items-center gap-2 ml-4 shrink-0">
                      {key.realmDowns > 0 && (
                        <span
                          className="text-xs text-red-400"
                          title="Realm down events"
                        >
                          RD: {key.realmDowns}
                        </span>
                      )}

                      <Badge
                        variant={getStatusVariant(status)}
                        className="min-w-[70px] max-w-[120px] justify-center truncate"
                        title={status === "in-use" ? usedByProfile : undefined}
                      >
                        {status === "available" && "Available"}
                        {status === "in-use" && (usedByProfile || "In Use")}
                        {status === "held" && "Held"}
                      </Badge>

                      {status === "available" && (
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => handleHoldKey(key.name)}
                          disabled={isHolding}
                          title="Hold key"
                        >
                          {isHolding ? (
                            <ArrowPathIcon className="h-4 w-4 animate-spin" />
                          ) : (
                            <LockClosedIcon className="h-4 w-4" />
                          )}
                        </Button>
                      )}
                      {status === "held" && (
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => handleReleaseKey(key.name)}
                          disabled={isReleasing}
                          title="Release key"
                        >
                          {isReleasing ? (
                            <ArrowPathIcon className="h-4 w-4 animate-spin" />
                          ) : (
                            <LockOpenIcon className="h-4 w-4" />
                          )}
                        </Button>
                      )}
                      {status === "in-use" && <div className="w-8" />}
                    </div>
                  </div>
                );
              })}
            </div>
          ) : (
            <div className="px-4 py-4 pl-11 text-sm text-zinc-500">
              No keys in this list. Edit to add keys.
            </div>
          )}
        </div>
      )}
    </>
  );
}
