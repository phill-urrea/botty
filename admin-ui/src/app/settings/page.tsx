'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Header } from '@/components/layout/header';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { Textarea } from '@/components/ui/textarea';
import { oauthApi, skillsApi, OAuthAccount } from '@/lib/api';
import { Settings, Database, Server, Shield, Key, Link, Trash2 } from 'lucide-react';

export default function SettingsPage() {
  const defaultScopes = useMemo(
    () => [
      'openid',
      'email',
      'profile',
      'https://www.googleapis.com/auth/gmail.readonly',
      'https://www.googleapis.com/auth/gmail.send',
      'https://www.googleapis.com/auth/gmail.modify',
      'https://www.googleapis.com/auth/calendar',
      'https://www.googleapis.com/auth/calendar.events',
    ],
    []
  );
  const [clientId, setClientId] = useState('');
  const [clientSecret, setClientSecret] = useState('');
  const [redirectUri, setRedirectUri] = useState('');
  const [scopesText, setScopesText] = useState(defaultScopes.join('\n'));
  const [accounts, setAccounts] = useState<OAuthAccount[]>([]);
  const [gmailDefault, setGmailDefault] = useState('');
  const [calendarDefault, setCalendarDefault] = useState('');
  const [saving, setSaving] = useState(false);
  const [linking, setLinking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    const [provider, linked, gmailConfig, calendarConfig] = await Promise.all([
      oauthApi.getProviderConfig('google'),
      oauthApi.listAccounts(),
      skillsApi.getConfig('gmail'),
      skillsApi.getConfig('google-calendar'),
    ]);

    setRedirectUri(
      provider.redirectUri || `${window.location.origin.replace(':3000', ':5001')}/api/oauth/providers/google/callback`
    );
    setScopesText((provider.scopes && provider.scopes.length > 0 ? provider.scopes : defaultScopes).join('\n'));
    setAccounts(linked.accounts || []);
    setGmailDefault((gmailConfig.values.default_account as string) || '');
    setCalendarDefault((calendarConfig.values.default_account as string) || '');
  }, [defaultScopes]);

  useEffect(() => {
    load().catch((err) => setError(err instanceof Error ? err.message : 'Failed to load settings'));
    const params = new URLSearchParams(window.location.search);
    if (params.get('oauth') === 'success') {
      setMessage(`Linked ${params.get('email') ?? 'account'} successfully.`);
    }
  }, [load]);

  const saveProviderConfig = async () => {
    try {
      setSaving(true);
      setError(null);
      const scopes = scopesText
        .split(/\r?\n|,/)
        .map((s) => s.trim())
        .filter(Boolean);
      await oauthApi.saveProviderConfig('google', { clientId, clientSecret, redirectUri, scopes });
      setMessage('Google OAuth provider configuration saved.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save OAuth provider settings');
    } finally {
      setSaving(false);
    }
  };

  const linkGoogleAccount = async () => {
    try {
      setLinking(true);
      setError(null);
      const returnUrl = `${window.location.origin}/settings`;
      const res = await oauthApi.startLinkFlow('google', returnUrl);
      window.location.href = res.authorizationUrl;
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to start OAuth flow');
      setLinking(false);
    }
  };

  const removeAccount = async (id: string) => {
    try {
      await oauthApi.deleteAccount(id);
      await load();
      setMessage('Linked account removed.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to remove account');
    }
  };

  const saveDefaults = async () => {
    try {
      setSaving(true);
      await Promise.all([
        skillsApi.updateConfig('gmail', { default_account: gmailDefault }),
        skillsApi.updateConfig('google-calendar', { default_account: calendarDefault }),
      ]);
      setMessage('Default accounts updated.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save default account settings');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="flex flex-col h-full">
      <Header title="Settings" description="Configure system settings" />

      <div className="flex-1 overflow-auto p-6">
        <div className="max-w-2xl space-y-6">
          {/* API Configuration */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <Server className="h-5 w-5" />
                API Configuration
              </CardTitle>
              <CardDescription>
                Configure the connection to the Botty API backend
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  API URL
                </label>
                <Input
                  value={process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5001/api'}
                  disabled
                  className="bg-gray-50"
                />
                <p className="text-xs text-gray-600 mt-1">
                  Set via NEXT_PUBLIC_API_URL environment variable
                </p>
              </div>
            </CardContent>
          </Card>

          {/* Database Status */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <Database className="h-5 w-5" />
                Database
              </CardTitle>
              <CardDescription>
                PostgreSQL database connection status
              </CardDescription>
            </CardHeader>
            <CardContent>
              <div className="flex items-center justify-between">
                <span className="text-gray-600">Connection Status</span>
                <Badge variant="success">Connected</Badge>
              </div>
            </CardContent>
          </Card>

          {/* Security */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <Shield className="h-5 w-5" />
                Security
              </CardTitle>
              <CardDescription>
                Security and authentication settings
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="flex items-center justify-between">
                <div>
                  <span className="font-medium">Secret Store</span>
                  <p className="text-sm text-gray-600">Where sensitive data is stored</p>
                </div>
                <Badge variant="outline">Local Environment</Badge>
              </div>
              <div className="flex items-center justify-between">
                <div>
                  <span className="font-medium">Approval Required</span>
                  <p className="text-sm text-gray-600">Actions require explicit approval</p>
                </div>
                <Badge variant="success">Enabled</Badge>
              </div>
            </CardContent>
          </Card>

          {/* API Keys */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <Key className="h-5 w-5" />
                OAuth Account Linking
              </CardTitle>
              <CardDescription>
                Configure provider credentials and link multiple Google accounts for Gmail and Calendar.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              {error && <p className="text-sm text-red-600">{error}</p>}
              {message && <p className="text-sm text-green-700">{message}</p>}

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Google OAuth Client ID</label>
                <Input value={clientId} onChange={(e) => setClientId(e.target.value)} placeholder="Google client ID" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Google OAuth Client Secret</label>
                <Input
                  type="password"
                  value={clientSecret}
                  onChange={(e) => setClientSecret(e.target.value)}
                  placeholder="Google client secret"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Redirect URI</label>
                <Input value={redirectUri} onChange={(e) => setRedirectUri(e.target.value)} />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Scopes (one per line)</label>
                <Textarea value={scopesText} onChange={(e) => setScopesText(e.target.value)} rows={6} />
              </div>

              <div className="flex items-center gap-2">
                <Button onClick={saveProviderConfig} disabled={saving}>
                  {saving ? 'Saving...' : 'Save OAuth Provider'}
                </Button>
                <Button onClick={linkGoogleAccount} variant="outline" disabled={linking}>
                  <Link className="h-4 w-4 mr-2" />
                  {linking ? 'Redirecting...' : 'Connect Google Account'}
                </Button>
              </div>

              <div className="pt-2">
                <div className="font-medium mb-2">Linked Accounts</div>
                {accounts.length === 0 ? (
                  <p className="text-sm text-gray-600">No linked accounts yet.</p>
                ) : (
                  <div className="space-y-2">
                    {accounts.map((account) => (
                      <div key={account.id} className="flex items-center justify-between border rounded p-2">
                        <div>
                          <div className="text-sm font-medium">{account.displayName || account.email}</div>
                          <div className="text-xs text-gray-600">{account.email}</div>
                        </div>
                        <Button variant="outline" size="sm" onClick={() => removeAccount(account.id)}>
                          <Trash2 className="h-4 w-4" />
                        </Button>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              <div className="pt-2 space-y-3">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Default Gmail Account</label>
                  <Input
                    value={gmailDefault}
                    onChange={(e) => setGmailDefault(e.target.value)}
                    placeholder="name@example.com"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Default Calendar Account</label>
                  <Input
                    value={calendarDefault}
                    onChange={(e) => setCalendarDefault(e.target.value)}
                    placeholder="name@example.com"
                  />
                </div>
                <Button variant="outline" onClick={saveDefaults} disabled={saving}>
                  Save Default Accounts
                </Button>
              </div>
            </CardContent>
          </Card>

          {/* System Info */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <Settings className="h-5 w-5" />
                System Information
              </CardTitle>
            </CardHeader>
            <CardContent>
              <div className="space-y-2 text-sm">
                <div className="flex justify-between">
                  <span className="text-gray-600">Admin UI Version</span>
                  <span>1.0.0</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-gray-600">Next.js Version</span>
                  <span>14.x</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-gray-600">Backend</span>
                  <span>.NET 10</span>
                </div>
              </div>
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}
