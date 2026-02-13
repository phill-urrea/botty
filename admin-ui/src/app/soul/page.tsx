'use client';

import { useEffect, useState, useCallback } from 'react';
import dynamic from 'next/dynamic';
import { Header } from '@/components/layout/header';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { soulApi, SoulConfiguration, SoulVersion } from '@/lib/api';
import { Save, RotateCcw, Eye, History, AlertCircle, Check } from 'lucide-react';
import { formatDate } from '@/lib/utils';

// Dynamically import Monaco to avoid SSR issues
const MonacoEditor = dynamic(() => import('@monaco-editor/react'), { ssr: false });

export default function SoulPage() {
  const [markdown, setMarkdown] = useState('');
  const [originalMarkdown, setOriginalMarkdown] = useState('');
  const [config, setConfig] = useState<SoulConfiguration | null>(null);
  const [versions, setVersions] = useState<SoulVersion[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [showPreview, setShowPreview] = useState(false);
  const [showHistory, setShowHistory] = useState(false);

  const loadSoul = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [markdownRes, configRes, historyRes] = await Promise.all([
        soulApi.getMarkdown().catch(() => ({ markdown: getDefaultSoulMd() })),
        soulApi.getConfig().catch(() => null),
        soulApi.getHistory().catch(() => ({ versions: [] })),
      ]);
      
      setMarkdown(markdownRes.markdown);
      setOriginalMarkdown(markdownRes.markdown);
      setConfig(configRes);
      setVersions(historyRes.versions || []);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load Soul configuration');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadSoul();
  }, [loadSoul]);

  const handleSave = async () => {
    try {
      setSaving(true);
      setError(null);
      setSuccess(null);
      await soulApi.updateMarkdown(markdown);
      setOriginalMarkdown(markdown);
      setSuccess('Soul configuration saved successfully');
      setTimeout(() => setSuccess(null), 3000);
      loadSoul(); // Reload to get new version
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save');
    } finally {
      setSaving(false);
    }
  };

  const handleRevert = async (versionId: string) => {
    try {
      setLoading(true);
      setError(null);
      await soulApi.revertToVersion(versionId);
      loadSoul();
      setShowHistory(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to revert');
    } finally {
      setLoading(false);
    }
  };

  const hasChanges = markdown !== originalMarkdown;

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" />
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      <Header 
        title="Soul Configuration" 
        description="Define your assistant's personality and behavior" 
      />

      <div className="p-4 border-b border-gray-200 bg-white flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Button
            variant={showPreview ? 'default' : 'outline'}
            onClick={() => setShowPreview(!showPreview)}
          >
            <Eye className="h-4 w-4 mr-2" />
            {showPreview ? 'Edit' : 'Preview'}
          </Button>
          <Button
            variant="outline"
            onClick={() => setShowHistory(!showHistory)}
          >
            <History className="h-4 w-4 mr-2" />
            History
          </Button>
        </div>
        <div className="flex items-center gap-2">
          {hasChanges && (
            <Badge variant="warning">Unsaved changes</Badge>
          )}
          <Button
            variant="outline"
            onClick={() => setMarkdown(originalMarkdown)}
            disabled={!hasChanges}
          >
            <RotateCcw className="h-4 w-4 mr-2" />
            Reset
          </Button>
          <Button onClick={handleSave} disabled={saving || !hasChanges}>
            <Save className="h-4 w-4 mr-2" />
            {saving ? 'Saving...' : 'Save'}
          </Button>
        </div>
      </div>

      {(error || success) && (
        <div className={`mx-6 mt-4 p-3 rounded-lg flex items-center gap-2 ${
          error ? 'bg-red-50 text-red-800 border border-red-200' : 'bg-green-50 text-green-800 border border-green-200'
        }`}>
          {error ? <AlertCircle className="h-4 w-4" /> : <Check className="h-4 w-4" />}
          {error || success}
        </div>
      )}

      <div className="flex-1 flex overflow-hidden">
        {/* Editor / Preview */}
        <div className={`flex-1 ${showHistory ? 'w-2/3' : 'w-full'}`}>
          {showPreview ? (
            <div className="h-full overflow-auto p-6">
              <Card>
                <CardHeader>
                  <CardTitle>Parsed Configuration</CardTitle>
                  <CardDescription>
                    Preview of how your Soul.md will be interpreted
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  {config ? (
                    <div className="space-y-6">
                      <div>
                        <h4 className="font-semibold text-sm text-gray-700 uppercase mb-2">Identity</h4>
                        <p>{config.identity ?? '—'}</p>
                      </div>
                      <div>
                        <h4 className="font-semibold text-sm text-gray-700 uppercase mb-2">Directives</h4>
                        <ul className="list-disc list-inside space-y-1">
                          {(config.directives ?? []).map((d, i) => (
                            <li key={i}>{d}</li>
                          ))}
                        </ul>
                      </div>
                      <div>
                        <h4 className="font-semibold text-sm text-gray-700 uppercase mb-2">Tone</h4>
                        <div className="grid grid-cols-3 gap-4">
                          <div>
                            <span className="text-sm text-gray-700">Style:</span>
                            <p className="font-medium">{config.tone?.style ?? '—'}</p>
                          </div>
                          <div>
                            <span className="text-sm text-gray-700">Formality:</span>
                            <p className="font-medium">{config.tone?.formality ?? '—'}</p>
                          </div>
                          <div>
                            <span className="text-sm text-gray-700">Personality:</span>
                            <div className="flex flex-wrap gap-1 mt-1">
                              {(config.tone?.personality ?? []).map((p, i) => (
                                <Badge key={i} variant="secondary">{p}</Badge>
                              ))}
                            </div>
                          </div>
                        </div>
                      </div>
                      <div>
                        <h4 className="font-semibold text-sm text-gray-700 uppercase mb-2">Boundaries</h4>
                        <div className="grid grid-cols-2 gap-4">
                          <div>
                            <span className="text-sm text-green-600">Must Do:</span>
                            <ul className="list-disc list-inside text-sm">
                              {(config.boundaries?.mustDo ?? []).map((d, i) => (
                                <li key={i}>{d}</li>
                              ))}
                            </ul>
                          </div>
                          <div>
                            <span className="text-sm text-red-600">Must Not Do:</span>
                            <ul className="list-disc list-inside text-sm">
                              {(config.boundaries?.mustNotDo ?? []).map((d, i) => (
                                <li key={i}>{d}</li>
                              ))}
                            </ul>
                          </div>
                        </div>
                      </div>
                    </div>
                  ) : (
                    <p className="text-gray-600">Unable to parse configuration</p>
                  )}
                </CardContent>
              </Card>
            </div>
          ) : (
            <div className="h-full">
              <MonacoEditor
                height="100%"
                language="markdown"
                theme="vs-light"
                value={markdown}
                onChange={(value) => setMarkdown(value || '')}
                options={{
                  minimap: { enabled: false },
                  fontSize: 14,
                  lineNumbers: 'on',
                  wordWrap: 'on',
                  padding: { top: 16 },
                }}
              />
            </div>
          )}
        </div>

        {/* History Panel */}
        {showHistory && (
          <div className="w-1/3 border-l border-gray-200 overflow-auto bg-white">
            <div className="p-4 border-b border-gray-200">
              <h3 className="font-semibold">Version History</h3>
            </div>
            <div className="p-4 space-y-2">
              {versions.length > 0 ? (
                versions.map((version) => (
                  <div
                    key={version.version}
                    className="p-3 border border-gray-200 rounded-lg hover:bg-gray-50"
                  >
                    <div className="flex items-center justify-between">
                      <span className="font-medium">Version {version.version}</span>
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() => version.id && handleRevert(version.id)}
                        disabled={!version.id}
                      >
                        Restore
                      </Button>
                    </div>
                    <p className="text-sm text-gray-600 mt-1">
                      {formatDate(version.updatedAt)}
                    </p>
                    {version.summary && (
                      <p className="text-sm text-gray-600 mt-1">{version.summary}</p>
                    )}
                  </div>
                ))
              ) : (
                <p className="text-gray-600 text-sm">No version history available</p>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function getDefaultSoulMd(): string {
  return `# Soul Configuration

## Identity
You are Botty, a helpful personal AI assistant.

## Directives
- Help the user with their daily tasks
- Be proactive but not intrusive
- Always ask for approval before taking important actions
- Remember context from previous conversations

## Tone
- Style: conversational
- Formality: casual
- Personality: helpful, friendly, efficient

## Boundaries

### Must Do
- Get approval before sending messages
- Get approval before modifying calendar events
- Protect user privacy

### Must Not Do
- Share personal information with third parties
- Make financial decisions without approval
- Send messages without explicit approval

## Response Templates

### Greeting
Hello! How can I help you today?

### Confirmation
I'll need your approval to proceed with this action.
`;
}
