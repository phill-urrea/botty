/**
 * Connection state of the WhatsApp client
 */
export type ConnectionState = 'disconnected' | 'connecting' | 'qr_pending' | 'authenticated' | 'ready';

/**
 * WhatsApp message received from the client
 */
export interface WhatsAppMessage {
  id: string;
  chatId: string;
  senderId: string;
  from: string;
  to: string;
  body: string;
  timestamp: number;
  isGroup: boolean;
  groupName?: string;
  senderName?: string;
  hasMedia: boolean;
  mediaType?: string;
  isForwarded: boolean;
  quotedMessageId?: string;
}

/**
 * Message to send via WhatsApp
 */
export interface OutgoingMessage {
  to: string;
  body: string;
  quotedMessageId?: string;
}

/**
 * Contact information
 */
export interface Contact {
  id: string;
  name: string;
  pushName?: string;
  isGroup: boolean;
  isMyContact: boolean;
}

/**
 * Chat information
 */
export interface Chat {
  id: string;
  name: string;
  isGroup: boolean;
  unreadCount: number;
  lastMessage?: {
    body: string;
    timestamp: number;
    fromMe: boolean;
  };
}

/**
 * Status response
 */
export interface StatusResponse {
  state: ConnectionState;
  isReady: boolean;
  phoneNumber?: string;
  connectedAt?: string;
}

/**
 * QR Code event data
 */
export interface QRCodeEvent {
  qr: string;
  timestamp: string;
}

/**
 * WebSocket event types
 */
export type WSEventType =
  | 'connection_state'
  | 'qr_code'
  | 'message_received'
  | 'message_sent'
  | 'message_ack'
  | 'error';

/**
 * WebSocket event payload
 */
export interface WSEvent {
  type: WSEventType;
  data: unknown;
  timestamp: string;
}

/**
 * Botty API WhatsApp queue request
 */
export interface QueueMessageRequest {
  to: string;
  body: string;
  recipientName?: string;
  replyToMessageId?: string;
  priority?: string;
}

/**
 * Botty API message forward request
 */
export interface ForwardMessageRequest {
  messageId: string;
  from: string;
  fromName: string;
  body: string;
  timestamp: string;
  isGroup: boolean;
  groupName?: string;
  conversationId?: string;
}
