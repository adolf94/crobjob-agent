export interface JobDto {
  id: number;
  name: string;
  targetUrl: string;
  cronExpression: string;
  timezone: string;
  enabled: boolean;
  catchupDays: number;
  maxRetries: number;
  appendDateParam: boolean;
  authMode: 'none' | 'arauth';
  authScope?: string | null;
  lastDispatchedUtc?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  nextRunUtc?: string | null;
  lastRun?: RunRecord | null;
}

export interface RunRecord {
  id: number;
  jobId: number;
  scheduledForUtc: string;
  startedAtUtc: string;
  finishedAtUtc?: string | null;
  attempt: number;
  status: 'Running' | 'Succeeded' | 'Failed' | 'Skipped';
  httpStatus?: number | null;
  error?: string | null;
}

export interface JobInput {
  name: string;
  targetUrl: string;
  cronExpression: string;
  timezone: string;
  enabled: boolean;
  catchupDays: number;
  maxRetries: number;
  appendDateParam: boolean;
  authMode: 'none' | 'arauth';
  authScope?: string | null;
}

export interface RunResult {
  success: boolean;
  attempts: number;
  http_status?: number | null;
  error?: string | null;
}
