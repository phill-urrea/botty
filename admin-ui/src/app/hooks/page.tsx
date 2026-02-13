'use client';

import { useEffect, useState, useCallback } from 'react';
import { Header } from '@/components/layout/header';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { hooksApi, HookList, HookDetail, HookExecution } from '@/lib/api';
import { Webhook, Plus, Play, Trash2, ToggleLeft, ToggleRight, List, AlertCircle } from 'lucide-react';
import { formatDate } from '@/lib/utils';

export default function HooksPage() {
  const [hooks, setHooks] = useState<HookList[]>([]);
  const [triggers, setTriggers] = useState<string[]>([]);
  const [selected, setSelected] = useState<HookDetail | null>(null);
  const [logs, setLogs] = useState<HookExecution[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [testing, setTesting] = useState<string | null>(null);

  const loadHooks = useCallback(async () => {
    try {
      setError(null);
      const [listRes, triggersRes] = await Promise.all([
        hooksApi.list(),
        hooksApi.triggers().catch(() => []),
      ]);
      setHooks(Array.isArray(listRes) ? listRes : []);
      setTriggers(Array.isArray(triggersRes) ? triggersRes : []);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load hooks');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadHooks();
  }, [loadHooks]);

  const selectHook = useCallback(async (id: string) => {
    try {
      const detail = await hooksApi.get(id);
      setSelected(detail);
      const logList = await hooksApi.logs(id, 30);
      setLogs(Array.isArray(logList) ? logList : []);
    } catch {
      setSelected(null);
      setLogs([]);
    }
  }, []);

  const handleTest = async (id: string) => {
    setTesting(id);
    try {
      await hooksApi.test(id, { test: true });
      await selectHook(id);
    } finally {
      setTesting(null);
    }
  };

  const handleToggle = async (id: string, enabled: boolean) => {
    try {
      if (enabled) await hooksApi.disable(id);
      else await hooksApi.enable(id);
      await loadHooks();
      if (selected?.id === id) await selectHook(id);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to update');
    }
  };

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this hook?')) return;
    try {
      await hooksApi.delete(id);
      setSelected(null);
      setLogs([]);
      await loadHooks();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to delete');
    }
  };

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" />
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      <Header title="Hooks" description="Event-driven automation and webhooks" />

      {error && (
        <div className="mx-6 mt-4 p-3 rounded-lg bg-red-50 text-red-800 border border-red-200 flex items-center gap-2">
          <AlertCircle className="h-4 w-4" />
          {error}
        </div>
      )}

      <div className="flex-1 flex overflow-hidden">
        <div className="w-96 border-r border-gray-200 flex flex-col bg-white">
          <div className="p-4 border-b border-gray-200 flex items-center justify-between">
            <h2 className="font-semibold text-gray-900">Hooks</h2>
            <Button size="sm" onClick={() => setShowCreate(true)}>
              <Plus className="h-4 w-4 mr-1" />
              Add
            </Button>
          </div>
          <div className="flex-1 overflow-auto p-2">
            {hooks.length === 0 ? (
              <p className="text-sm text-gray-600 p-4">No hooks yet. Create one or receive webhooks at POST /api/webhooks/{'{hookId}'}</p>
            ) : (
              hooks.map((h) => (
                <button
                  key={h.id}
                  onClick={() => selectHook(h.id)}
                  className={`w-full text-left p-3 rounded-lg mb-1 transition-colors ${
                    selected?.id === h.id ? 'bg-blue-50 border border-blue-200' : 'hover:bg-gray-50'
                  }`}
                >
                  <div className="font-medium text-gray-900">{h.name}</div>
                  <div className="flex items-center gap-2 mt-1">
                    <Badge variant={h.isEnabled ? 'success' : 'secondary'} className="text-xs">
                      {h.isEnabled ? 'On' : 'Off'}
                    </Badge>
                    <span className="text-xs text-gray-600">{h.trigger}</span>
                  </div>
                </button>
              ))
            )}
          </div>
        </div>

        <div className="flex-1 overflow-auto p-6">
          {selected ? (
            <div className="max-w-2xl space-y-6">
              <Card>
                <CardHeader className="flex flex-row items-center justify-between">
                  <div>
                    <CardTitle className="text-gray-900">{selected.name}</CardTitle>
                    <CardDescription>{selected.description || 'No description'}</CardDescription>
                  </div>
                  <div className="flex gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => handleToggle(selected.id, selected.isEnabled)}
                    >
                      {selected.isEnabled ? <ToggleRight className="h-4 w-4" /> : <ToggleLeft className="h-4 w-4" />}
                      {selected.isEnabled ? 'Disable' : 'Enable'}
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => handleTest(selected.id)}
                      disabled={testing === selected.id}
                    >
                      <Play className="h-4 w-4" />
                      {testing === selected.id ? 'Testing...' : 'Test'}
                    </Button>
                    <Button variant="outline" size="sm" onClick={() => handleDelete(selected.id)}>
                      <Trash2 className="h-4 w-4 text-red-600" />
                    </Button>
                  </div>
                </CardHeader>
                <CardContent className="text-sm text-gray-700 space-y-2">
                  <div><span className="text-gray-500">Trigger:</span> {selected.trigger}</div>
                  <div><span className="text-gray-500">Action:</span> {selected.actionType}</div>
                  <pre className="mt-2 p-3 bg-gray-50 rounded text-xs overflow-auto max-h-40">{selected.actionConfigJson}</pre>
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-gray-900">
                    <List className="h-5 w-5" />
                    Recent runs
                  </CardTitle>
                  <CardDescription>Last 30 executions</CardDescription>
                </CardHeader>
                <CardContent>
                  {logs.length === 0 ? (
                    <p className="text-gray-600 text-sm">No executions yet.</p>
                  ) : (
                    <ul className="space-y-2">
                      {logs.map((log) => (
                        <li key={log.id} className="flex items-center justify-between text-sm py-2 border-b border-gray-100 last:border-0">
                          <div className="flex items-center gap-2">
                            <span className={`w-2 h-2 rounded-full ${log.success ? 'bg-green-500' : 'bg-red-500'}`} />
                            <span className="text-gray-700">{formatDate(log.executedAt)}</span>
                            <span className="text-gray-500">{log.durationMs}ms</span>
                          </div>
                          {log.error && <span className="text-red-600 truncate max-w-xs">{log.error}</span>}
                        </li>
                      ))}
                    </ul>
                  )}
                </CardContent>
              </Card>
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center h-full text-gray-600">
              <Webhook className="h-12 w-12 mb-4" />
              <p className="font-medium text-gray-900">Select a hook</p>
              <p className="text-sm mt-1">Or create one to run actions on events (e.g. TaskMoved, WebhookReceived).</p>
              <p className="text-xs mt-4 text-gray-500">Webhook URL: POST {typeof window !== 'undefined' ? `${window.location.origin.replace(':3000', ':5001')}/api/webhooks/` : ''}{'{hookId}'}</p>
            </div>
          )}
        </div>
      </div>

      {showCreate && (
        <CreateHookModal
          triggers={triggers}
          onClose={() => setShowCreate(false)}
          onCreated={() => { setShowCreate(false); loadHooks(); }}
        />
      )}
    </div>
  );
}

function CreateHookModal({
  triggers,
  onClose,
  onCreated,
}: {
  triggers: string[];
  onClose: () => void;
  onCreated: () => void;
}) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [trigger, setTrigger] = useState('WebhookReceived');
  const [actionType, setActionType] = useState('create_task');
  const [actionConfig, setActionConfig] = useState('{\n  "title": "Hook-created task",\n  "description": "From webhook"\n}');
  const [saving, setSaving] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErr(null);
    setSaving(true);
    try {
      let parsed: string;
      try {
        parsed = JSON.stringify(JSON.parse(actionConfig));
      } catch {
        setErr('Invalid JSON in action config');
        return;
      }
      await hooksApi.create({
        name: name || 'Unnamed hook',
        description: description || undefined,
        trigger,
        actionType,
        actionConfigJson: parsed,
        isEnabled: true,
      });
      onCreated();
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Failed to create');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50" onClick={onClose}>
      <div className="bg-white rounded-lg shadow-xl max-w-lg w-full mx-4 max-h-[90vh] overflow-auto" onClick={(e) => e.stopPropagation()}>
        <div className="p-6 border-b border-gray-200">
          <h2 className="text-lg font-semibold text-gray-900">Create hook</h2>
          <p className="text-sm text-gray-600 mt-1">Runs an action when an event occurs.</p>
        </div>
        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          {err && <div className="p-2 rounded bg-red-50 text-red-800 text-sm">{err}</div>}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Name</label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="w-full border border-gray-300 rounded px-3 py-2 text-sm"
              placeholder="e.g. Urgent email alert"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
            <input
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              className="w-full border border-gray-300 rounded px-3 py-2 text-sm"
              placeholder="Optional"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Trigger</label>
            <select
              value={trigger}
              onChange={(e) => setTrigger(e.target.value)}
              className="w-full border border-gray-300 rounded px-3 py-2 text-sm"
            >
              {triggers.map((t) => (
                <option key={t} value={t}>{t}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Action type</label>
            <select
              value={actionType}
              onChange={(e) => setActionType(e.target.value)}
              className="w-full border border-gray-300 rounded px-3 py-2 text-sm"
            >
              <option value="create_task">create_task</option>
              <option value="send_message">send_message</option>
              <option value="http_callback">http_callback</option>
              <option value="execute_skill">execute_skill</option>
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Action config (JSON)</label>
            <textarea
              value={actionConfig}
              onChange={(e) => setActionConfig(e.target.value)}
              rows={8}
              className="w-full border border-gray-300 rounded px-3 py-2 text-sm font-mono"
            />
          </div>
          <div className="flex justify-end gap-2 pt-4">
            <Button type="button" variant="outline" onClick={onClose}>Cancel</Button>
            <Button type="submit" disabled={saving}>{saving ? 'Creating...' : 'Create'}</Button>
          </div>
        </form>
      </div>
    </div>
  );
}
