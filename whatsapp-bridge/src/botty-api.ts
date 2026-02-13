import axios, { AxiosInstance, AxiosError } from 'axios';
import { config } from './config.js';
import { logger } from './logger.js';
import type { WhatsAppMessage, QueueMessageRequest, ForwardMessageRequest } from './types.js';

/**
 * Client for communicating with the Botty API
 */
export class BottyApiClient {
  private client: AxiosInstance;

  constructor() {
    this.client = axios.create({
      baseURL: config.bottyApiUrl,
      headers: {
        'Content-Type': 'application/json',
        ...(config.bottyApiKey && { 'X-API-Key': config.bottyApiKey }),
      },
      timeout: 30000,
    });

    // Request logging
    this.client.interceptors.request.use((request) => {
      logger.debug(`API Request: ${request.method?.toUpperCase()} ${request.url}`);
      return request;
    });

    // Response logging
    this.client.interceptors.response.use(
      (response) => {
        logger.debug(`API Response: ${response.status} ${response.config.url}`);
        return response;
      },
      (error: AxiosError) => {
        logger.error(`API Error: ${error.response?.status} ${error.config?.url} - ${error.message}`);
        return Promise.reject(error);
      }
    );
  }

  /**
   * Forwards a received WhatsApp message to Botty API
   */
  async forwardMessage(message: WhatsAppMessage): Promise<void> {
    const request: ForwardMessageRequest = {
      messageId: message.id,
      from: message.from,
      fromName: message.senderName || message.from,
      body: message.body,
      timestamp: new Date(message.timestamp * 1000).toISOString(),
      isGroup: message.isGroup,
      groupName: message.groupName,
    };

    try {
      await this.client.post('/api/whatsapp/messages/incoming', request);
      logger.info(`Message ${message.id} forwarded to Botty API`);
    } catch (error) {
      logger.error(`Failed to forward message ${message.id}:`, error);
      throw error;
    }
  }

  /**
   * Sends an incoming message to the Botty feed (merged timeline + WebSocket broadcast).
   */
  async sendFeedIncoming(message: WhatsAppMessage): Promise<void> {
    const payload = {
      channelId: 'whatsapp',
      chatId: message.from,
      messageId: message.id,
      senderId: message.from,
      senderName: message.senderName || message.from.split('@')[0],
      text: message.body,
      timestamp: new Date(message.timestamp * 1000).toISOString(),
    };

    try {
      await this.client.post('/api/feed/incoming', payload);
      logger.debug(`Feed incoming: ${message.id} from ${message.from}`);
    } catch (error) {
      logger.error(`Failed to send feed incoming for message ${message.id}:`, error);
      throw error;
    }
  }

  /**
   * Sends an incoming message to the channel webhook for auto-reply processing.
   */
  async sendChannelWebhook(message: WhatsAppMessage): Promise<void> {
    const payload = {
      messageId: message.id,
      from: message.from,
      fromName: message.senderName || message.from.split('@')[0],
      body: message.body,
      timestamp: new Date(message.timestamp * 1000).toISOString(),
      isGroup: message.isGroup,
      groupName: message.groupName,
      conversationId: message.from,
    };

    try {
      await this.client.post('/api/channels/whatsapp/webhook', payload);
      logger.debug(`Channel webhook: ${message.id} from ${message.from}`);
    } catch (error) {
      logger.error(`Failed to send channel webhook for message ${message.id}:`, error);
    }
  }

  /**
   * Creates a Kanban task for an incoming message
   */
  async createMessageTask(message: WhatsAppMessage): Promise<string> {
    const senderDisplay = message.senderName || message.from.split('@')[0];

    const request: QueueMessageRequest = {
      to: message.from,
      body: message.body,
      recipientName: senderDisplay,
      replyToMessageId: message.id,
      priority: 'Normal',
    };

    try {
      const response = await this.client.post<{ taskId: string; status: string }>(
        '/api/whatsapp/messages/queue',
        request
      );
      logger.info(`Created task ${response.data.taskId} for message ${message.id}`);
      return response.data.taskId;
    } catch (error) {
      logger.error(`Failed to create task for message ${message.id}:`, error);
      throw error;
    }
  }

  /**
   * Notifies Botty API that a message was sent successfully
   */
  async notifyMessageSent(
    taskId: string,
    message: WhatsAppMessage
  ): Promise<void> {
    try {
      await this.client.post(`/api/whatsapp/messages/sent`, {
        taskId,
        messageId: message.id,
        to: message.to,
        body: message.body,
        timestamp: new Date(message.timestamp * 1000).toISOString(),
      });
      logger.info(`Notified Botty API of sent message ${message.id}`);
    } catch (error) {
      logger.error(`Failed to notify sent message ${message.id}:`, error);
      throw error;
    }
  }

  /**
   * Gets pending outbound messages from the approval queue
   */
  async getPendingOutboundMessages(): Promise<PendingOutboundMessage[]> {
    try {
      const response = await this.client.get<PendingOutboundMessage[]>(
        '/api/whatsapp/messages/pending'
      );
      return response.data;
    } catch (error) {
      logger.error('Failed to get pending outbound messages:', error);
      throw error;
    }
  }

  /**
   * Health check for Botty API
   */
  async healthCheck(): Promise<boolean> {
    try {
      const response = await this.client.get('/health');
      return response.status === 200;
    } catch {
      return false;
    }
  }
}

/**
 * Pending outbound message from approval queue
 */
export interface PendingOutboundMessage {
  taskId: string;
  to: string;
  body: string;
  replyToMessageId?: string;
  approvedAt: string;
}
