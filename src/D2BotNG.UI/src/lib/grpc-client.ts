/**
 * gRPC-Web client setup using Connect-RPC
 *
 * This module provides typed gRPC clients for all D2Bot services.
 * Connect-RPC v2 uses service descriptors from protoc-gen-es directly.
 */

import { createGrpcWebTransport } from "@connectrpc/connect-web";
import { createClient, type Interceptor, Code } from "@connectrpc/connect";
import { getAuthPassword, triggerAuthRequired } from "./auth";

// Import service descriptors from generated files
import { EventService } from "../generated/events_pb";
import { ItemService } from "../generated/items_pb";
import { KeyService } from "../generated/keys_pb";
import { ProxyService } from "../generated/proxies_pb";
import { ProfileService } from "../generated/profiles_pb";
import { ScheduleService } from "../generated/schedules_pb";
import { SettingsService, FileService } from "../generated/settings_pb";
import { UpdateService } from "../generated/updates_pb";
import { LoggingService } from "../generated/logging_pb";
import { CharacterService } from "../generated/characters_pb";

/** LocalStorage key for dev backend URL override */
const DEV_BACKEND_URL_KEY = "d2bot-dev-backend-url";

/** Default backend URL for development */
const DEFAULT_DEV_BACKEND_URL = "http://localhost:5000";

/**
 * Get the dev backend URL from localStorage, or return default.
 */
export function getDevBackendUrl(): string {
  if (typeof window === "undefined") return DEFAULT_DEV_BACKEND_URL;
  return localStorage.getItem(DEV_BACKEND_URL_KEY) || DEFAULT_DEV_BACKEND_URL;
}

/**
 * Set the dev backend URL in localStorage.
 */
export function setDevBackendUrl(url: string): void {
  if (typeof window === "undefined") return;
  if (url === DEFAULT_DEV_BACKEND_URL || url === "") {
    localStorage.removeItem(DEV_BACKEND_URL_KEY);
  } else {
    localStorage.setItem(DEV_BACKEND_URL_KEY, url);
  }
}

/**
 * Get the base URL for gRPC services.
 * In development, connect directly to the C# backend on port 5000.
 * In production, use the same origin (served by the backend).
 */
export function getBaseUrl(): string {
  if (typeof window === "undefined") return "";
  if (import.meta.env.DEV) return getDevBackendUrl();
  return window.location.origin;
}

/**
 * Auth interceptor that adds password header and detects auth failures.
 */
const authInterceptor: Interceptor = (next) => async (req) => {
  // Add auth header if password is set
  const password = getAuthPassword();
  if (password) {
    req.header.set("x-auth-password", password);
  }

  try {
    return await next(req);
  } catch (err) {
    // Check for unauthenticated error
    if (
      err &&
      typeof err === "object" &&
      "code" in err &&
      err.code === Code.Unauthenticated
    ) {
      triggerAuthRequired();
    }
    throw err;
  }
};

/**
 * Create gRPC-Web transport for communicating with the C# backend.
 * The C# backend uses Grpc.AspNetCore.Web which implements the gRPC-Web protocol.
 */
const transport = createGrpcWebTransport({
  baseUrl: getBaseUrl(),
  interceptors: [authInterceptor],
});

/**
 * Transport for unary command/mutation RPCs, with a client-side deadline so a
 * stuck call (e.g. a window command against a hung game) can never spin the UI
 * forever — a safety net on top of the backend's non-blocking calls. These
 * commands return promptly (Start backgrounds the launch rather than waiting on
 * the startup semaphore; Stop is bounded by the ~5s graceful-shutdown grace), so
 * 10s is ample. Genuinely slow external calls (e.g. TestDiscord) pass a longer
 * per-call timeoutMs. NOT used for the long-lived event stream or update streaming.
 */
const commandTransport = createGrpcWebTransport({
  baseUrl: getBaseUrl(),
  interceptors: [authInterceptor],
  defaultTimeoutMs: 10000,
});

/**
 * Profile management client
 * - CRUD operations for bot profiles
 * - Start/Stop/Restart controls
 * - Window visibility controls
 * - Status streaming
 */
export const profileClient = createClient(ProfileService, commandTransport);

/**
 * CD Key management client
 * - KeyList CRUD operations
 * - Key hold/release operations
 * - Key status dashboard
 */
export const keyClient = createClient(KeyService, commandTransport);

/**
 * Proxy management client
 * - Proxy CRUD + bulk import
 * - SOCKS5 connection test
 */
export const proxyClient = createClient(ProxyService, transport);

/**
 * Schedule management client
 * - Schedule CRUD operations
 * - Import/Export functionality
 */
export const scheduleClient = createClient(ScheduleService, commandTransport);

/**
 * Server settings client
 * - Get/Update server configuration
 */
export const settingsClient = createClient(SettingsService, commandTransport);

/**
 * File browser client
 * - Remote directory listing for D2 path selection
 */
export const fileClient = createClient(FileService, commandTransport);

/**
 * Update service client
 * - Check for updates
 * - Stream update status
 * - Trigger updates
 */
export const updateClient = createClient(UpdateService, transport);

/**
 * Event service client
 * - Clear system messages
 */
export const eventClient = createClient(EventService, transport);

/**
 * Item service client
 * - List entities (characters/directories)
 * - Search items with filters
 */
export const itemClient = createClient(ItemService, commandTransport);

/**
 * Logging configuration client
 * - Get/Set per-logger log levels for UI console
 */
export const loggingClient = createClient(LoggingService, commandTransport);

/**
 * Character analytics client
 * - Reset accumulated kill counts
 */
export const characterClient = createClient(CharacterService, transport);
