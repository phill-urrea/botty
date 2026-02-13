import pkg from 'whatsapp-web.js';
const { Client, LocalAuth } = pkg;
import type { Message, MessageAck, Chat as WAChat } from 'whatsapp-web.js';
import qrcode from 'qrcode-terminal';
import { EventEmitter } from 'events';
import { config } from './config.js';
import { logger } from './logger.js';
import type {
  ConnectionState,
  WhatsAppMessage,
  OutgoingMessage,
  Contact,
  Chat,
  StatusResponse,
} from './types.js';

/**
 * WhatsApp Web.js client wrapper with event handling
 */
export class WhatsAppClient extends EventEmitter {
  private client: InstanceType<typeof Client>;
  private state: ConnectionState = 'disconnected';
  private currentQR: string | null = null;
  private phoneNumber: string | null = null;
  private activeSends = new Set<string>();
  private connectedAt: Date | null = null;

  constructor() {
    super();

    // Use account-specific session path for multi-account support
    const sessionPath = config.accountId !== 'default'
      ? `${config.sessionPath}/${config.accountId}`
      : config.sessionPath;

    this.client = new Client({
      authStrategy: new LocalAuth({
        dataPath: sessionPath,
      }),
      puppeteer: {
        headless: config.headless,
        args: [
          '--no-sandbox',
          '--disable-setuid-sandbox',
          '--disable-dev-shm-usage',
          '--disable-accelerated-2d-canvas',
          '--no-first-run',
          '--no-zygote',
          '--disable-gpu',
          '--disable-crash-reporter',
          '--disable-breakpad',
          '--disable-features=ProcessSingleton',
        ],
      },
    });

    this.setupEventHandlers();
  }

  /**
   * Sets up all WhatsApp client event handlers
   */
  private setupEventHandlers(): void {
    // QR Code event
    this.client.on('qr', (qr: string) => {
      this.state = 'qr_pending';
      this.currentQR = qr;
      
      logger.info('QR Code received, scan with WhatsApp');
      qrcode.generate(qr, { small: true });
      
      this.emit('qr_code', { qr, timestamp: new Date().toISOString() });
      this.emit('state_change', this.state);
    });

    // Authentication success
    this.client.on('authenticated', () => {
      this.state = 'authenticated';
      this.currentQR = null;
      
      logger.info('WhatsApp authenticated successfully');
      
      this.emit('authenticated');
      this.emit('state_change', this.state);
    });

    // Authentication failure
    this.client.on('auth_failure', (message: string) => {
      this.state = 'disconnected';
      
      logger.error(`WhatsApp authentication failed: ${message}`);
      
      this.emit('auth_failure', message);
      this.emit('state_change', this.state);
    });

    // Client ready
    this.client.on('ready', async () => {
      this.state = 'ready';
      this.connectedAt = new Date();
      
      // Get phone number
      const info = this.client.info;
      this.phoneNumber = info?.wid?.user || null;
      
      logger.info(`WhatsApp client ready. Phone: ${this.phoneNumber}`);
      
      this.emit('ready');
      this.emit('state_change', this.state);
    });

    // Disconnected
    this.client.on('disconnected', (reason: string) => {
      this.state = 'disconnected';
      this.connectedAt = null;
      
      logger.warn(`WhatsApp disconnected: ${reason}`);
      
      this.emit('disconnected', reason);
      this.emit('state_change', this.state);
    });

    // Incoming message
    this.client.on('message', async (message: Message) => {
      try {
        const parsedMessage = await this.parseMessage(message);
        
        logger.info(`Message received from ${parsedMessage.from}: ${parsedMessage.body.substring(0, 50)}...`);
        
        this.emit('message', parsedMessage);
      } catch (error) {
        logger.error('Error processing incoming message:', error);
      }
    });

    // Message acknowledgment
    this.client.on('message_ack', (message: Message, ack: MessageAck) => {
      this.emit('message_ack', {
        messageId: message.id._serialized,
        ack,
        timestamp: new Date().toISOString(),
      });
    });

    // Message creation (sent messages)
    this.client.on('message_create', async (message: Message) => {
      if (message.fromMe) {
        try {
          const parsedMessage = await this.parseMessage(message);
          this.emit('message_sent', parsedMessage);
        } catch (error) {
          logger.error('Error processing sent message:', error);
        }
      }
    });
  }

  /**
   * Parses a WhatsApp message into our standard format
   */
  private async parseMessage(message: Message): Promise<WhatsAppMessage> {
    let isGroup = false;
    let groupName: string | undefined;
    let senderName: string = message.from;

    try {
      const chat = await message.getChat();
      isGroup = chat.isGroup;
      groupName = chat.isGroup ? chat.name : undefined;
    } catch (error) {
      logger.warn(`Failed to getChat for message ${message.id._serialized}: ${error}`);
    }

    try {
      const contact = await message.getContact();
      senderName = contact.pushname || contact.name || message.from;
    } catch (error) {
      logger.warn(`Failed to getContact for message ${message.id._serialized}: ${error}`);
    }

    let quotedMessageId: string | undefined;
    try {
      if (message.hasQuotedMsg) {
        const quoted = await message.getQuotedMessage();
        quotedMessageId = quoted?.id._serialized;
      }
    } catch (error) {
      logger.warn(`Failed to get quoted message: ${error}`);
    }

    const chatId = message.fromMe ? message.to : message.from;
    const senderId = isGroup ? (message.author || message.from) : message.from;

    return {
      id: message.id._serialized,
      chatId,
      senderId,
      from: message.from,
      to: message.to,
      body: message.body,
      timestamp: message.timestamp,
      isGroup,
      groupName,
      senderName,
      hasMedia: message.hasMedia,
      mediaType: message.type !== 'chat' ? message.type : undefined,
      isForwarded: message.isForwarded,
      quotedMessageId,
    };
  }

  /**
   * Initializes and starts the WhatsApp client
   */
  async initialize(): Promise<void> {
    this.state = 'connecting';
    this.emit('state_change', this.state);
    
    logger.info('Initializing WhatsApp client...');
    
    try {
      await this.client.initialize();
    } catch (error) {
      this.state = 'disconnected';
      this.emit('state_change', this.state);
      throw error;
    }
  }

  /**
   * Destroys the WhatsApp client
   */
  async destroy(): Promise<void> {
    logger.info('Destroying WhatsApp client...');
    await this.client.destroy();
    this.state = 'disconnected';
    this.emit('state_change', this.state);
  }

  /**
   * Logs out from WhatsApp (clears session)
   */
  async logout(): Promise<void> {
    logger.info('Logging out from WhatsApp...');
    await this.client.logout();
    this.state = 'disconnected';
    this.phoneNumber = null;
    this.connectedAt = null;
    this.emit('state_change', this.state);
  }

  /**
   * Sends a message
   */
  async sendMessage(outgoing: OutgoingMessage): Promise<WhatsAppMessage> {
    if (this.state !== 'ready') {
      throw new Error(`Cannot send message: client is ${this.state}`);
    }

    logger.info(`Sending message to ${outgoing.to}`);

    // Mark chatId BEFORE sending so the message_create event sees it
    this.activeSends.add(outgoing.to);

    const sentMessage = await this.client.sendMessage(outgoing.to, outgoing.body, {
      quotedMessageId: outgoing.quotedMessageId,
    });

    const parsed = await this.parseMessage(sentMessage);
    // Keep the flag briefly so the async message_create event is caught
    setTimeout(() => this.activeSends.delete(outgoing.to), 5000);
    return parsed;
  }

  /**
   * Sends a poll message
   */
  async sendPoll(
    to: string,
    question: string,
    options: string[],
    maxSelections: number = 1,
    quotedMessageId?: string,
  ): Promise<WhatsAppMessage> {
    if (this.state !== 'ready') {
      throw new Error(`Cannot send poll: client is ${this.state}`);
    }

    logger.info(`Sending poll to ${to}: "${question}" with ${options.length} options`);

    const { Poll } = pkg;
    const poll = new Poll(question, options, {
      allowMultipleAnswers: maxSelections > 1,
    } as any);

    const sentMessage = await this.client.sendMessage(to, poll, {
      quotedMessageId,
    } as any);

    const parsed = await this.parseMessage(sentMessage);
    this.activeSends.add(to);
    setTimeout(() => this.activeSends.delete(to), 5000);
    return parsed;
  }

  /**
   * Checks if the bot is currently sending to a chat (to filter out message_create echoes).
   */
  isBotSending(chatId: string): boolean {
    return this.activeSends.has(chatId);
  }

  /**
   * Gets all chats
   */
  async getChats(): Promise<Chat[]> {
    if (this.state !== 'ready') {
      throw new Error(`Cannot get chats: client is ${this.state}`);
    }

    const chats = await this.client.getChats();

    return chats.map((chat: WAChat) => ({
      id: chat.id._serialized,
      name: chat.name,
      isGroup: chat.isGroup,
      unreadCount: chat.unreadCount,
      lastMessage: chat.lastMessage
        ? {
            body: chat.lastMessage.body,
            timestamp: chat.lastMessage.timestamp,
            fromMe: chat.lastMessage.fromMe,
          }
        : undefined,
    }));
  }

  /**
   * Gets contacts
   */
  async getContacts(): Promise<Contact[]> {
    if (this.state !== 'ready') {
      throw new Error(`Cannot get contacts: client is ${this.state}`);
    }

    const contacts = await this.client.getContacts();

    return contacts.map((contact: { id: { _serialized: string; user?: string }; name?: string; pushname?: string; isGroup?: boolean; isMyContact?: boolean }) => ({
      id: contact.id._serialized,
      name: (contact.name || contact.pushname || contact.id.user) ?? '',
      pushName: contact.pushname,
      isGroup: contact.isGroup ?? false,
      isMyContact: contact.isMyContact ?? false,
    }));
  }

  /**
   * Gets chat by ID
   */
  async getChatById(chatId: string): Promise<Chat | null> {
    if (this.state !== 'ready') {
      throw new Error(`Cannot get chat: client is ${this.state}`);
    }

    try {
      const chat = await this.client.getChatById(chatId);
      return {
        id: chat.id._serialized,
        name: chat.name,
        isGroup: chat.isGroup,
        unreadCount: chat.unreadCount,
        lastMessage: chat.lastMessage
          ? {
              body: chat.lastMessage.body,
              timestamp: chat.lastMessage.timestamp,
              fromMe: chat.lastMessage.fromMe,
            }
          : undefined,
      };
    } catch {
      return null;
    }
  }

  /**
   * Sends a typing indicator (composing state) to a chat
   */
  async sendTypingIndicator(chatId: string): Promise<void> {
    if (this.state !== 'ready') {
      throw new Error(`Cannot send typing indicator: client is ${this.state}`);
    }

    const chat = await this.client.getChatById(chatId);
    await chat.sendStateTyping();
  }

  /**
   * Gets current status
   */
  getStatus(): StatusResponse {
    return {
      state: this.state,
      isReady: this.state === 'ready',
      phoneNumber: this.phoneNumber || undefined,
      connectedAt: this.connectedAt?.toISOString(),
    };
  }

  /**
   * Gets current QR code (if in qr_pending state)
   */
  getCurrentQR(): string | null {
    return this.currentQR;
  }

  /**
   * Gets the current connection state
   */
  getState(): ConnectionState {
    return this.state;
  }
}
