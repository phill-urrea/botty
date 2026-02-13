import express, { Express, Request, Response, NextFunction } from 'express';
import cors from 'cors';
import { config } from './config.js';
import { logger } from './logger.js';
import { WhatsAppClient } from './whatsapp-client.js';
import type { OutgoingMessage } from './types.js';

/**
 * Creates and configures the Express API server
 */
export function createApi(whatsapp: WhatsAppClient): Express {
  const app = express();

  // Middleware
  app.use(cors());
  app.use(express.json());

  // Request logging
  app.use((req: Request, _res: Response, next: NextFunction) => {
    logger.debug(`${req.method} ${req.path}`);
    next();
  });

  // Health check
  app.get('/health', (_req: Request, res: Response) => {
    res.json({
      status: 'healthy',
      whatsapp: whatsapp.getStatus(),
      timestamp: new Date().toISOString(),
    });
  });

  // Get WhatsApp connection status
  app.get('/status', (_req: Request, res: Response) => {
    res.json({
      ...whatsapp.getStatus(),
      accountId: config.accountId,
    });
  });

  // Get current QR code (for authentication)
  app.get('/qr', (_req: Request, res: Response) => {
    const qr = whatsapp.getCurrentQR();
    if (qr) {
      res.json({
        qr,
        state: whatsapp.getState(),
        timestamp: new Date().toISOString(),
      });
    } else {
      const state = whatsapp.getState();
      if (state === 'ready') {
        res.status(200).json({
          message: 'Already authenticated',
          state,
        });
      } else {
        res.status(404).json({
          message: 'No QR code available',
          state,
        });
      }
    }
  });

  // Get QR code as image (for display in browser/app)
  app.get('/qr/image', async (_req: Request, res: Response) => {
    const qr = whatsapp.getCurrentQR();
    if (qr) {
      try {
        // Dynamic import for qrcode
        const QRCode = await import('qrcode');
        const qrImage = await QRCode.default.toDataURL(qr);
        res.json({
          image: qrImage,
          state: whatsapp.getState(),
        });
      } catch (error) {
        logger.error('Error generating QR image:', error);
        res.status(500).json({ error: 'Failed to generate QR image' });
      }
    } else {
      res.status(404).json({
        message: 'No QR code available',
        state: whatsapp.getState(),
      });
    }
  });

  // Send a message
  app.post('/send', async (req: Request, res: Response) => {
    try {
      const { to, body, quotedMessageId } = req.body as OutgoingMessage;

      if (!to || !body) {
        res.status(400).json({ error: 'Missing required fields: to, body' });
        return;
      }

      const message = await whatsapp.sendMessage({ to, body, quotedMessageId });
      res.json({
        success: true,
        message,
      });
    } catch (error) {
      logger.error('Error sending message:', error);
      res.status(500).json({
        error: error instanceof Error ? error.message : 'Failed to send message',
      });
    }
  });

  // Send a poll
  app.post('/send-poll', async (req: Request, res: Response) => {
    try {
      const { to, question, options, maxSelections, replyTo } = req.body;

      if (!to || !question || !options || !Array.isArray(options) || options.length < 2) {
        res.status(400).json({
          error: 'Missing required fields: to, question, options (array with 2+ items)',
        });
        return;
      }

      const message = await whatsapp.sendPoll(to, question, options, maxSelections || 1, replyTo);
      res.json({
        success: true,
        message,
      });
    } catch (error) {
      logger.error('Error sending poll:', error);
      res.status(500).json({
        error: error instanceof Error ? error.message : 'Failed to send poll',
      });
    }
  });

  // Send typing indicator
  app.post('/typing', async (req: Request, res: Response) => {
    try {
      const { chatId } = req.body;

      if (!chatId) {
        res.status(400).json({ error: 'Missing required field: chatId' });
        return;
      }

      await whatsapp.sendTypingIndicator(chatId);
      res.json({ success: true });
    } catch (error) {
      logger.error('Error sending typing indicator:', error);
      res.status(500).json({
        error: error instanceof Error ? error.message : 'Failed to send typing indicator',
      });
    }
  });

  // Get all chats
  app.get('/chats', async (_req: Request, res: Response) => {
    try {
      const chats = await whatsapp.getChats();
      res.json(chats);
    } catch (error) {
      logger.error('Error getting chats:', error);
      res.status(500).json({
        error: error instanceof Error ? error.message : 'Failed to get chats',
      });
    }
  });

  // Get specific chat
  app.get('/chats/:chatId', async (req: Request, res: Response) => {
    try {
      const chatId = Array.isArray(req.params.chatId) ? req.params.chatId[0] : req.params.chatId;
      if (!chatId) {
        res.status(404).json({ error: 'Chat not found' });
        return;
      }
      const chat = await whatsapp.getChatById(chatId);
      if (chat) {
        res.json(chat);
      } else {
        res.status(404).json({ error: 'Chat not found' });
      }
    } catch (error) {
      logger.error('Error getting chat:', error);
      res.status(500).json({
        error: error instanceof Error ? error.message : 'Failed to get chat',
      });
    }
  });

  // Get all contacts
  app.get('/contacts', async (_req: Request, res: Response) => {
    try {
      const contacts = await whatsapp.getContacts();
      res.json(contacts);
    } catch (error) {
      logger.error('Error getting contacts:', error);
      res.status(500).json({
        error: error instanceof Error ? error.message : 'Failed to get contacts',
      });
    }
  });

  // Logout (clear session)
  app.post('/logout', async (_req: Request, res: Response) => {
    try {
      await whatsapp.logout();
      res.json({ success: true, message: 'Logged out successfully' });
    } catch (error) {
      logger.error('Error logging out:', error);
      res.status(500).json({
        error: error instanceof Error ? error.message : 'Failed to logout',
      });
    }
  });

  // Restart client
  app.post('/restart', async (_req: Request, res: Response) => {
    try {
      await whatsapp.destroy();
      await whatsapp.initialize();
      res.json({ success: true, message: 'Client restarted' });
    } catch (error) {
      logger.error('Error restarting client:', error);
      res.status(500).json({
        error: error instanceof Error ? error.message : 'Failed to restart',
      });
    }
  });

  // Error handler
  app.use((err: Error, _req: Request, res: Response, _next: NextFunction) => {
    logger.error('Unhandled error:', err);
    res.status(500).json({ error: 'Internal server error' });
  });

  return app;
}
