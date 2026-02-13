import { WebSocketServer, WebSocket } from 'ws';
import { Server } from 'http';
import { config } from './config.js';
import { logger } from './logger.js';
import type { WSEvent, WSEventType } from './types.js';

/**
 * WebSocket server for real-time events
 */
export class WSServer {
  private wss: WebSocketServer;
  private clients: Set<WebSocket> = new Set();
  private heartbeatInterval: NodeJS.Timeout | null = null;

  constructor(server: Server) {
    this.wss = new WebSocketServer({ server, path: '/ws/events' });

    this.wss.on('connection', (ws: WebSocket) => {
      this.handleConnection(ws);
    });

    this.startHeartbeat();

    logger.info('WebSocket server initialized');
  }

  /**
   * Handles new WebSocket connection
   */
  private handleConnection(ws: WebSocket): void {
    this.clients.add(ws);
    logger.info(`WebSocket client connected. Total clients: ${this.clients.size}`);

    // Send initial connection event
    this.sendToClient(ws, 'connection_state', { connected: true });

    ws.on('message', (data: Buffer) => {
      try {
        const message = JSON.parse(data.toString());
        this.handleMessage(ws, message);
      } catch (error) {
        logger.error('Invalid WebSocket message:', error);
      }
    });

    ws.on('close', () => {
      this.clients.delete(ws);
      logger.info(`WebSocket client disconnected. Total clients: ${this.clients.size}`);
    });

    ws.on('error', (error) => {
      logger.error('WebSocket error:', error);
      this.clients.delete(ws);
    });

    ws.on('pong', () => {
      // Client is alive
    });
  }

  /**
   * Handles incoming WebSocket message
   */
  private handleMessage(ws: WebSocket, message: unknown): void {
    // Handle ping/pong or other client messages
    if (typeof message === 'object' && message !== null && 'type' in message) {
      const msg = message as { type: string };
      if (msg.type === 'ping') {
        this.sendToClient(ws, 'connection_state', { pong: true });
      }
    }
  }

  /**
   * Sends an event to a specific client
   */
  private sendToClient(ws: WebSocket, type: WSEventType, data: unknown): void {
    if (ws.readyState === WebSocket.OPEN) {
      const event: WSEvent = {
        type,
        data,
        timestamp: new Date().toISOString(),
      };
      ws.send(JSON.stringify(event));
    }
  }

  /**
   * Broadcasts an event to all connected clients
   */
  broadcast(type: WSEventType, data: unknown): void {
    const event: WSEvent = {
      type,
      data,
      timestamp: new Date().toISOString(),
    };
    const message = JSON.stringify(event);

    this.clients.forEach((client) => {
      if (client.readyState === WebSocket.OPEN) {
        client.send(message);
      }
    });

    logger.debug(`Broadcast ${type} to ${this.clients.size} clients`);
  }

  /**
   * Starts heartbeat to detect dead connections
   */
  private startHeartbeat(): void {
    this.heartbeatInterval = setInterval(() => {
      this.clients.forEach((ws) => {
        if (ws.readyState === WebSocket.OPEN) {
          ws.ping();
        }
      });
    }, config.wsHeartbeatInterval);
  }

  /**
   * Stops the heartbeat and closes all connections
   */
  close(): void {
    if (this.heartbeatInterval) {
      clearInterval(this.heartbeatInterval);
    }

    this.clients.forEach((client) => {
      client.close();
    });

    this.wss.close();
    logger.info('WebSocket server closed');
  }

  /**
   * Gets the number of connected clients
   */
  getClientCount(): number {
    return this.clients.size;
  }
}
