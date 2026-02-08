import { useQuery } from "@tanstack/react-query";
import { fileClient } from "@/lib/grpc-client";

export function useDirectoryListing(path: string) {
  return useQuery({
    queryKey: ["directory", path],
    queryFn: () => fileClient.listDirectory({ path }),
  });
}
