'use client';

import { useEffect, useState, useCallback, useRef } from 'react';
import { Header } from '@/components/layout/header';
import { Button } from '@/components/ui/button';
import { feedApi, chatApi, getFeedWebSocketUrl, type FeedMessage } from '@/lib/api';
import { MessageSquare, Send, User, Bot, Radio } from 'lucide-react';

function formatTime(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  } catch {
    return '';
  }
}

function messageLabel(msg: FeedMessage): string {
  if (msg.source === 'admin' && msg.role === 'user') return 'You';
  if (msg.role === 'assistant') return 'Assistant';
  if (msg.source !== 'admin' && (msg.role === 'user' || msg.role === 'thirdparty')) {
    if (msg.senderName) return `${msg.source}: ${msg.senderName}`;
    if (msg.senderId) return `${msg.source}: ${msg.senderId}`;
    if (msg.externalId) return `${msg.source}: ${msg.externalId}`;
    return msg.source;
  }
  if (msg.senderName) return `${msg.source}: ${msg.senderName}`;
  if (msg.senderId) return `${msg.source}: ${msg.senderId}`;
  if (msg.externalId) return `${msg.source}: ${msg.externalId}`;
  return msg.source;
}

function MessageIcon({ role, source }: { role: string; source: string }) {
  if (role === 'user') return <User className="h-4 w-4 text-gray-700" />;
  if (role === 'assistant') return <Bot className="h-4 w-4 text-blue-600" />;
  return <Radio className="h-4 w-4 text-green-700" />;
}

export default function ChatPage() {
  const [messages, setMessages] = useState<FeedMessage[]>([]);
  const [loading, setLoading] = useState(true);
  const [input, setInput] = useState('');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [wsConnected, setWsConnected] = useState(false);
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [isAssistantTyping, setIsAssistantTyping] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);
  const wsRef = useRef<WebSocket | null>(null);

  const mergeMessage = useCallback((msg: FeedMessage) => {
    setMessages((prev) => {
      if (prev.some((m) => m.id === msg.id)) return prev;
      return [...prev, msg].sort(
        (a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()
      );
    });
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        const res = await feedApi.getFeed();
        if (!cancelled) setMessages(res.messages || []);
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load feed');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    const url = getFeedWebSocketUrl();
    const ws = new WebSocket(url);
    wsRef.current = ws;

    ws.onopen = () => setWsConnected(true);
    ws.onclose = () => setWsConnected(false);
    ws.onerror = () => setWsConnected(false);
    ws.onmessage = (event) => {
      try {
        const data = JSON.parse(event.data as string);
        if (data?.type === 'new_message' && data?.message) {
          const m = data.message as FeedMessage;
          mergeMessage(m);
        } else if (data?.type === 'typing_indicator') {
          setIsAssistantTyping(data.isTyping);
        } else if (data?.type === 'assistant_delta') {
          const { messageId, delta } = data as { messageId: string; delta: string };
          setIsAssistantTyping(false);
          setMessages((prev) =>
            prev.map((m) =>
              m.id === messageId ? { ...m, content: m.content + delta } : m
            )
          );
        } else if (data?.type === 'assistant_done') {
          const { messageId, content } = data as { messageId: string; content: string };
          setMessages((prev) =>
            prev.map((m) =>
              m.id === messageId ? { ...m, content } : m
            )
          );
          setIsAssistantTyping(false);
        }
      } catch {
        // ignore parse errors
      }
    };

    return () => {
      ws.close();
      wsRef.current = null;
    };
  }, [mergeMessage]);

  // Auto-clear typing indicator after 30 seconds (safety net)
  useEffect(() => {
    if (!isAssistantTyping) return;
    const timer = setTimeout(() => setIsAssistantTyping(false), 30000);
    return () => clearTimeout(timer);
  }, [isAssistantTyping]);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' });
  }, [messages, isAssistantTyping]);

  const handleSend = async () => {
    const text = input.trim();
    if (!text || sending) return;

    setInput('');
    setSending(true);
    setIsAssistantTyping(true);
    setError(null);

    try {
      const res = await chatApi.chat({
        messages: [{ role: 'user', content: text }],
        conversationId: conversationId ?? undefined,
        includeMemory: true,
        extractMemories: true,
      });
      if (res.conversationId) setConversationId(res.conversationId);
      // Streaming via WebSocket handles real-time message delivery;
      // no need to refetch feed here.
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to send');
      setInput(text);
    } finally {
      setSending(false);
      // Don't clear isAssistantTyping here — assistant_done event will clear it
    }
  };

  return (
    <div className="flex h-full flex-col">
      <Header
        title="Assistant & channels"
        description="Merged feed: talk to the assistant and see channel conversations"
      />

      <div className="flex flex-1 flex-col overflow-hidden border-t border-gray-200 bg-gray-50">
        <div className="flex items-center gap-2 border-b border-gray-200 bg-white px-4 py-2">
          {wsConnected ? (
            <span className="text-xs font-medium text-green-700">Live</span>
          ) : (
            <span className="text-xs font-medium text-amber-700">Reconnecting…</span>
          )}
        </div>

        <div
          ref={scrollRef}
          className="flex-1 overflow-y-auto p-4 space-y-3"
        >
          {loading && (
            <div className="flex items-center justify-center py-8">
              <div className="h-8 w-8 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
            </div>
          )}
          {!loading && messages.length === 0 && (
            <div className="flex flex-col items-center justify-center py-12 text-gray-800">
              <MessageSquare className="h-12 w-12 mb-2 text-gray-600" />
              <p className="text-gray-800">No messages yet. Say something to the assistant below.</p>
            </div>
          )}
          {!loading &&
            messages
              .filter((msg) => msg.role !== 'assistant' || msg.content)
              .map((msg) => (
              <div
                key={msg.id}
                className={`flex gap-3 rounded-lg p-3 ${
                  msg.role === 'user' ? 'bg-blue-50 ml-8' : 'bg-white border border-gray-100 mr-8'
                }`}
              >
                <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-gray-200">
                  <MessageIcon role={msg.role} source={msg.source} />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2 text-xs text-gray-700">
                    <span className="font-medium text-gray-900">{messageLabel(msg)}</span>
                    <span className="text-gray-700">{formatTime(msg.createdAt)}</span>
                    {msg.source !== 'admin' && (
                      <span className="rounded bg-gray-300 px-1.5 py-0.5 text-gray-800 font-medium">{msg.source}</span>
                    )}
                  </div>
                  <p className="mt-0.5 whitespace-pre-wrap break-words text-sm text-gray-900">
                    {msg.content}
                  </p>
                </div>
              </div>
            ))}
          {isAssistantTyping && (
            <div className="flex gap-3 rounded-lg p-3 bg-white border border-gray-100 mr-8 animate-pulse">
              <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-gray-200">
                <Bot className="h-4 w-4 text-blue-600" />
              </div>
              <div className="flex items-center gap-1 py-2">
                <span className="h-2 w-2 rounded-full bg-gray-400 animate-bounce [animation-delay:0ms]" />
                <span className="h-2 w-2 rounded-full bg-gray-400 animate-bounce [animation-delay:150ms]" />
                <span className="h-2 w-2 rounded-full bg-gray-400 animate-bounce [animation-delay:300ms]" />
              </div>
            </div>
          )}
        </div>

        {error && (
          <div className="mx-4 mb-2 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">
            {error}
          </div>
        )}

        <div className="border-t border-gray-200 bg-white p-4">
          <div className="flex gap-2">
            <textarea
              className="min-h-[44px] flex-1 resize-none rounded-lg border border-gray-300 px-3 py-2 text-sm text-gray-900 placeholder:text-gray-600 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
              placeholder="Message the assistant…"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault();
                  handleSend();
                }
              }}
              rows={1}
              disabled={sending}
            />
            <Button
              onClick={handleSend}
              disabled={sending || !input.trim()}
              className="shrink-0"
            >
              <Send className="h-4 w-4" />
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
