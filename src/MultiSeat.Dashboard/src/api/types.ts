// Mirrors MultiSeat.Shared.Models — keep in sync with the C# models

export type SeatStatus =
  | "Idle"
  | "Provisioning"
  | "Configuring"
  | "Ready"
  | "Streaming"
  | "TearingDown"
  | "Error";

export interface SeatInfo {
  id: string;
  accountName: string;
  sessionId: number;
  status: SeatStatus;
  width: number;
  height: number;
  fps: number;
  displayDevicePath: string | null;
  portBase: number;
  apolloProcessId: number;
  audioDeviceId: string | null;
  vacCableIndex: number;
  viGEmControllerIndex: number;
  createdAt: string;
  readyAt: string | null;
  errorMessage: string | null;
  launchApp: string | null;
}

export interface SeatRequest {
  accountName: string;
  width?: number;
  height?: number;
  fps?: number;
  launchApp?: string;
}

export interface LaunchAppRequest {
  executablePath: string;
  arguments?: string;
  workingDirectory?: string;
}

export interface AccountInfo {
  username: string;
  sid: string | null;
  profilePath: string | null;
  isManaged: boolean;
  createdAt: string;
}

export interface AccountCreateRequest {
  username: string;
  password: string;
}

export interface GpuInfo {
  name: string;
  utilizationPercent: number;
  vramTotalMb: number;
  vramUsedMb: number;
  encoderUtilizationPercent: number;
  activeEncoderSessions: number;
}

export interface SystemStatus {
  activeSeats: number;
  maxSeats: number;
  gpu: GpuInfo | null;
  systemMemoryMb: number;
  availableMemoryMb: number;
  windowsBuild: string;
  rdpWrapperActive: boolean;
  timestamp: string;
}

// ── Input ──────────────────────────────────────────────────────────

export interface ControllerInfo {
  xInputIndex: number;
  assignedSeatId: string | null;
  assignedSeatName: string | null;
}

export interface ControllerAssignments {
  [xinputIndex: string]: string; // index → seatId
}

export interface HookStatus {
  installed: boolean;
  targetSessionId: number;
  enabled: boolean;
}

// ── Per-seat service status ───────────────────────────────────────

export interface SeatServices {
  apollo: boolean;
  apolloRestarts: number;
  display: boolean;
  audio: boolean;
  controller: boolean;
  inputHooks: boolean;
  firewall: boolean;
  session: boolean;
}
