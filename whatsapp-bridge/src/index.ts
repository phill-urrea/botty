import { createServer } from 'http';
import { config } from './config.js';
import { logger } from './logger.js';
import { WhatsAppClient } from './whatsapp-client.js';
import { BottyApiClient } from './botty-api.js';
import { WSServer } from './websocket-server.js';
import { createApi } from './api.js';
import type { WhatsAppMessage } from './types.js';

/**
 * Main application entry point
 */
async function main(): Promise<void> {
  logger.info('Starting Botty WhatsApp Bridge...');
  logger.info(`Configuration: Port=${config.port}, Botty API=${config.bottyApiUrl}`);

  // Create WhatsApp client
  const whatsapp = new WhatsAppClient();

  // Create Botty API client
  const bottyApi = new BottyApiClient();

  // Create Express app
  const app = createApi(whatsapp);

  // Create HTTP server
  const server = createServer(app);

  // Create WebSocket server
  const wsServer = new WSServer(server);

  // Wire up WhatsApp events to WebSocket broadcast
  whatsapp.on('state_change', (state) => {
    wsServer.broadcast('connection_state', { state });
  });

  whatsapp.on('qr_code', (data) => {
    wsServer.broadcast('qr_code', data);
  });

  whatsapp.on('message', async (message: WhatsAppMessage) => {
    // Skip messages that are echoes of bot-sent replies (e.g. messaging yourself)
    if (whatsapp.isBotSending(message.from)) {
      logger.debug(`Skipping incoming echo of bot-sent message to ${message.from}`);
      return;
    }

    // Broadcast to WebSocket clients
    wsServer.broadcast('message_received', message);

    // Always push to Botty feed (merged timeline + admin UI) when API is configured
    if (config.bottyApiUrl) {
      try {
        await bottyApi.sendFeedIncoming(message);
      } catch (error) {
        logger.error('Failed to send message to feed:', error);
      }

      // Trigger channel webhook for auto-reply (security filter + LLM response)
      try {
        await bottyApi.sendChannelWebhook(message);
      } catch (error) {
        logger.error('Failed to send channel webhook:', error);
      }
    }

    // Optionally create Kanban task for the assistant
    if (config.autoCreateTasks) {
      try {
        if (config.allowedContacts.length > 0) {
          const isAllowed = config.allowedContacts.some(
            (c) => message.from.includes(c) || message.senderName?.includes(c)
          );
          if (!isAllowed) {
            logger.debug(`Message from ${message.from} not in allowed contacts, skipping task creation`);
            return;
          }
        }

        await bottyApi.createMessageTask(message);
      } catch (error) {
        logger.error('Failed to create task for incoming message:', error);
      }
    }
  });

  whatsapp.on('message_sent', async (message: WhatsAppMessage) => {
    wsServer.broadcast('message_sent', message);

    // Forward user-typed messages (not bot API replies) so the LLM can respond
    if (!whatsapp.isBotSending(message.to) && config.bottyApiUrl) {
      // For fromMe messages, the conversation partner is message.to
      const adjusted: WhatsAppMessage = { ...message, from: message.to };

      try {
        await bottyApi.sendFeedIncoming(adjusted);
      } catch (error) {
        logger.error('Failed to send own message to feed:', error);
      }

      try {
        await bottyApi.sendChannelWebhook(adjusted);
      } catch (error) {
        logger.error('Failed to send own message to channel webhook:', error);
      }
    }
  });

  whatsapp.on('message_ack', (ack) => {
    wsServer.broadcast('message_ack', ack);
  });

  // Graceful shutdown
  const shutdown = async (signal: string): Promise<void> => {
    logger.info(`Received ${signal}, shutting down...`);

    wsServer.close();

    try {
      await whatsapp.destroy();
    } catch (error) {
      logger.error('Error destroying WhatsApp client:', error);
    }

    server.close(() => {
      logger.info('Server closed');
      process.exit(0);
    });

    // Force exit after 10 seconds
    setTimeout(() => {
      logger.warn('Forced exit after timeout');
      process.exit(1);
    }, 10000);
  };

  process.on('SIGINT', () => shutdown('SIGINT'));
  process.on('SIGTERM', () => shutdown('SIGTERM'));

  // Start HTTP server
  server.listen(config.port, config.host, () => {
    logger.info(`HTTP server listening on ${config.host}:${config.port}`);
    logger.info(`WebSocket endpoint: ws://${config.host}:${config.port}/ws/events`);
  });

  // Check Botty API health (retry a few times so API can finish starting after depends_on)
  if (config.bottyApiUrl) {
    const maxAttempts = 10;
    const delayMs = 3000;
    let bottyHealthy = false;
    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
      bottyHealthy = await bottyApi.healthCheck();
      if (bottyHealthy) {
        logger.info('Botty API is healthy');
        break;
      }
      if (attempt < maxAttempts) {
        logger.debug(`Botty API not ready (attempt ${attempt}/${maxAttempts}), retrying in ${delayMs / 1000}s...`);
        await new Promise((r) => setTimeout(r, delayMs));
      }
    }
    if (!bottyHealthy) {
      logger.warn('Botty API is not reachable - messages will not be forwarded');
    }
  }

  // Initialize WhatsApp client
  try {
    await whatsapp.initialize();
  } catch (error) {
    logger.error('Failed to initialize WhatsApp client:', error);
    logger.info('You can restart the client via POST /restart');
  }
}

// Run the application
main().catch((error) => {
  logger.error('Fatal error:', error);
  process.exit(1);
});
