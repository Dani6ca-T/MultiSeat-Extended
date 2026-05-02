// Mirrors MultiSeat.Shared.Models — keep in sync with the C# models

export type NvencQualityPreset = "Latency" | "Balanced" | "Quality";

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
  autoStart: boolean;
  nvencPreset: NvencQualityPreset;
  provisioningStep: string | null;
}

export interface SeatPreset {
  id: string;
  accountName: string;
  width: number;
  height: number;
  fps: number;
  autoStart: boolean;
  nvencPreset: NvencQualityPreset;
  createdAt: string;
}

export interface SeatRequest {
  accountName: string;
  width?: number;
  height?: number;
  fps?: number;
  launchApp?: string;
  nvencPreset?: NvencQualityPreset;
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

export interface CpuInfo {
  name: string;
  utilizationPercent: number;
  logicalCores: number;
}

export interface NetworkInfo {
  primaryAdapter: string;
  bytesReceivedPerSec: number;
  bytesSentPerSec: number;
}

export interface TopProcess {
  pid: number;
  name: string;
  cpuPercent: number;
  memoryMb: number;
  networkConnections: number;
}

export interface DiskInfo {
  name: string;
  label: string;
  totalMb: number;
  freeMb: number;
  usedPercent: number;
}

export type HealthStatus = "Healthy" | "Warning" | "Critical";

export interface HealthScore {
  status: HealthStatus;
  score: number;
  issues: string[];
}

export interface SystemStatus {
  activeSeats: number;
  maxSeats: number;
  gpu: GpuInfo | null;
  cpu: CpuInfo | null;
  network: NetworkInfo | null;
  topProcesses: TopProcess[];
  disks: DiskInfo[];
  health: HealthScore;
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
