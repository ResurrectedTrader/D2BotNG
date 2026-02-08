/**
 * Hook to check if the app is being accessed from localhost.
 * Used to conditionally show sensitive controls like window visibility.
 */
export function useIsLocalhost(): boolean {
  const hostname = window.location.hostname;
  return (
    hostname === "localhost" ||
    hostname === "127.0.0.1" ||
    hostname === "::1" ||
    hostname === ""
  );
}
