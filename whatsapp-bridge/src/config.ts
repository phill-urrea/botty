import dotenv from 'dotenv';

dotenv.config();

export const config = {
  // Server settings
  port: parseInt(process.env.PORT || '3001', 10),
  host: process.env.HOST || '0.0.0.0',

  // Botty API settings
  bottyApiUrl: process.env.BOTTY_API_URL || 'http://localhost:5001',
  bottyApiKey: process.env.BOTTY_API_KEY || '',

  // Account identification (for multi-account deployments)
  accountId: process.env.ACCOUNT_ID || 'default',

  // WhatsApp settings
  sessionPath: process.env.SESSION_PATH || './.wwebjs_auth',
  headless: process.env.HEADLESS !== 'false',

  // Logging
  logLevel: process.env.LOG_LEVEL || 'info',

  // WebSocket
  wsHeartbeatInterval: parseInt(process.env.WS_HEARTBEAT_INTERVAL || '30000', 10),

  // Message handling
  autoCreateTasks: process.env.AUTO_CREATE_TASKS !== 'false',
  allowedContacts: process.env.ALLOWED_CONTACTS?.split(',').map(c => c.trim()) || [],
};

export type Config = typeof config;
