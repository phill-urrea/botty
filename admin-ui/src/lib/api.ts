const API_BASE = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5001/api';

async function fetchApi<T>(endpoint: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${endpoint}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...options?.headers,
    },
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: response.statusText }));
    throw new Error(error.error || 'API request failed');
  }

  return response.json();
}

// Kanban Types
export interface KanbanTask {
  id: string;
  title: string;
  description?: string;
  type: string;
  lane: string;
  assignee: string;
  priority: string;
  conversationId?: string;
  userId?: string;
  source?: string;
  externalId?: string;
  pendingAction?: PendingAction;
  createdAt: string;
  updatedAt: string;
}

export interface PendingAction {
  actionType: string;
  description: string;
  payload?: Record<string, string>;
  preview?: string;
  requiresApproval: boolean;
}

export interface CreateTaskRequest {
  title: string;
  description?: string;
  type?: string;
  assignee?: string;
  priority?: string;
}

// Kanban API
export const kanbanApi = {
  getTasks: async (): Promise<{ tasks: KanbanTask[] }> => {
    const board = await fetchApi<{
      toDo: KanbanTask[];
      inProgress: KanbanTask[];
      needsApproval: KanbanTask[];
      done: KanbanTask[];
      cancelled: KanbanTask[];
    }>('/kanban/board');
    return {
      tasks: [
        ...(board.toDo ?? []),
        ...(board.inProgress ?? []),
        ...(board.needsApproval ?? []),
        ...(board.done ?? []),
        ...(board.cancelled ?? []),
      ],
    };
  },
  getTask: (id: string) => fetchApi<KanbanTask>(`/kanban/${id}`),
  createTask: (task: CreateTaskRequest) => 
    fetchApi<KanbanTask>('/kanban', { method: 'POST', body: JSON.stringify(task) }),
  moveTask: (id: string, lane: string) =>
    fetchApi<KanbanTask>(`/kanban/${id}/move`, { 
      method: 'POST', 
      body: JSON.stringify({ lane }) 
    }),
  approveTask: (id: string) =>
    fetchApi<KanbanTask>(`/kanban/${id}/approve`, { method: 'POST' }),
  rejectTask: (id: string, reason?: string) =>
    fetchApi<KanbanTask>(`/kanban/${id}/reject`, { 
      method: 'POST',
      body: JSON.stringify({ reason })
    }),
  deleteTask: (id: string) =>
    fetchApi<void>(`/kanban/${id}`, { method: 'DELETE' }),
};

// Soul Types (UI shape)
export interface SoulConfiguration {
  identity: string;
  directives: string[];
  tone: ToneConfiguration;
  boundaries: BoundaryConfiguration;
  workingHours?: WorkingHoursConfiguration;
  responseTemplates: Record<string, string>;
}

// Soul API response shape (camelCase from backend)
interface SoulConfigurationApi {
  name?: string;
  role?: string;
  primaryDirectives?: string[];
  tone?: {
    communicationStyle?: string;
    humorLevel?: string;
    verbosity?: string;
    formalityWithOthers?: string;
  };
  boundaries?: {
    topicsToAvoid?: string[];
    actionsNeverToTakeAutonomously?: string[];
    informationNeverToShare?: string[];
  };
  workingHours?: { activeHours?: string; urgentOverride?: string };
  responseTemplates?: Record<string, string>;
}

function mapSoulApiToConfig(api: SoulConfigurationApi | null): SoulConfiguration | null {
  if (!api) return null;
  const tone = api.tone;
  const boundaries = api.boundaries;
  const mustNotDo = [
    ...(boundaries?.topicsToAvoid ?? []),
    ...(boundaries?.actionsNeverToTakeAutonomously ?? []),
    ...(boundaries?.informationNeverToShare ?? []),
  ];
  const personality = [tone?.humorLevel, tone?.verbosity].filter(Boolean) as string[];
  return {
    identity: [api.name, api.role].filter(Boolean).join(' – ') || 'Botty',
    directives: api.primaryDirectives ?? [],
    tone: {
      style: tone?.communicationStyle ?? '',
      formality: tone?.formalityWithOthers ?? '',
      personality,
    },
    boundaries: {
      mustDo: [],
      mustNotDo,
      escalateTo: undefined,
    },
    workingHours: api.workingHours?.activeHours
      ? {
          timezone: 'UTC',
          start: '',
          end: '',
          offHoursMessage: api.workingHours.urgentOverride,
        }
      : undefined,
    responseTemplates: api.responseTemplates ?? {},
  };
}

export interface ToneConfiguration {
  style: string;
  formality: string;
  personality: string[];
}

export interface BoundaryConfiguration {
  mustDo: string[];
  mustNotDo: string[];
  escalateTo?: string;
}

export interface WorkingHoursConfiguration {
  timezone: string;
  start: string;
  end: string;
  offHoursMessage?: string;
}

export interface SoulVersion {
  version: number;
  id?: string;
  updatedAt: string;
  summary?: string;
}

interface SoulVersionApi {
  id?: string;
  changedBy?: string;
  createdAt?: string;
  isActive?: boolean;
}

// Soul API
export const soulApi = {
  getConfig: async () => {
    const api = await fetchApi<SoulConfigurationApi | null>('/soul');
    return mapSoulApiToConfig(api);
  },
  getMarkdown: async () => {
    const res = await fetchApi<{ content: string }>('/soul/markdown');
    return { markdown: res.content };
  },
  updateMarkdown: (markdown: string) =>
    fetchApi<{ message?: string }>('/soul/markdown', {
      method: 'PUT',
      body: JSON.stringify({ content: markdown })
    }),
  getSystemPrompt: async () => {
    const res = await fetchApi<{ prompt: string }>('/soul/system-prompt');
    return { systemPrompt: res.prompt };
  },
  getHistory: async () => {
    const raw = await fetchApi<SoulVersionApi[] | { versions: SoulVersion[] }>('/soul/history');
    const list = Array.isArray(raw)
      ? raw.map((v, i) => ({
          version: i + 1,
          id: v.id,
          updatedAt: v.createdAt ?? new Date().toISOString(),
          summary: v.changedBy,
        }))
      : (raw.versions ?? []);
    return { versions: list };
  },
  revertToVersion: (versionId: string) =>
    fetchApi<{ message?: string }>(`/soul/revert/${versionId}`, { method: 'POST' }),
};

// Skills Types
export interface Skill {
  id: string;
  name: string;
  description: string;
  toolCount: number;
  isConfigured: boolean;
}

export interface SkillConfigField {
  key: string;
  label: string;
  description?: string;
  type: string;
  isSensitive: boolean;
  isRequired: boolean;
  defaultValue?: string;
  options?: { value: string; label: string }[];
}

export interface SkillConfigSchema {
  skillId: string;
  fields: SkillConfigField[];
}

export interface SkillTool {
  name: string;
  description: string;
  parametersSchema: string;
}

// Skills API
export const skillsApi = {
  list: () => fetchApi<{ skills: Skill[] }>('/skills'),
  get: (id: string) => fetchApi<Skill & { tools: SkillTool[] }>(`/skills/${id}`),
  getSchema: (id: string) => fetchApi<SkillConfigSchema>(`/skills/${id}/config/schema`),
  getConfig: (id: string) => fetchApi<{ skillId: string; values: Record<string, string | null> }>(`/skills/${id}/config`),
  updateConfig: (id: string, values: Record<string, string>) =>
    fetchApi<void>(`/skills/${id}/config`, { method: 'PUT', body: JSON.stringify({ values }) }),
  validateConfig: (id: string) =>
    fetchApi<{ isValid: boolean; errors: { field: string; message: string }[] }>(`/skills/${id}/config/validate`, { method: 'POST' }),
  executeTool: (skillId: string, toolName: string, args: string) =>
    fetchApi<{ success: boolean; result?: string; error?: string }>(`/skills/${skillId}/execute`, {
      method: 'POST',
      body: JSON.stringify({ toolName, arguments: args })
    }),
  getAllTools: () => fetchApi<{ tools: SkillTool[] }>('/skills/tools'),
};

export interface OAuthAccount {
  id: string;
  provider: string;
  email: string;
  displayName?: string;
  externalAccountId?: string;
  scope?: string;
  createdAt: string;
  updatedAt: string;
  lastLinkedAt: string;
}

export interface OAuthProviderConfig {
  provider: string;
  configured: boolean;
  redirectUri?: string;
  scopes?: string[];
  clientId?: string;
  clientSecret?: string;
}

export const oauthApi = {
  getProviderConfig: (provider: string) =>
    fetchApi<OAuthProviderConfig>(`/oauth/providers/${provider}/config`),
  saveProviderConfig: (provider: string, request: { clientId: string; clientSecret: string; redirectUri: string; scopes: string[] }) =>
    fetchApi<void>(`/oauth/providers/${provider}/config`, {
      method: 'PUT',
      body: JSON.stringify(request),
    }),
  startLinkFlow: (provider: string, returnUrl?: string) =>
    fetchApi<{ authorizationUrl: string; state: string }>(`/oauth/providers/${provider}/start`, {
      method: 'POST',
      body: JSON.stringify({ returnUrl }),
    }),
  listAccounts: () => fetchApi<{ accounts: OAuthAccount[]; count: number }>('/oauth/accounts'),
  deleteAccount: (id: string) => fetchApi<void>(`/oauth/accounts/${id}`, { method: 'DELETE' }),
};

// Memory Types
export interface Memory {
  id: string;
  type: string;
  content: string;
  confidence: number;
  source: string;
  createdAt: string;
  lastAccessedAt?: string;
  accessCount: number;
  tags: string[];
}

// Memory API
export const memoryApi = {
  search: (query: string, type?: string, limit?: number) =>
    fetchApi<{ memories: Memory[] }>(`/memory/search?query=${encodeURIComponent(query)}${type ? `&type=${type}` : ''}${limit ? `&limit=${limit}` : ''}`),
  get: (id: string) => fetchApi<Memory>(`/memory/${id}`),
  delete: (id: string) => fetchApi<void>(`/memory/${id}`, { method: 'DELETE' }),
  getStats: () => fetchApi<{ totalCount: number; byType: Record<string, number> }>('/memory/stats'),
};

// Scheduler Types
export interface ScheduledTask {
  id: string;
  name: string;
  description?: string;
  cronExpression: string;
  taskType: string;
  taskPayload?: string;
  isEnabled: boolean;
  lastRunAt?: string;
  nextRunAt?: string;
  createdAt: string;
}

// Scheduler API
export const schedulerApi = {
  list: () => fetchApi<{ tasks: ScheduledTask[] }>('/scheduler/tasks'),
  get: (id: string) => fetchApi<ScheduledTask>(`/scheduler/tasks/${id}`),
  create: (task: Omit<ScheduledTask, 'id' | 'createdAt' | 'lastRunAt' | 'nextRunAt'>) =>
    fetchApi<ScheduledTask>('/scheduler/tasks', { method: 'POST', body: JSON.stringify(task) }),
  update: (id: string, task: Partial<ScheduledTask>) =>
    fetchApi<ScheduledTask>(`/scheduler/tasks/${id}`, { method: 'PUT', body: JSON.stringify(task) }),
  delete: (id: string) => fetchApi<void>(`/scheduler/tasks/${id}`, { method: 'DELETE' }),
  enable: (id: string) => fetchApi<ScheduledTask>(`/scheduler/tasks/${id}/enable`, { method: 'POST' }),
  disable: (id: string) => fetchApi<ScheduledTask>(`/scheduler/tasks/${id}/disable`, { method: 'POST' }),
  runNow: (id: string) => fetchApi<void>(`/scheduler/tasks/${id}/run`, { method: 'POST' }),
};

// WhatsApp Types
export interface WhatsAppStatus {
  connected: boolean;
  phoneNumber?: string;
  qrCode?: string;
  lastSeen?: string;
}

// WhatsApp API
export const whatsappApi = {
  getStatus: () => fetchApi<WhatsAppStatus>('/whatsapp/status'),
  getQrCode: () => fetchApi<{ qrCode: string }>('/whatsapp/qr'),
};

// Channels Types
export interface Channel {
  id: string;
  label: string;
  description: string;
  isEnabled: boolean;
  isConnected: boolean;
  accountId?: string;
  accountName?: string;
  connectedSince?: string;
  lastError?: string;
  capabilities: ChannelCapabilities;
}

export interface ChannelCapabilities {
  supportsMedia: boolean;
  supportsThreads: boolean;
  supportsReactions: boolean;
  supportsEdits: boolean;
  supportsDeletes: boolean;
  supportsVoiceNotes: boolean;
  maxMessageLength: number;
}

export interface ChannelDetail extends Channel {
  configSchema: ChannelConfigField[];
  config: Record<string, string>;
}

export interface ChannelConfigField {
  key: string;
  label: string;
  description?: string;
  type: string;
  isSensitive: boolean;
  isRequired: boolean;
  defaultValue?: string;
}

export interface ChannelStatus {
  channelId: string;
  isConnected: boolean;
  accountId?: string;
  accountName?: string;
  connectedSince?: string;
  error?: string;
}

// Channels API
export const channelsApi = {
  list: () => fetchApi<Channel[]>('/channels'),
  get: (id: string) => fetchApi<ChannelDetail>(`/channels/${id}`),
  getStatus: (id: string) => fetchApi<ChannelStatus>(`/channels/${id}/status`),
  connect: (id: string) => fetchApi<ChannelStatus>(`/channels/${id}/connect`, { method: 'POST' }),
  disconnect: (id: string) => fetchApi<void>(`/channels/${id}/disconnect`, { method: 'POST' }),
  updateConfig: (id: string, config: { enabled?: boolean; config?: Record<string, string> }) =>
    fetchApi<void>(`/channels/${id}/config`, { method: 'PUT', body: JSON.stringify(config) }),
  sendMessage: (id: string, chatId: string, text: string) =>
    fetchApi<{ success: boolean; messageId?: string; error?: string }>(`/channels/${id}/send`, {
      method: 'POST',
      body: JSON.stringify({ chatId, text })
    }),
};

// Hooks Types
export interface HookList {
  id: string;
  name: string;
  description?: string;
  trigger: string;
  actionType: string;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface HookDetail extends HookList {
  conditionJson?: string;
  actionConfigJson: string;
  createdBy?: string;
}

export interface HookExecution {
  id: string;
  hookId: string;
  trigger: string;
  success: boolean;
  output?: string;
  error?: string;
  durationMs: number;
  executedAt: string;
}

export interface HookResult {
  success: boolean;
  output?: string;
  error?: string;
  duration?: number;
}

// Hooks API
export const hooksApi = {
  list: () => fetchApi<HookList[]>('/hooks'),
  get: (id: string) => fetchApi<HookDetail>(`/hooks/${id}`),
  create: (body: { name: string; description?: string; trigger: string; conditionJson?: string; actionType: string; actionConfigJson: string; isEnabled?: boolean; createdBy?: string }) =>
    fetchApi<HookDetail>('/hooks', { method: 'POST', body: JSON.stringify(body) }),
  update: (id: string, body: Partial<{ name: string; description: string; trigger: string; conditionJson: string; actionType: string; actionConfigJson: string; isEnabled: boolean }>) =>
    fetchApi<HookDetail>(`/hooks/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  delete: (id: string) => fetchApi<void>(`/hooks/${id}`, { method: 'DELETE' }),
  test: (id: string, payload?: object) =>
    fetchApi<HookResult>(`/hooks/${id}/test`, { method: 'POST', body: JSON.stringify(payload ?? {}) }),
  enable: (id: string) => fetchApi<HookDetail>(`/hooks/${id}/enable`, { method: 'POST' }),
  disable: (id: string) => fetchApi<HookDetail>(`/hooks/${id}/disable`, { method: 'POST' }),
  logs: (id: string, limit?: number) =>
    fetchApi<HookExecution[]>(`/hooks/${id}/logs${limit != null ? `?limit=${limit}` : ''}`),
  triggers: () => fetchApi<string[]>('/hooks/triggers'),
};

// Health
export const healthApi = {
  check: () => fetchApi<{ status: string; timestamp: string }>('/health'),
};

// Feed (merged timeline)
export interface FeedMessage {
  id: string;
  conversationId: string;
  source: string;
  externalId: string | null;
  role: string;
  content: string;
  senderId: string | null;
  senderName: string | null;
  createdAt: string;
}

export interface FeedResponse {
  messages: FeedMessage[];
}

export const feedApi = {
  getFeed: (since?: string) =>
    fetchApi<FeedResponse>(
      '/feed' + (since ? `?since=${encodeURIComponent(since)}` : '')
    ),
};

// Chat (assistant)
export interface ChatMessageDto {
  role: string;
  content: string;
}

export interface ChatRequest {
  messages: ChatMessageDto[];
  conversationId?: string;
  userId?: string;
  includeMemory?: boolean;
  extractMemories?: boolean;
}

export interface ChatResponse {
  content: string;
  conversationId?: string;
  toolCalls?: Array<{ id: string; name: string; arguments?: string }>;
  finishReason?: string;
  usage?: { promptTokens: number; completionTokens: number; totalTokens: number };
  memoryExtractionTriggered?: boolean;
  memoriesInjected?: number;
}

export const chatApi = {
  chat: (request: ChatRequest) =>
    fetchApi<ChatResponse>('/chat', {
      method: 'POST',
      body: JSON.stringify(request),
    }),
};

// Streaming WebSocket event types
export interface AssistantDeltaEvent {
  type: 'assistant_delta';
  conversationId: string;
  messageId: string;
  delta: string;
}

export interface AssistantDoneEvent {
  type: 'assistant_done';
  conversationId: string;
  messageId: string;
  content: string;
  usage?: { promptTokens: number; completionTokens: number; totalTokens: number };
  finishReason?: string;
}

/** WebSocket URL for feed (new_message events). Derives from API base. */
export function getFeedWebSocketUrl(): string {
  const base = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5001/api';
  const url = new URL(base);
  const wsProtocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
  const origin = `${wsProtocol}//${url.host}`;
  return `${origin}/ws/feed`;
}
